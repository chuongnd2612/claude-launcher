using System.Globalization;

namespace ClaudeLauncher.Screens;

/// <summary>
/// How much of the wall each column and row gets.
///
/// The grid was fixed at equal shares, which is the wrong split for almost every
/// real pair of panes: one holds a conversation you are reading and the other a
/// build log you are glancing at. Weights are kept per shape - the two column
/// case is a different arrangement from the three column one - and normalised on
/// the way in, so a stored file that has been edited by hand cannot produce a
/// pane of zero width.
/// </summary>
public sealed class PaneSplits
{
    private readonly Dictionary<int, double[]> _splits = new();

    /// <summary>How far from equal one boundary may be pushed.</summary>
    private const double Least = 0.12;

    public double[] For(int count)
    {
        if (count < 1) count = 1;
        if (_splits.TryGetValue(count, out var stored) && stored.Length == count) return stored;

        var even = new double[count];
        Array.Fill(even, 1.0 / count);
        _splits[count] = even;
        return even;
    }

    public bool IsEven(int count) => !_splits.ContainsKey(count) ||
                                     For(count).All(w => Math.Abs(w - 1.0 / count) < 0.001);

    public void Reset(int count) => _splits.Remove(count);

    /// <summary>
    /// Moves the boundary to the right of <paramref name="index"/>, taking from
    /// one side and giving to the other so the total stays one.
    /// </summary>
    public bool Nudge(int count, int index, double by)
    {
        if (count < 2 || index < 0 || index >= count - 1) return false;

        var weights = (double[])For(count).Clone();
        var left = weights[index] + by;
        var right = weights[index + 1] - by;

        if (left < Least || right < Least) return false;

        weights[index] = left;
        weights[index + 1] = right;
        _splits[count] = weights;
        return true;
    }

    /// <summary>Sets one boundary outright, for a divider dragged with the mouse.</summary>
    public bool Place(int count, int index, double fraction)
    {
        if (count < 2 || index < 0 || index >= count - 1) return false;

        var weights = For(count);
        var before = weights.Take(index).Sum();
        var pair = weights[index] + weights[index + 1];
        var left = fraction - before;

        if (left < Least || pair - left < Least) return false;

        return Nudge(count, index, left - weights[index]);
    }

    /// <summary>
    /// Turns weights into whole cells, giving the remainder to the widest pane
    /// so the far edge lands flush however the fractions divide.
    /// </summary>
    public int[] Cells(int count, int total, int least)
    {
        var sizes = new int[count];
        if (count < 1) return sizes;

        if (total < least * count)
        {
            // Too little room to honour anything: share it out evenly and let
            // the caller's own minimum decide what is drawable.
            for (var i = 0; i < count; i++) sizes[i] = total / count;
            return sizes;
        }

        var weights = For(count);
        var used = 0;

        for (var i = 0; i < count; i++)
        {
            sizes[i] = Math.Max(least, (int)Math.Round(weights[i] * total));
            used += sizes[i];
        }

        // Rounding and the floor can push the total either way; settle up on the
        // pane that can most afford it.
        while (used > total)
        {
            var widest = Array.IndexOf(sizes, sizes.Max());
            if (sizes[widest] <= least) break;
            sizes[widest]--;
            used--;
        }

        while (used < total)
        {
            var narrowest = Array.IndexOf(sizes, sizes.Min());
            sizes[narrowest]++;
            used++;
        }

        return sizes;
    }

    /// <summary>Reads back "2:0.62,0.38;3:0.5,0.25,0.25"; anything malformed is ignored.</summary>
    public static PaneSplits Parse(string? text)
    {
        var splits = new PaneSplits();
        if (string.IsNullOrWhiteSpace(text)) return splits;

        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');
            if (colon < 1) continue;

            if (!int.TryParse(part[..colon], out var count) || count is < 2 or > 16) continue;

            var numbers = part[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length != count) continue;

            var weights = new double[count];
            var sum = 0.0;
            var ok = true;

            for (var i = 0; i < count; i++)
            {
                if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out weights[i]) ||
                    weights[i] < Least / 2)
                {
                    ok = false;
                    break;
                }

                sum += weights[i];
            }

            if (!ok || sum <= 0) continue;

            for (var i = 0; i < count; i++) weights[i] /= sum;
            splits._splits[count] = weights;
        }

        return splits;
    }

    public override string ToString() => string.Join(';', _splits
        .Where(pair => !IsEven(pair.Key))
        .OrderBy(pair => pair.Key)
        .Select(pair => pair.Key + ":" + string.Join(',', pair.Value
            .Select(w => w.ToString("0.###", CultureInfo.InvariantCulture)))));
}
