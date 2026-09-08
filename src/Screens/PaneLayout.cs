using System.Globalization;
using System.Text;

namespace ClaudeLauncher.Screens;

/// <summary>
/// One node of a tile's interior: a leaf holding one pane, or a branch holding
/// children side by side or stacked.
///
/// The wall arranged panes by flowing them into a grid, which meant a split
/// could only ever mean "another box somewhere". Two sessions you are reading
/// against each other want to be one tile - a shared header, a divider between
/// them - and that is a tree, not a flow: each half can be split again, the
/// other way, without disturbing anything around it.
///
/// Weights are kept per branch and normalised to one, so a hand-edited file
/// cannot produce a pane of no width. <see cref="PaneSplits"/> still owns the
/// outer grid; this owns everything inside a tile.
/// </summary>
public sealed class PaneNode
{
    /// <summary>How far from equal one boundary may be pushed.</summary>
    private const double Least = 0.12;

    private readonly List<double> _weights = new();

    private PaneNode()
    {
    }

    public string? Key { get; private set; }

    /// <summary>Children run left to right; stacked top to bottom when false.</summary>
    public bool Vertical { get; private set; }

    public List<PaneNode> Children { get; } = new();

    public bool IsLeaf => Children.Count == 0;

    public IReadOnlyList<double> Weights => _weights;

    public static PaneNode Leaf(string key) => new() { Key = key };

