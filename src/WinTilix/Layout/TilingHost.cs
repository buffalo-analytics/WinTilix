using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinTilix.Controls;

namespace WinTilix.Layout;

/// <summary>
/// Keeps every terminal pane as a permanent child of one canvas. Splits only
/// change layout math, so WebView2/ConPTY are never torn down by reparenting.
/// </summary>
public sealed class TilingHost : UserControl
{
    private const double SplitterSize = 6;
    private readonly Canvas _surface = new() { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)) };
    private TileNode? _root;
    private readonly Dictionary<SplitNode, Border> _splitters = [];
    private Border? _drag;
    private Point _dragStart;
    private double _dragRatio;

    public IEnumerable<TerminalPane> Panes => _surface.Children.OfType<TerminalPane>();

    public TilingHost()
    {
        Content = _surface;
        _surface.SizeChanged += (_, _) => Relayout();
    }

    public void SetRoot(TerminalPane pane)
    {
        Clear(disposePanes: false);
        _surface.Children.Add(pane);
        _root = new LeafNode(pane);
        Relayout();
    }

    public void ReplaceAll(IReadOnlyList<TerminalPane> panes)
    {
        Clear(disposePanes: true);
        if (panes.Count == 0)
        {
            return;
        }

        foreach (var pane in panes)
        {
            _surface.Children.Add(pane);
        }

        _root = BuildBalanced(panes, 0);
        Relayout();
    }

    public void Split(TerminalPane source, Orientation orientation, TerminalPane newbie)
    {
        if (_root is null)
        {
            SetRoot(newbie);
            return;
        }

        _surface.Children.Add(newbie);
        _root = InsertSplit(_root, source, orientation, newbie);
        Relayout();
    }

    public bool Close(TerminalPane pane)
    {
        var count = Panes.Count();
        if (count <= 1 || _root is null)
        {
            return false;
        }

        _root = RemoveLeaf(_root, pane);
        _surface.Children.Remove(pane);
        pane.DisposeSession();
        Relayout();
        return true;
    }

    public void DisposeAll() => Clear(disposePanes: true);

    private void Clear(bool disposePanes)
    {
        foreach (var splitter in _splitters.Values)
        {
            _surface.Children.Remove(splitter);
        }

        _splitters.Clear();

        if (disposePanes)
        {
            foreach (var pane in Panes.ToList())
            {
                _surface.Children.Remove(pane);
                pane.DisposeSession();
            }
        }
        else
        {
            _surface.Children.Clear();
        }

        _root = null;
    }

    private static TileNode BuildBalanced(IReadOnlyList<TerminalPane> panes, int depth)
    {
        if (panes.Count == 1)
        {
            return new LeafNode(panes[0]);
        }

        var mid = panes.Count / 2;
        var orientation = depth % 2 == 0 ? Orientation.Horizontal : Orientation.Vertical;
        return new SplitNode(
            orientation,
            BuildBalanced(panes.Take(mid).ToList(), depth + 1),
            BuildBalanced(panes.Skip(mid).ToList(), depth + 1));
    }

    private static TileNode InsertSplit(TileNode node, TerminalPane source, Orientation orientation, TerminalPane newbie)
    {
        if (node is LeafNode leaf && leaf.Pane == source)
        {
            return new SplitNode(orientation, leaf, new LeafNode(newbie));
        }

        if (node is SplitNode split)
        {
            split.First = InsertSplit(split.First, source, orientation, newbie);
            split.Second = InsertSplit(split.Second, source, orientation, newbie);
        }

        return node;
    }

    private static TileNode? RemoveLeaf(TileNode node, TerminalPane pane)
    {
        if (node is LeafNode leaf)
        {
            return leaf.Pane == pane ? null : node;
        }

        if (node is SplitNode split)
        {
            var first = RemoveLeaf(split.First, pane);
            var second = RemoveLeaf(split.Second, pane);
            if (first is null)
            {
                return second;
            }

            if (second is null)
            {
                return first;
            }

            split.First = first;
            split.Second = second;
            return split;
        }

        return node;
    }

    private void Relayout()
    {
        if (_root is null || _surface.ActualWidth <= 0 || _surface.ActualHeight <= 0)
        {
            return;
        }

        var live = new HashSet<SplitNode>();
        LayoutNode(_root, new Rect(0, 0, _surface.ActualWidth, _surface.ActualHeight), live);
        foreach (var dead in _splitters.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _surface.Children.Remove(_splitters[dead]);
            _splitters.Remove(dead);
        }

        // Ensure remaining panes refit xterm/ConPTY after tile geometry changes.
        foreach (var pane in Panes)
        {
            pane.RequestTerminalFit();
        }
    }

    private void LayoutNode(TileNode node, Rect rect, HashSet<SplitNode> live)
    {
        switch (node)
        {
            case LeafNode leaf:
                Canvas.SetLeft(leaf.Pane, rect.X);
                Canvas.SetTop(leaf.Pane, rect.Y);
                leaf.Pane.Width = Math.Max(0, rect.Width);
                leaf.Pane.Height = Math.Max(0, rect.Height);
                break;
            case SplitNode split:
                live.Add(split);
                if (split.Orientation == Orientation.Horizontal)
                {
                    var usable = Math.Max(0, rect.Width - SplitterSize);
                    var firstWidth = usable * split.Ratio;
                    LayoutNode(split.First, new Rect(rect.X, rect.Y, firstWidth, rect.Height), live);
                    PlaceSplitter(split, new Rect(rect.X + firstWidth, rect.Y, SplitterSize, rect.Height));
                    LayoutNode(split.Second, new Rect(rect.X + firstWidth + SplitterSize, rect.Y, usable - firstWidth, rect.Height), live);
                }
                else
                {
                    var usable = Math.Max(0, rect.Height - SplitterSize);
                    var firstHeight = usable * split.Ratio;
                    LayoutNode(split.First, new Rect(rect.X, rect.Y, rect.Width, firstHeight), live);
                    PlaceSplitter(split, new Rect(rect.X, rect.Y + firstHeight, rect.Width, SplitterSize));
                    LayoutNode(split.Second, new Rect(rect.X, rect.Y + firstHeight + SplitterSize, rect.Width, usable - firstHeight), live);
                }

                break;
        }
    }

    private void PlaceSplitter(SplitNode split, Rect rect)
    {
        if (!_splitters.TryGetValue(split, out var bar))
        {
            bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
                Cursor = split.Orientation == Orientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                Tag = split
            };
            bar.MouseLeftButtonDown += SplitterDown;
            bar.MouseLeftButtonUp += SplitterUp;
            bar.MouseMove += SplitterMove;
            _splitters[split] = bar;
            _surface.Children.Add(bar);
        }

        bar.Width = rect.Width;
        bar.Height = rect.Height;
        Canvas.SetLeft(bar, rect.X);
        Canvas.SetTop(bar, rect.Y);
    }

    private void SplitterDown(object sender, MouseButtonEventArgs e)
    {
        _drag = (Border)sender;
        _dragStart = e.GetPosition(_surface);
        _dragRatio = ((SplitNode)_drag.Tag!).Ratio;
        _drag.CaptureMouse();
        e.Handled = true;
    }

    private void SplitterUp(object sender, MouseButtonEventArgs e)
    {
        _drag?.ReleaseMouseCapture();
        _drag = null;
    }

    private void SplitterMove(object sender, MouseEventArgs e)
    {
        if (_drag is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(_surface);
        var node = (SplitNode)_drag.Tag!;
        var span = node.Orientation == Orientation.Horizontal ? _surface.ActualWidth : _surface.ActualHeight;
        if (span <= SplitterSize)
        {
            return;
        }

        var delta = node.Orientation == Orientation.Horizontal
            ? pos.X - _dragStart.X
            : pos.Y - _dragStart.Y;
        node.Ratio = Math.Clamp(_dragRatio + delta / (span - SplitterSize), 0.15, 0.85);
        Relayout();
    }

    private abstract class TileNode;

    private sealed class LeafNode(TerminalPane pane) : TileNode
    {
        public TerminalPane Pane { get; } = pane;
    }

    private sealed class SplitNode(Orientation orientation, TileNode first, TileNode second) : TileNode
    {
        public Orientation Orientation { get; } = orientation;
        public TileNode First { get; set; } = first;
        public TileNode Second { get; set; } = second;
        public double Ratio { get; set; } = 0.5;
    }
}
