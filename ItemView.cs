using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickerBoard;

/// <summary>One image or note on the board (note-specific code is in ItemView.Note.cs)</summary>
public sealed partial class ItemView : Grid
{
    const double MinSize = 24;

    readonly MainWindow _owner;
    readonly Border _outline;
    readonly FrameworkElement _handle;
    bool _selected;
    bool _dragging;
    Point _dragStart;
    Rect _startRect;

    public BoardItem Item { get; }
    public BitmapSource? Bitmap { get; }
    public bool IsImage => Item.Type == ItemTypes.Image;

    public ItemView(MainWindow owner, BoardItem item, BitmapSource? bitmap)
    {
        _owner = owner;
        Item = item;
        Bitmap = bitmap;

        if (IsImage) BuildImage();
        else BuildNote();

        _outline = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(-2),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_outline);

        _handle = BuildHandle();
        Children.Add(_handle);

        PreviewMouseLeftButtonDown += (_, _) => _owner.Select(this);
        MouseEnter += (_, _) => UpdateChrome();
        MouseLeave += (_, _) => UpdateChrome();
        ContextMenu = IsImage ? BuildImageMenu() : BuildNoteMenu();

        ApplyLayout();
        UpdateChrome();
    }

    public void ApplyLayout()
    {
        Canvas.SetLeft(this, Item.X);
        Canvas.SetTop(this, Item.Y);
        Width = Item.W;
        Height = Item.H;
        Panel.SetZIndex(this, Item.Z);
        if (!IsImage) UpdateNoteChrome();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        UpdateChrome();
    }

    void UpdateChrome()
    {
        _outline.Visibility = _selected ? Visibility.Visible : Visibility.Collapsed;
        _handle.Visibility = _selected || _dragging || IsMouseOver ? Visibility.Visible : Visibility.Hidden;
        if (!IsImage) UpdateNoteChrome();
    }

    // ---------- Construction ----------

    void BuildImage()
    {
        var img = new Image { Source = Bitmap, Stretch = Stretch.Fill, Cursor = Cursors.SizeAll };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        AttachDrag(img, resize: false);
        img.MouseWheel += OnImageWheel;
        Children.Add(img);
    }

    FrameworkElement BuildHandle()
    {
        var triangle = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 11,1 L 11,11 L 1,11 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2),
        };
        var handle = new Grid
        {
            Width = 18,
            Height = 18,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            ToolTip = L.T(IsImage ? "tip.resizeImage" : "tip.resize"),
        };
        handle.Children.Add(triangle);
        AttachDrag(handle, resize: true);
        return handle;
    }

    ContextMenu BuildImageMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.originalSize"), ResetSize));
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.copyImage"), () => _owner.CopyImage(this), "Ctrl+C"));
        m.Items.Add(new Separator());
        AddOrderItems(m);
        m.Items.Add(new Separator());
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.delete"), () => _owner.DeleteItem(this), "Del"));
        return m;
    }

    void AddOrderItems(ContextMenu m)
    {
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.bringToFront"), () => _owner.BringToFront(this)));
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.sendToBack"), () => _owner.SendToBack(this)));
    }

    // ---------- Behavior ----------

    void AttachDrag(FrameworkElement el, bool resize)
    {
        el.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _dragStart = e.GetPosition(_owner.Board);
            _startRect = new Rect(Item.X, Item.Y, Item.W, Item.H);
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseMove += (_, e) =>
        {
            if (!_dragging || !el.IsMouseCaptured) return;
            var p = e.GetPosition(_owner.Board);
            double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
            if (resize)
            {
                ResizeTo(_startRect.Width + dx, _startRect.Height + dy);
            }
            else
            {
                Item.X = _startRect.X + dx;
                Item.Y = _startRect.Y + dy;
            }
            ApplyLayout();
        };
        el.MouseLeftButtonUp += (_, _) =>
        {
            if (el.IsMouseCaptured) el.ReleaseMouseCapture();
        };
        el.LostMouseCapture += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            _owner.KeepVisible(Item);
            ApplyLayout();
            UpdateChrome();
            _owner.ScheduleSave();
        };
    }

    void ResizeTo(double w, double h)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        if (IsImage && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            // Keep the aspect ratio, growing to enclose the mouse position
            double aspect = _startRect.Width / _startRect.Height;
            if (w / aspect >= h) h = w / aspect;
            else w = h * aspect;
            double s = Math.Max(1, Math.Max(MinSize / w, MinSize / h));
            w *= s;
            h *= s;
        }
        Item.W = Math.Max(MinSize, w);
        Item.H = Math.Max(MinSize, h);
    }

    void OnImageWheel(object sender, MouseWheelEventArgs e)
    {
        double f = e.Delta > 0 ? 1.1 : 1 / 1.1;
        double w = Item.W * f, h = Item.H * f;
        e.Handled = true;
        if (Math.Min(w, h) < MinSize) return;

        // Zoom around the cursor position
        var p = e.GetPosition(_owner.Board);
        Item.X = p.X - (p.X - Item.X) * f;
        Item.Y = p.Y - (p.Y - Item.Y) * f;
        Item.W = w;
        Item.H = h;
        ApplyLayout();
        _owner.ScheduleSave();
    }

    void ResetSize()
    {
        if (Bitmap == null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Item.W = Bitmap.PixelWidth / dpi.DpiScaleX;
        Item.H = Bitmap.PixelHeight / dpi.DpiScaleY;
        ApplyLayout();
        _owner.ScheduleSave();
    }

    internal static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0xF7, 0xE0, 0x7A); }
    }
}