    public IEnumerable<string> Leaves()
    {
        if (IsLeaf)
        {
            if (Key is not null) yield return Key;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var key in child.Leaves()) yield return key;
        }
    }

    public int Panes => IsLeaf ? 1 : Children.Sum(child => child.Panes);

    public bool Holds(string key) => Leaves().Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// Puts <paramref name="next"/> beside <paramref name="key"/>.
    ///
    /// A leaf already sitting in a branch that runs the right way joins that
    /// branch rather than nesting a new one inside it - three terminals in a row
    /// are three children of one branch, not a branch inside a branch, which is
    /// what keeps the dividers in a line and the weights meaningful.
    /// </summary>
    public bool Split(string key, string next, bool vertical)
    {
        if (IsLeaf)
        {
            if (!Same(Key, key)) return false;

            var mine = Leaf(Key!);
            Key = null;
            Vertical = vertical;
            Children.Add(mine);
            Children.Add(Leaf(next));
            _weights.Clear();
            _weights.AddRange(new[] { 0.5, 0.5 });
            return true;
        }

        for (var i = 0; i < Children.Count; i++)
        {
            if (Vertical == vertical && Children[i].IsLeaf && Same(Children[i].Key, key))
            {
                Insert(i + 1, Leaf(next));
                return true;
            }

            if (Children[i].Split(key, next, vertical)) return true;
        }

        return false;
    }

    /// <summary>Takes a pane out, collapsing a branch left holding one child.</summary>
    public bool Remove(string key)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            if (Children[i].IsLeaf && Same(Children[i].Key, key))
            {
                Children.RemoveAt(i);
                _weights.RemoveAt(i);
                Normalise();
                Collapse();
                return true;
            }

            if (!Children[i].Remove(key)) continue;

            Collapse();
            return true;
        }

        return false;
    }

    /// <summary>A pane keeps its place when its key changes - a chat given an id.</summary>
    public bool Rename(string from, string to)
    {
        if (IsLeaf)
        {
            if (!Same(Key, from)) return false;

            Key = to;
            return true;
        }

        return Children.Any(child => child.Rename(from, to));
    }

    /// <summary>
    /// The nearest branch running the given way that holds this pane, and which
    /// of its children the pane is in - what a resize key needs to know.
    /// </summary>
    public (PaneNode Branch, int Index)? Enclosing(string key, bool vertical)
    {
        if (IsLeaf) return null;

        for (var i = 0; i < Children.Count; i++)
        {
            if (!Children[i].Holds(key)) continue;

            // The deepest one wins: splitting a half and then resizing should
            // move the divider you just made, not the one around it.
            var deeper = Children[i].Enclosing(key, vertical);
            if (deeper is not null) return deeper;

            return Vertical == vertical ? (this, i) : null;
        }

        return null;
    }

    /// <summary>Moves the boundary to the right of <paramref name="index"/>.</summary>
    public bool Nudge(int index, double by)
    {
        if (Children.Count < 2 || index < 0 || index >= Children.Count - 1) return false;

        var left = _weights[index] + by;
        var right = _weights[index + 1] - by;

        if (left < Least || right < Least) return false;

        _weights[index] = left;
        _weights[index + 1] = right;
        return true;
    }

    /// <summary>Sets one boundary outright, for a divider dragged with the mouse.</summary>
    public bool Place(int index, double fraction)
    {
        if (Children.Count < 2 || index < 0 || index >= Children.Count - 1) return false;

        var before = _weights.Take(index).Sum();
        return Nudge(index, fraction - before - _weights[index]);
    }

    public void Even()
    {
        if (IsLeaf) return;

        _weights.Clear();
        for (var i = 0; i < Children.Count; i++) _weights.Add(1.0 / Children.Count);

        foreach (var child in Children) child.Even();
    }

    /// <summary>
    /// Turns weights into whole cells, with one column or row of divider between
    /// each pair taken off the top. Returns an empty array when there is not
    /// enough room for every child to be readable.
    /// </summary>
    public int[] Cells(int total, int least)
    {
        var count = Children.Count;
        if (count == 0) return Array.Empty<int>();

        var room = total - (count - 1);
        if (room < least * count) return Array.Empty<int>();

        var sizes = new int[count];
        var used = 0;

        for (var i = 0; i < count; i++)
        {
            sizes[i] = Math.Max(least, (int)Math.Round(_weights[i] * room));
            used += sizes[i];
        }

        while (used > room)
        {
            var widest = Array.IndexOf(sizes, sizes.Max());
            if (sizes[widest] <= least) break;
            sizes[widest]--;
            used--;
        }

        while (used < room)
        {
            sizes[Array.IndexOf(sizes, sizes.Min())]++;
            used++;
        }

        return sizes;
    }

    /// <summary>True while every child would still be readable with one more.</summary>
    public bool RoomFor(int total, int least) => (total - Children.Count) >= least * (Children.Count + 1);

    /// <summary>
    /// Takes a pane in at one end, as a child of this branch - what a pane
    /// arriving from the tile next door lands as.
    /// </summary>
    public void Join(string key, bool atStart, bool vertical)
    {
        if (IsLeaf)
        {
            var mine = Leaf(Key!);
            Key = null;
            Vertical = vertical;
            Children.Add(atStart ? Leaf(key) : mine);
            Children.Add(atStart ? mine : Leaf(key));
            _weights.Clear();
            _weights.AddRange(new[] { 0.5, 0.5 });
            return;
        }

        if (atStart)
        {
            // Insert() takes the room from the child before it, and at the front
            // there is none - so the first child gives up half of its own.
            var taken = _weights[0] / 2;
            _weights[0] = taken;
            Children.Insert(0, Leaf(key));
            _weights.Insert(0, taken);
            Normalise();
            return;
        }

        Insert(Children.Count, Leaf(key));
    }

    private void Insert(int index, PaneNode child)
    {
        // The room comes from the neighbour being split, not from everyone: the
        // panes you were not touching keep the width you gave them.
        var taken = _weights[index - 1] / 2;
        _weights[index - 1] = taken;

        Children.Insert(index, child);
        _weights.Insert(index, taken);
        Normalise();
    }

    /// <summary>A branch down to one child is that child.</summary>
    private void Collapse()
    {
        if (Children.Count != 1) return;

        var only = Children[0];
        Key = only.Key;
        Vertical = only.Vertical;

        var children = only.Children.ToList();
        var weights = only.Weights.ToList();

        Children.Clear();
        Children.AddRange(children);
        _weights.Clear();
        _weights.AddRange(weights);
    }

    private void Normalise()
    {
        var sum = _weights.Sum();
        if (sum <= 0)
        {
            Even();
            return;
        }

        for (var i = 0; i < _weights.Count; i++) _weights[i] /= sum;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Written as `V(0.6*key,0.4*H(0.5*key,0.5*key))`. Keys are percent-escaped,
    /// because a project path is a key and can hold anything.
    /// </summary>
    public void Write(StringBuilder into)
    {
        if (IsLeaf)
        {
            into.Append(Escape(Key ?? string.Empty));
            return;
        }

        into.Append(Vertical ? 'V' : 'H').Append('(');

        for (var i = 0; i < Children.Count; i++)
        {
            if (i > 0) into.Append(',');
            into.Append(_weights[i].ToString("0.###", CultureInfo.InvariantCulture)).Append('*');
            Children[i].Write(into);
        }

        into.Append(')');
    }

    public static PaneNode? Read(string text, ref int at)
    {
        if (at >= text.Length) return null;

        var kind = text[at];
        if ((kind == 'V' || kind == 'H') && at + 1 < text.Length && text[at + 1] == '(')
        {
            var branch = new PaneNode { Vertical = kind == 'V' };
            at += 2;

            while (true)
            {
                var star = text.IndexOf('*', at);
                if (star < 0) return null;

                if (!double.TryParse(text[at..star], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var weight) || weight <= 0)
                {
                    return null;
                }

                at = star + 1;

                var child = Read(text, ref at);
                if (child is null) return null;

                branch.Children.Add(child);
                branch._weights.Add(weight);

                if (at >= text.Length) return null;
                if (text[at] == ',') { at++; continue; }
                if (text[at] != ')') return null;

                at++;
                break;
            }

            if (branch.Children.Count < 2) return null;

            branch.Normalise();
            return branch;
        }

        var end = at;
        while (end < text.Length && text[end] is not (',' or ')' or '|')) end++;

        var key = Unescape(text[at..end]);
        at = end;
        return key.Length == 0 ? null : Leaf(key);
    }

    private static string Escape(string key)
    {
        var text = new StringBuilder(key.Length);

        foreach (var ch in key)
        {
            if (ch is '%' or '(' or ')' or ',' or '*' or '|' || char.IsControl(ch))
                text.Append('%').Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
            else
                text.Append(ch);
        }

        return text.ToString();
    }

    private static string Unescape(string text)
    {
        var key = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 2 < text.Length &&
                int.TryParse(text.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out var code))
            {
                key.Append((char)code);
                i += 2;
                continue;
            }

            key.Append(text[i]);
        }

        return key.ToString();
    }
}

