using System.IO.Compression;
using System.Security.Cryptography;

namespace ClaudeLauncher;

/// <summary>How far the background install has got.</summary>
public enum InstallStage
{
    /// <summary>Nothing has been asked for yet.</summary>
    Idle,

    /// <summary>Pulling the release zip down.</summary>
    Downloading,

    /// <summary>Hash checked, files going into place.</summary>
    Installing,

    /// <summary>The new build is on disk and starts next time.</summary>
    Installed,

    /// <summary>It did not work, and the old build is untouched.</summary>
    Failed
}

/// <summary>
/// Puts a newer release on disk while the launcher carries on running.
///
/// The trick that makes this possible is that Windows will happily *rename* a
/// running executable even though it will not let anything overwrite it: the
/// process keeps running from the image it already mapped. So the swap is two
/// moves - the live exe out of the way, the new one into its place - and the
/// only thing left to do is start the launcher again. Nothing is replaced under
/// a running process, and nothing needs the launcher to quit first, which is
/// what the wrapper's "update on the way out" path had to do.
///
/// Every failure here is quiet by design. An update that could not download is
/// an update that has not happened yet: the installed build is still the one
/// that was working a minute ago, and the update screen still offers the manual
/// command.
/// </summary>
public static class UpdateInstall
{
    /// <summary>A self-contained build is tens of megabytes on a hotel connection.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>The exe left behind by a swap, reaped on the next start.</summary>
    private const string OldSuffix = ".old";

    /// <summary>Where the new exe is assembled before the swap.</summary>
    private const string NewSuffix = ".new";

    private static readonly object Gate = new();

    public static InstallStage Stage { get; private set; } = InstallStage.Idle;

    /// <summary>The version being installed, and then the one now on disk.</summary>
    public static string Staged { get; private set; } = string.Empty;

    /// <summary>Why it did not work, for the update screen to show.</summary>
    public static string? Error { get; private set; }

