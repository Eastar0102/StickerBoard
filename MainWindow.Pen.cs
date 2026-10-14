using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StickerBoard;

/// <summary>Pen (drawing) mode</summary>
public partial class MainWindow
{
    static readonly string[] PenColors =
        { "#FFFF4D4D", "#FFFFD23F", "#FF3FD27A", "#FF4C9AFF", "#FFFFFFFF", "#FF111111" };
    static readonly (double Size, double Dot)[] PenSizes = { (2, 4), (4, 7), (8, 11) };
    static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x4C, 0x9A, 0xFF));
    static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    enum PenTool { Pen, Highlighter, Eraser }

    PenTool _penTool = PenTool.Pen;
    bool _penMode;
    bool _inkDirty;
    bool _undoingInk;
    readonly Stack<(StrokeCollection Added, StrokeCollection Removed)> _inkHistory = new();
    readonly List<(Border Button, Func<bool> IsActive)> _penButtons = new();

    void InitPen()
    {
        Ink.Strokes = Storage.LoadInk();
        Ink.Strokes.StrokesChanged += OnStrokesChanged;
        BuildPenToolbar();
        ApplyPenAttributes();
    }

    void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (!_undoingInk) _inkHistory.Push((e.Added, e.Removed));
        _inkDirty = true;
        UpdateHint();
        ScheduleSave();
    }

    void SaveInkIfDirty()
    {
        if (!_inkDirty) return;
        Storage.SaveInk(Ink.Strokes);
        _inkDirty = false;
    }

    void SetPenMode(bool on)
    {
        _penMode = on;
        Ink.IsHitTestVisible = on;
        PenToolbar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            Select(null);
            PenButton.Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0xB0, 0xFF));
        }
        else
        {
            PenButton.ClearValue(ForegroundProperty);
        }
        Keyboard.Focus(Board);
        ApplyPenAttributes();
    }

    void SetPenTool(PenTool tool)
    {
        // Selecting the active tool again returns to the normal pen
        _penTool = _penTool == tool ? PenTool.Pen : tool;
        ApplyPenAttributes();
    }

    void ApplyPenAttributes()
    {
        Ink.EditingMode = _penTool == PenTool.Eraser ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
        bool highlighter = _penTool == PenTool.Highlighter;
        double size = highlighter ? _data.PenSize * 4 : _data.PenSize;
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = ItemView.ParseColor(_data.PenColor),
            Width = size,
            Height = size,
            FitToCurve = true,
            IsHighlighter = highlighter,
            IgnorePressure = highlighter,
            StylusTip = highlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
        };
        RefreshPenToolbar();
    }

    void UndoInk()
    {
        if (_inkHistory.Count == 0) return;
        var (added, removed) = _inkHistory.Pop();
        _undoingInk = true;
        try
        {
            foreach (var s in added.Where(Ink.Strokes.Contains).ToList())
                Ink.Strokes.Remove(s);
            foreach (var s in removed.Where(s => !Ink.Strokes.Contains(s)))
                Ink.Strokes.Add(s);
        }
        finally { _undoingInk = false; }
    }

    // Clearing can be undone with Ctrl+Z as well
    void ClearInk() => Ink.Strokes.Clear();

    // ---------- Toolbar ----------

    void BuildPenToolbar()
    {
        foreach (var hex in PenColors)
        {
            var dot = new Ellipse
            {
                Width = 14, Height = 14,
                Fill = new SolidColorBrush(ItemView.ParseColor(hex)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
            };
            AddPenButton(dot, L.T("pen.color"), () =>
            {
                _data.PenColor = hex;
                if (_penTool == PenTool.Eraser) _penTool = PenTool.Pen;
                ApplyPenAttributes();
                ScheduleSave();
            }, () => _penTool != PenTool.Eraser && _data.PenColor == hex);
        }
        AddPenSeparator();

        foreach (var (size, dotSize) in PenSizes)
        {
            var dot = new Ellipse { Width = dotSize, Height = dotSize, Fill = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)) };
            AddPenButton(dot, L.T("pen.size"), () =>
            {
                _data.PenSize = size;
                if (_penTool == PenTool.Eraser) _penTool = PenTool.Pen;
                ApplyPenAttributes();
                ScheduleSave();
            }, () => _penTool != PenTool.Eraser && Math.Abs(_data.PenSize - size) < 0.01);
        }
        AddPenSeparator();

        AddPenButton(Glyph(""), L.T("pen.highlighter"), () => SetPenTool(PenTool.Highlighter), () => _penTool == PenTool.Highlighter);
        AddPenButton(Glyph(""), L.T("pen.eraser"), () => SetPenTool(PenTool.Eraser), () => _penTool == PenTool.Eraser);
        AddPenButton(Glyph(""), L.T("pen.undo"), UndoInk);
        AddPenButton(Glyph(""), L.T("menu.clearInk"), ClearInk);
        AddPenSeparator();
        AddPenButton(Glyph(""), L.T("pen.exit"), () => SetPenMode(false));
    }

    void AddPenButton(FrameworkElement content, string tip, Action click, Func<bool>? isActive = null)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border
        {
            Width = 28, Height = 24, Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(12), Background = Brushes.Transparent,
            Child = content, ToolTip = tip, Cursor = Cursors.Hand,
        };
        b.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            click();
            RefreshPenToolbar();
        };
        b.MouseEnter += (_, _) => RefreshPenToolbar();
        b.MouseLeave += (_, _) => RefreshPenToolbar();
        _penButtons.Add((b, isActive ?? (() => false)));
        PenTools.Children.Add(b);
    }

    void AddPenSeparator() => PenTools.Children.Add(new Border
    {
        Width = 1, Height = 16, Margin = new Thickness(5, 0, 5, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
    });

    static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
    };

    void RefreshPenToolbar()
    {
        foreach (var (button, isActive) in _penButtons)
            button.Background = isActive() ? ActiveBrush : button.IsMouseOver ? HoverBrush : Brushes.Transparent;
    }

    void Pen_Click(object sender, RoutedEventArgs e) => SetPenMode(!_penMode);
}