/// <summary>
/// How the wall is arranged: one tree per tile, in the order the tiles are
/// drawn.
///
/// Grouping only. Which panes exist and which slot each one remembers is still
/// the wall's flat order - this says which of them share a tile, and in what
/// shape, so a pane that goes away and comes back is not also a layout change.
/// </summary>
public sealed class PaneLayout
{
    public List<PaneNode> Roots { get; } = new();

    /// <summary>
    /// Brings the tree up to date with what is actually on the wall: panes that
    /// have gone are pruned, panes it has never seen become tiles of their own,
    /// and the tiles are put back in the wall's own order.
    /// </summary>
    public void Sync(IReadOnlyList<string> keys)
    {
        var live = new HashSet<string>(keys, StringComparer.Ordinal);

        foreach (var root in Roots.ToList())
        {
            foreach (var key in root.Leaves().ToList())
            {
                if (live.Contains(key)) continue;

                if (root.IsLeaf) Roots.Remove(root);
                else root.Remove(key);
            }
        }

        var held = new HashSet<string>(Roots.SelectMany(root => root.Leaves()), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (held.Add(key)) Roots.Add(PaneNode.Leaf(key));
        }

        // A tile sits where its earliest pane sits, so moving a pane along the
        // wall moves the tile it belongs to and nothing else.
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++) order.TryAdd(keys[i], i);

