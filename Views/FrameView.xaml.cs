using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        Matrix.Children.Clear();
        Matrix.ColumnDefinitions.Clear();
        Matrix.RowDefinitions.Clear();
        _segmentBorders.Clear();
        var layout = _vm?.Layout;
        if (layout is null || !layout.HasFrame) return;

        // Columns: byte label + 8 bit columns.
        Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (int c = 0; c < 8; c++) Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });

        // Rows: header + one per byte.
        Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (int r = 0; r < layout.ByteCount; r++) Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        // Bit-label header (7 → 0).
        for (int c = 0; c < 8; c++)
            AddText($"{7 - c}", 0, c + 1, FontWeights.SemiBold, 0.6, HorizontalAlignment.Center);

        // Byte labels + empty grid cells.
        var gridStroke = (Brush)(TryFindResource("CardStrokeColorDefaultBrush") ?? Brushes.Gray);
        for (int by = 0; by < layout.ByteCount; by++)
        {
            AddText($"B{by}", by + 1, 0, FontWeights.Normal, 0.55, HorizontalAlignment.Center);
            for (int c = 0; c < 8; c++)
            {
                var cell = new Border { BorderBrush = gridStroke, BorderThickness = new Thickness(0.5), Background = Brushes.Transparent };
                Grid.SetRow(cell, by + 1); Grid.SetColumn(cell, c + 1);
                Matrix.Children.Add(cell);
            }
        }

        // Signal segments on top.
        foreach (var block in layout.Blocks)
        {
            bool first = true;
            foreach (var seg in block.Segments)
            {
                if (seg.Byte + 1 > layout.ByteCount) continue;
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
                Grid.SetRow(border, seg.Byte + 1);
                Grid.SetColumn(border, seg.ColStart + 1);
                Grid.SetColumnSpan(border, seg.Span);
                Matrix.Children.Add(border);
                _segmentBorders.Add((block, border));
                first = false;
            }
        }
    }

    private void Segment_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not SignalBlock block || _vm is null) return;
        _vm.SelectedBlock = block;
        var accent = (Brush)(TryFindResource("SystemAccentColorPrimaryBrush") ?? Brushes.White);
        foreach (var (blk, border) in _segmentBorders)
        {
            bool sel = ReferenceEquals(blk, block);
            border.BorderBrush = sel ? accent : Brushes.White;
            border.BorderThickness = new Thickness(sel ? 2 : 0.5);
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