    /// <summary>
    /// True when this process is the installed launcher, and so is allowed to
    /// replace itself.
    ///
    /// The test is the directory rather than the file name because a debug build
    /// is called ClaudeLauncher.exe too, and someone running one out of src/bin
    /// must never have it swapped out from under them.
    /// </summary>
    public static bool Supported
    {
        get
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(dir)) return false;

            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(StateStore.DataDir)),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Starts the install for a release, once.
    ///
    /// Called from the update check the moment a newer release is known, so the
    /// first anyone hears of an update is usually that it is already installed.
    /// </summary>
    public static void Start(UpdateInfo info, Action? changed = null)
    {
        if (!Supported) return;
        if (info.ZipUrl.Length == 0) return;

        lock (Gate)
        {
            if (Stage is InstallStage.Downloading or InstallStage.Installing or InstallStage.Installed) return;

            // A second attempt in the same run would fail the same way, and would
            // keep a blocked proxy busy for the rest of the afternoon.
            if (Stage == InstallStage.Failed && Staged == info.Latest) return;

            Stage = InstallStage.Downloading;
            Staged = info.Latest;
            Error = null;
        }

        Task.Run(async () =>
        {
            try
            {
                await Install(info);

                Stage = InstallStage.Installed;
                Error = null;
            }
            catch (Exception ex)
            {
                Stage = InstallStage.Failed;
                Error = ex.Message;
            }
            finally
            {
                changed?.Invoke();
            }
        });
    }

    /// <summary>
    /// Downloads the release zip, checks it against the published hash, and puts
    /// the exe and the wrapper into place.
    /// </summary>
    private static async Task Install(UpdateInfo info)
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("cannot tell which exe is running");

        var work = Directory.CreateTempSubdirectory("claude-launcher-update");

        try
        {
            var zip = Path.Combine(work.FullName, "release.zip");

            using (var client = new HttpClient { Timeout = Timeout })
            {
                client.DefaultRequestHeaders.Add("User-Agent", "claude-launcher");

                await Download(client, info.ZipUrl, zip);

                // No published hash means no install: a build that arrived over a
                // connection we cannot vouch for is not one to swap an exe for.
                if (info.ShaUrl.Length == 0)
                    throw new InvalidOperationException("the release has no checksum to verify against");

                var published = Published(await client.GetStringAsync(info.ShaUrl));
                var actual = await Hash(zip);

                if (!string.Equals(published, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("the download did not match the published checksum");
            }

            Stage = InstallStage.Installing;

            var unpacked = Path.Combine(work.FullName, "unpacked");
            ZipFile.ExtractToDirectory(zip, unpacked);

            var fresh = Path.Combine(unpacked, "ClaudeLauncher.exe");
            if (!File.Exists(fresh))
                throw new InvalidOperationException("the release zip has no ClaudeLauncher.exe in it");

            ReplaceRunningExe(exe, fresh);
            RefreshWrapper(Path.Combine(unpacked, "claude-launcher.ps1"));
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* a temp dir Windows clears itself */ }
        }
    }

    private static async Task Download(HttpClient client, string url, string target)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var file = File.Create(target);
        await response.Content.CopyToAsync(file);
    }

    /// <summary>Reads the hash out of a sha256sum line - the hash, two spaces, the file name.</summary>
    private static string Published(string text)
    {
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static async Task<string> Hash(string path)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file));
    }

    /// <summary>
    /// Swaps a staged exe in for one that may be running this very process.
    ///
    /// The staged copy is written next to the target first so both moves stay on
    /// one volume, where a move is a rename and cannot half-happen. If the
    /// second move fails the first is undone, because a directory with no
    /// ClaudeLauncher.exe in it is the one outcome worse than not updating.
    /// </summary>
    public static void ReplaceRunningExe(string exePath, string stagedExe)
    {
        var pending = exePath + NewSuffix;
        var previous = exePath + OldSuffix;

        File.Copy(stagedExe, pending, overwrite: true);
        File.Delete(previous);

        File.Move(exePath, previous);

        try
        {
            File.Move(pending, exePath);
        }
        catch (Exception)
        {
            File.Move(previous, exePath);
            throw;
        }
    }

    /// <summary>
    /// Copies the new wrapper over an installed one, wherever the installer put
    /// it. Only over a file that is already there: writing one somewhere no
    /// shell profile dot-sources would register nothing and confuse everything.
    /// </summary>
    private static void RefreshWrapper(string wrapper)
    {
        if (!File.Exists(wrapper)) return;

        foreach (var target in WrapperPaths())
        {
            try
            {
                if (File.Exists(target)) File.Copy(wrapper, target, overwrite: true);
            }
            catch (Exception)
            {
                // Locked, redirected, read-only: the exe is what matters, and the
                // wrapper is written to tolerate being a version behind.
            }
        }
    }

    /// <summary>
    /// Every place a wrapper may have been installed: both PowerShell hosts, and
    /// under OneDrive for a machine whose Documents folder was redirected.
    /// </summary>
    private static IEnumerable<string> WrapperPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var documents in new[] { "Documents", Path.Combine("OneDrive", "Documents") })
        foreach (var host in new[] { "WindowsPowerShell", "PowerShell" })
        {
            yield return Path.Combine(home, documents, host, "functions", "claude-launcher.ps1");
        }
    }

    /// <summary>
    /// Clears the build the last swap replaced. Safe at every start: the exe
    /// being deleted is the one this process is *not* running from.
    ///
    /// Only the .old file. The staged .new belongs to whichever launcher window
    /// is downloading right now, and deleting it under that window would fail
    /// its swap; a stale one is overwritten by the next attempt anyway.
    ///
    /// A window still running the previous build keeps that file open, and
    /// Windows will not delete a mapped image - so this quietly does nothing
    /// until the run that can.
    /// </summary>
    public static void Reap(string? exePath = null)
    {
        var exe = exePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        try { File.Delete(exe + OldSuffix); } catch { /* still open; a later start then */ }
    }
}