        Roots.Sort((a, b) => First(a, order).CompareTo(First(b, order)));
    }

    private static int First(PaneNode root, Dictionary<string, int> order) =>
        root.Leaves().Select(key => order.TryGetValue(key, out var at) ? at : int.MaxValue).DefaultIfEmpty(int.MaxValue).Min();

    /// <summary>
    /// Moves the tiles holding a pinned pane to the front, keeping the order
    /// they were in among themselves.
    ///
    /// Applied after <see cref="Sync"/> rather than inside it, because it
    /// answers a different question: Sync says where a tile sits in the wall's
    /// own order, and this says that being pinned outranks that.
    /// </summary>
    public void First(Func<string, bool> pinned)
    {
        var wanted = Roots.Where(root => root.Leaves().Any(pinned)).ToList();
        if (wanted.Count == 0 || wanted.Count == Roots.Count) return;

        var rest = Roots.Where(root => !root.Leaves().Any(pinned)).ToList();

        Roots.Clear();
        Roots.AddRange(wanted);
        Roots.AddRange(rest);
    }

    /// <summary>Every pane, tile by tile - the order the wall draws them in.</summary>
    public List<string> Order() => Roots.SelectMany(root => root.Leaves()).ToList();

    public PaneNode? RootOf(string key) => Roots.FirstOrDefault(root => root.Holds(key));

    /// <summary>Which tile a pane is in, or -1.</summary>
    public int TileOf(string key) => Roots.FindIndex(root => root.Holds(key));

    public bool Split(string key, string next, bool vertical)
    {
        var root = RootOf(key);
        if (root is null) return false;

        // A pane can only be in one place. Splitting towards a pane that is
        // already on the wall moves it here rather than drawing it twice - and
        // the tile it leaves collapses behind it.
        if (!string.Equals(key, next, StringComparison.Ordinal)) Remove(next);

        root = RootOf(key);
        return root is not null && root.Split(key, next, vertical);
    }

    public void Remove(string key)
    {
        var root = RootOf(key);
        if (root is null) return;

        if (root.IsLeaf) Roots.Remove(root);
        else root.Remove(key);
    }

    public void Rename(string from, string to)
    {
        foreach (var root in Roots)
        {
            if (root.Rename(from, to)) return;
        }
    }

    public (PaneNode Branch, int Index)? Enclosing(string key, bool vertical) =>
        RootOf(key)?.Enclosing(key, vertical);

    /// <summary>
    /// Moves a pane out of its tile and into the one <paramref name="step"/>
    /// along, at the end nearest where it came from.
    ///
    /// The tile it leaves collapses behind it, so a pair that has been taken
    /// apart is an ordinary tile again rather than a branch of one child.
    /// </summary>
    public bool MoveTo(string key, int step)
    {
        var from = TileOf(key);
        if (from < 0) return false;

        var to = from + step;
        if (to < 0 || to >= Roots.Count || to == from) return false;

        var target = Roots[to];
        Remove(key);

        // Removing a lone pane takes its tile with it, which shifts everything
        // after it - so the target is found again by identity, not by index.
        target.Join(key, atStart: step > 0, vertical: true);
        return true;
    }

    /// <summary>Takes a pane out of its tile into one of its own, just after it.</summary>
    public bool MoveOut(string key)
    {
        var from = TileOf(key);
        if (from < 0 || Roots[from].IsLeaf) return false;

        // The tile it leaves keeps its slot - it still holds panes - so the new
        // one goes in immediately after it.
        Remove(key);
        Roots.Insert(Math.Min(from + 1, Roots.Count), PaneNode.Leaf(key));
        return true;
    }

    public string Format()
    {
        // Tiles holding one pane say nothing a flat order does not already say,
        // so a wall nobody has split writes an empty setting.
        if (Roots.All(root => root.IsLeaf)) return string.Empty;

        var text = new StringBuilder();

        foreach (var root in Roots)
        {
            if (text.Length > 0) text.Append('|');
            root.Write(text);
        }

        return text.ToString();
    }

    public static PaneLayout Parse(string? text)
    {
        var layout = new PaneLayout();
        if (string.IsNullOrWhiteSpace(text)) return layout;

        foreach (var part in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = 0;
            var root = PaneNode.Read(part, ref at);

            // Anything malformed is dropped rather than guessed at: Sync puts
            // its panes back as tiles of their own on the next frame.
            if (root is not null && at == part.Length) layout.Roots.Add(root);
        }

        return layout;
    }
}
