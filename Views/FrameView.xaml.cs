using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ExchangeFileValidator.Controls;
using ExchangeFileValidator.Services;
using ExchangeFileValidator.ViewModels;

namespace ExchangeFileValidator.Views;

public partial class FrameView : UserControl
{
    private FrameViewModel? _vm;
    private readonly List<(SignalBlock block, Border border)> _segmentBorders = new();

    public FrameView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.LayoutChanged -= Rebuild;
        _vm = DataContext as FrameViewModel;
        if (_vm is not null) { _vm.LayoutChanged += Rebuild; Rebuild(); }
    }

    private static readonly string[] AppPalette =
        { "#556D5DF5", "#551565C0", "#5500897B", "#55EF8C00", "#552E7D32", "#558E24AA", "#55C62828" };

    private static Brush BlockBrush(SignalBlock b) => b.Kind switch
    {
        "crc" => Hex("#5500897B"),
        "clock" => Hex("#558E24AA"),
        "filler" => Hex("#33808080"),
        _ => Hex(AppPalette[b.ColorIndex % AppPalette.Length]),
    };

    private void Rebuild()
    {
        try { RebuildCore(); }
        catch (Exception ex) { ShowMessage("Could not render this frame: " + ex.Message); }
    }

    private void ShowMessage(string text)
    {
        Matrix.Children.Clear();
        Matrix.ColumnDefinitions.Clear();
        Matrix.RowDefinitions.Clear();
        Matrix.ColumnDefinitions.Add(new ColumnDefinition());
        Matrix.RowDefinitions.Add(new RowDefinition());
        Matrix.Children.Add(new TextBlock
        {
            Text = text, Opacity = 0.6, Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left,
        });
    }

    private void RebuildCore()
    {
        Matrix.Children.Clear();
        Matrix.ColumnDefinitions.Clear();
        Matrix.RowDefinitions.Clear();
        _segmentBorders.Clear();
        var layout = _vm?.Layout;
        if (layout is null || !layout.HasFrame) { ShowMessage("No frame selected. Open a frame from the Explorer, or search a frame name above."); return; }
        if (layout.Blocks.Count == 0) { ShowMessage($"'{layout.FrameName}' has no byte/bit layout data (Byte/Bit Position or Signal Size missing on its rows)."); return; }

        int numBlocks = (layout.ByteCount + 7) / 8;
        if (numBlocks < 1) numBlocks = 1;

        // Define columns: for each block we need 1 label col (40) + 8 bit cols (46) + 1 separator (16)
        for (int b = 0; b < numBlocks; b++)
        {
            Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            for (int c = 0; c < 8; c++) Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        }

        // Define rows: 1 header row (22) + 8 byte rows (30)
        Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (int r = 0; r < 8; r++) Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        // Bit-label headers and empty grid cells for all blocks
        var gridStroke = (Brush)(TryFindResource("CardStrokeColorDefaultBrush") ?? Brushes.Gray);
        for (int b = 0; b < numBlocks; b++)
        {
            int colOffset = b * 10;

            // Bit headers (7 -> 0)
            for (int c = 0; c < 8; c++)
            {
                AddText($"{7 - c}", 0, colOffset + 1 + c, FontWeights.SemiBold, 0.6, HorizontalAlignment.Center);
            }

            // Byte rows inside this block
            for (int r = 0; r < 8; r++)
            {
                int byteIdx = b * 8 + r;
                if (byteIdx >= layout.ByteCount) continue;

                AddText($"B{byteIdx}", r + 1, colOffset, FontWeights.Normal, 0.55, HorizontalAlignment.Center);
                for (int c = 0; c < 8; c++)
                {
                    var cell = new Border { BorderBrush = gridStroke, BorderThickness = new Thickness(0.5), Background = Brushes.Transparent };
                    Grid.SetRow(cell, r + 1); Grid.SetColumn(cell, colOffset + 1 + c);
                    Matrix.Children.Add(cell);
                }
            }
        }

        // Signal segments on top
        foreach (var block in layout.Blocks)
        {
            bool first = true;
            foreach (var seg in block.Segments)
            {
                if (seg.Byte >= layout.ByteCount) continue;
                int b = seg.Byte / 8;
                int r = seg.Byte % 8;
                int colOffset = b * 10;

                var border = new Border
                {
                    Background = BlockBrush(block),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{block.Name} — {block.Size} bits @ byte {block.StartByte}/bit {block.StartBit}",
                    Tag = block,
                };
                if (first)
                    border.Child = new TextBlock
                    {
                        Text = block.Name,
                        FontSize = 10,
                        Margin = new Thickness(3, 0, 3, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = Brushes.White,
                    };
                border.MouseLeftButtonUp += Segment_Click;
                Grid.SetRow(border, r + 1);
                Grid.SetColumn(border, colOffset + 1 + seg.ColStart);
                Grid.SetColumnSpan(border, seg.Span);
                Matrix.Children.Add(border);
                _segmentBorders.Add((block, border));

                if (AppAnimations.Enabled)   // staggered fade-in (chrome only)
                {
                    border.Opacity = 0;
                    var fade = new DoubleAnimation(0, 1, AppAnimations.Fast)
                    {
                        BeginTime = TimeSpan.FromMilliseconds(Math.Min(600, _segmentBorders.Count * 6)),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    };
                    border.BeginAnimation(OpacityProperty, fade);
                }
                first = false;
            }
        }
    }

    private void Segment_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not SignalBlock block || _vm is null) return;
        _vm.SelectedBlock = block;
        var accent = (Brush)(TryFindResource("SystemAccentColorPrimaryBrush") ?? Brushes.White);
        var glowColor = (Color)ColorConverter.ConvertFromString("#6D5DF5")!;
        foreach (var (blk, border) in _segmentBorders)
        {
            bool sel = ReferenceEquals(blk, block);
            border.BorderBrush = sel ? accent : Brushes.White;
            border.BorderThickness = new Thickness(sel ? 2 : 0.5);
            border.Effect = sel && AppAnimations.Enabled
                ? new DropShadowEffect { Color = glowColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 }
                : null;
        }
    }

    private void AddText(string text, int row, int col, FontWeight weight, double opacity, HorizontalAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text, FontWeight = weight, Opacity = opacity, FontSize = 11,
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(tb, row); Grid.SetColumn(tb, col);
        Matrix.Children.Add(tb);
    }

    private static Brush Hex(string h)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)!);
        b.Freeze();
        return b;
    }
}
