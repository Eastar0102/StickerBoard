using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace StickerBoard;

public partial class MainWindow : Window
{
    static readonly (string Name, double Value)[] BgLevels =
        { ("bg.opaque", 0.95), ("bg.translucent", 0.6), ("bg.faint", 0.25), ("bg.clear", 0) };

    readonly BoardData _data;
    readonly List<ItemView> _views = new();
    readonly Stack<BoardItem> _trash = new();
    readonly DispatcherTimer _saveTimer;
    ItemView? _selected;
    Point _menuPoint;
    Point? _maxDragStart;
    bool _syncingSlider;
    bool _hover;

    public MainWindow(BoardData data)
    {
        InitializeComponent();
        _data = data;
        FontFamily = new FontFamily(L.T("ui.font"));
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => SaveNow();

        RestoreWindowBounds();
        foreach (var item in _data.Items.OrderBy(i => i.Z).ToList())
            AddView(item);
        InitPen();
        UpdateHint();
        ApplyBackground();
        Topmost = _data.Mode == Modes.Top;
        UpdatePinButton();
        ApplyLock();
        StateChanged += (_, _) => OnWindowStateChanged();
        if (_data.Maximized) WindowState = WindowState.Maximized;

        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        Loaded += (_, _) =>
        {
            Keyboard.Focus(Board);
            if (_data.Mode == Modes.Bottom) PushToBottom();
        };
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) =>
        {
            ScheduleSave();
            // Hide the opacity slider on narrow windows to leave room for the buttons
            OpacityPanel.Visibility = ActualWidth < 340 ? Visibility.Collapsed : Visibility.Visible;
        };
        _syncingSlider = true;
        OpacitySlider.Value = _data.BgOpacity;
        _syncingSlider = false;
        OpacityPanel.PreviewMouseWheel += (_, e) =>
        {
            OpacitySlider.Value = Math.Clamp(OpacitySlider.Value + (e.Delta > 0 ? 0.05 : -0.05), 0, OpacitySlider.Maximum);
            e.Handled = true;
        };
        Board.SizeChanged += (_, _) =>
        {
            PullItemsInside();
            LayoutHint();
        };
        Closing += (_, _) => SaveNow();
        Closed += (_, _) => Storage.CleanupImages(_data);
    }

    // ---------- Saving ----------

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveNow()
    {
        _saveTimer.Stop();
        if (WindowState == WindowState.Normal && IsLoaded)
        {
            _data.Left = Left;
            _data.Top = Top;
            _data.Width = ActualWidth;
            _data.Height = ActualHeight;
        }
        try
        {
            foreach (var view in _views) view.FlushText();
            Storage.Save(_data);
            SaveInkIfDirty();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void RestoreWindowBounds()
    {
        Width = Math.Max(MinWidth, _data.Width);
        Height = Math.Max(MinHeight, _data.Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_data.Left is double l && _data.Top is double t && screen.Contains(new Point(l + 40, t + 10)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    // ---------- Items ----------

    ItemView? AddView(BoardItem item, System.Windows.Media.Imaging.BitmapSource? bmp = null)
    {
        if (item.Type == ItemTypes.Image)
        {
            bmp ??= item.Image != null ? Storage.LoadImage(item.Image) : null;
            if (bmp == null)
            {
                _data.Items.Remove(item);
                return null;
            }
        }
        var view = new ItemView(this, item, bmp);
        _views.Add(view);
        Board.Children.Add(view);
        return view;
    }

    int NextZ() => _data.Items.Count == 0 ? 1 : _data.Items.Max(i => i.Z) + 1;

    Point SpawnPoint()
    {
        var m = Mouse.GetPosition(Board);
        if (m.X >= 0 && m.Y >= 0 && m.X <= Board.ActualWidth && m.Y <= Board.ActualHeight) return m;
        return new Point(Board.ActualWidth / 2, Board.ActualHeight / 2);
    }

    void PasteFrom(IDataObject? data, Point? at)
    {
        if (data == null) return;
        var images = ImageData.Extract(data);
        if (images.Count > 0)
        {
            var p = at ?? SpawnPoint();
            foreach (var img in images)
            {
                AddImage(img, p);
                p += new Vector(24, 24);
            }
            return;
        }

        string? text = null;
        try
        {
            if (data.GetDataPresent(DataFormats.UnicodeText))
                text = data.GetData(DataFormats.UnicodeText) as string;
        }
        catch { }
        if (Links.AsSingleUrl(text) is { } url)
            AddLink(at, url);
        else if (!string.IsNullOrWhiteSpace(text))
            AddNote(at, text.Trim());
    }

    void AddLink(Point? at, Uri url)
    {
        var item = new BoardItem { Type = ItemTypes.Link, Url = url.AbsoluteUri, W = 240, H = 58, Z = NextZ() };
        var p = at ?? SpawnPoint();
        item.X = p.X - (at == null ? item.W / 2 : 0);
        item.Y = p.Y - (at == null ? item.H / 2 : 0);
        FitInside(item);
        _data.Items.Add(item);
        if (AddView(item) is { } view) Select(view);
        UpdateHint();
        ScheduleSave();
    }

    void AddImage(ClipImage src, Point center)
    {
        string name;
        try { name = src.File != null ? Storage.ImportFile(src.File) : Storage.SaveImage(src.Bitmap); }
        catch (Exception ex) { App.Log(ex); return; }

        // Show at native pixel size on screen; shrink to fit the board if too large
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = src.Bitmap.PixelWidth / dpi.DpiScaleX;
        double h = src.Bitmap.PixelHeight / dpi.DpiScaleY;
        double maxW = Math.Max(80, Board.ActualWidth * 0.8), maxH = Math.Max(80, Board.ActualHeight * 0.8);
        double s = Math.Min(1, Math.Min(maxW / w, maxH / h));
        w *= s;
        h *= s;

        var item = new BoardItem
        {
            Type = ItemTypes.Image, Image = name,
            W = w, H = h, X = center.X - w / 2, Y = center.Y - h / 2, Z = NextZ(),
        };
        FitInside(item);
        _data.Items.Add(item);
        if (AddView(item, src.Bitmap) is { } view) Select(view);
        UpdateHint();
        ScheduleSave();
    }

    void AddNote(Point? at, string text)
    {
        var item = new BoardItem
        {
            Type = ItemTypes.Note, Text = text, W = 200, H = 140, Z = NextZ(),
            Color = ItemView.NoteColors[0].Hex,
        };
        if (at is Point p)
        {
            item.X = p.X;
            item.Y = p.Y;
        }
        else
        {
            var c = SpawnPoint();
            item.X = c.X - item.W / 2;
            item.Y = c.Y - item.H / 2;
        }
        FitInside(item);
        _data.Items.Add(item);
        var view = AddView(item);
        UpdateHint();
        ScheduleSave();
        if (view?.Editor is { } editor)
        {
            Select(view);
            Dispatcher.BeginInvoke(() =>
            {
                editor.Focus();
                editor.CaretPosition = editor.Document.ContentEnd;
            }, DispatcherPriority.Input);
        }
    }

    /// <summary>Place new items fully inside the board</summary>
    void FitInside(BoardItem item)
    {
        item.X = Clamp(item.X, 0, Board.ActualWidth - item.W);
        item.Y = Clamp(item.Y, 0, Board.ActualHeight - item.H);
        // Nudge items that would stack exactly on top of another
        for (int i = 0; i < 20 && _data.Items.Any(o => Math.Abs(o.X - item.X) < 4 && Math.Abs(o.Y - item.Y) < 4); i++)
        {
            item.X += 20;
            item.Y += 20;
        }
    }

    /// <summary>After a drag, keep part of the item visible so it cannot get lost</summary>
    /// <summary>Pull items back inside when the board shrinks (also applied once at startup)</summary>
    void PullItemsInside()
    {
        if (Board.ActualWidth < 1 || Board.ActualHeight < 1) return;
        bool moved = false;
        foreach (var view in _views)
        {
            var item = view.Item;
            double x = Clamp(item.X, 0, Board.ActualWidth - item.W);
            double y = Clamp(item.Y, 0, Board.ActualHeight - item.H);
            if (x == item.X && y == item.Y) continue;
            item.X = x;
            item.Y = y;
            view.ApplyLayout();
            moved = true;
        }
        if (moved) ScheduleSave();
    }

    public void KeepVisible(BoardItem item)
    {
        const double margin = 30;
        item.X = Clamp(item.X, margin - item.W, Board.ActualWidth - margin);
        item.Y = Clamp(item.Y, item.Type == ItemTypes.Note ? 0 : margin - item.H, Board.ActualHeight - margin);
    }

    static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(Math.Max(min, max), v));

    public void Select(ItemView? view)
    {
        if (_selected == view) return;
        _selected?.SetSelected(false);
        _selected = view;
        // Focus left in another item's text box would swallow the board shortcuts (P, E, H, Delete)
        if (Keyboard.FocusedElement is Visual focused && view?.IsAncestorOf(focused) != true)
            Keyboard.Focus(Board);
        if (view == null) return;
        view.SetSelected(true);
        BringToFront(view);
    }

    public void BringToFront(ItemView view)
    {
        if (!_data.Items.Any(i => i != view.Item && i.Z >= view.Item.Z)) return;
        view.Item.Z = NextZ();
        view.ApplyLayout();
        ScheduleSave();
    }

    public void SendToBack(ItemView view)
    {
        view.Item.Z = _data.Items.Min(i => i.Z) - 1;
        view.ApplyLayout();
        ScheduleSave();
    }

    public void DeleteItem(ItemView view)
    {
        view.FlushText(); // keep formatting for undo
        Board.Children.Remove(view);
        _views.Remove(view);
        _data.Items.Remove(view.Item);
        _trash.Push(view.Item);
        if (_selected == view) _selected = null;
        UpdateHint();
        ScheduleSave();
        Keyboard.Focus(Board);
    }

    void UndoDelete()
    {
        if (_trash.Count == 0) return;
        var item = _trash.Pop();
        item.Z = NextZ();
        _data.Items.Add(item);
        if (AddView(item) is { } view) Select(view);
        UpdateHint();
        ScheduleSave();
    }

    void ClearAll()
    {
        if (_views.Count == 0) return;
        if (MessageBox.Show(this, L.T("msg.clearAll"),
                "StickerBoard", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var view in _views.OrderBy(v => v.Item.Z).ToList())
            DeleteItem(view);
    }

    public void CopyImage(ItemView view)
    {
        if (view.Bitmap == null) return;
        try { ImageData.CopyToClipboard(view.Bitmap); }
        catch (Exception ex) { App.Log(ex); }
    }


    /// <summary>Hint text: one line when the board is wide enough, several lines otherwise</summary>
    void LayoutHint()
    {
        HintText.Text = L.T("hint.oneLine");
        HintText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double available = Board.ActualWidth - HintText.Margin.Right - 12;
        if (HintText.DesiredSize.Width > available) HintText.Text = L.T("hint.lines");
    }

    void UpdateHint() =>
        HintText.Visibility = _views.Count == 0 && Ink.Strokes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    static IDataObject? GetClipboard()
    {
        // The clipboard can be briefly locked by another app; retry a few times
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (COMException) { System.Threading.Thread.Sleep(30); }
        }
        return null;
    }

    static bool ClipboardHasText()
    {
        try { return Clipboard.ContainsText(); }
        catch { return false; }
    }

    // ---------- Input ----------

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Even while editing a note: if the clipboard has no text (image only), paste it onto the board
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
            && Keyboard.FocusedElement is TextBoxBase && !ClipboardHasText())
        {
            PasteFrom(GetClipboard(), null);
            e.Handled = true;
        }
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool inText = Keyboard.FocusedElement is TextBoxBase;

        if (e.Key == Key.F11)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (_penMode) SetPenMode(false);
            Select(null);
            Keyboard.Focus(Board);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.V)
        {
            // Text pasted while editing a note is handled by the note itself
            if (inText && ClipboardHasText()) return;
            PasteFrom(GetClipboard(), null);
            e.Handled = true;
            return;
        }
        if (inText) return;

        if (!ctrl && e.Key == Key.P)
        {
            SetPenMode(!_penMode);
            e.Handled = true;
            return;
        }
        if (_penMode)
        {
            if (ctrl && e.Key == Key.Z) UndoInk();
            else if (!ctrl && e.Key == Key.E) SetPenTool(PenTool.Eraser);
            else if (!ctrl && e.Key == Key.H) SetPenTool(PenTool.Highlighter);
            else return;
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Delete || e.Key == Key.Back) && _selected != null)
        {
            DeleteItem(_selected);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z)
        {
            UndoDelete();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C && _selected?.Bitmap != null)
        {
            CopyImage(_selected);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C && _selected?.IsLink == true)
        {
            _selected.CopyLink();
            e.Handled = true;
        }
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e) => PasteFrom(e.Data, e.GetPosition(Board));

    void Board_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Board)) return;
        Select(null);
        Keyboard.Focus(Board);
        if (e.ClickCount == 2)
        {
            AddNote(e.GetPosition(Board) - new Vector(10, 7), "");
            e.Handled = true;
            return;
        }
        // Dragging empty space moves the window
        TryDragMove();
    }

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (WindowState == WindowState.Maximized)
        {
            // When maximized, restore only once the mouse actually moves (a plain click does not restore)
            _maxDragStart = e.GetPosition(this);
            TitleBar.CaptureMouse();
            return;
        }
        TryDragMove();
    }

    void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_maxDragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - start.X) < 6 && Math.Abs(p.Y - start.Y) < 6) return;

        _maxDragStart = null;
        TitleBar.ReleaseMouseCapture();
        if (_data.Locked) return;

        // Position the restored window so the cursor stays at the same relative spot on the title bar
        double ratio = p.X / ActualWidth;
        var screen = PointToScreen(p);
        var dip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(screen) ?? screen;
        var restore = RestoreBounds;
        WindowState = WindowState.Normal;
        Left = dip.X - restore.Width * ratio;
        Top = dip.Y - p.Y;
        TryDragMove();
    }

    void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _maxDragStart = null;
        TitleBar.ReleaseMouseCapture();
    }

    void TryDragMove()
    {
        if (_data.Locked || WindowState != WindowState.Normal) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    void ToggleMaximize()
    {
        if (_data.Locked) return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    void OnWindowStateChanged()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = L.T(max ? "tip.restore" : "tip.maximize");
        RootBorder.CornerRadius = new CornerRadius(max ? 0 : 8);
        RootBorder.BorderThickness = new Thickness(max ? 0 : 1);
        WindowGrip.Visibility = max || _data.Locked ? Visibility.Collapsed : Visibility.Visible;
        if (WindowState != WindowState.Minimized)
        {
            _data.Maximized = max;
            ScheduleSave();
        }
    }

    void Window_MouseHover(object sender, MouseEventArgs e)
    {
        _hover = IsMouseOver;
        UpdateChrome();
    }

    void AddNote_Click(object sender, RoutedEventArgs e) => AddNote(null, "");
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Pin_Click(object sender, RoutedEventArgs e) => SetMode(_data.Mode switch
    {
        Modes.Normal => Modes.Top,
        Modes.Top => Modes.Bottom,
        _ => Modes.Normal,
    });

    // ---------- Appearance / window mode ----------

    void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSlider || _data == null) return;
        SetBgOpacity(Math.Round(e.NewValue, 2));
    }

    void SetBgOpacity(double value)
    {
        _data.BgOpacity = value;
        ApplyBackground();
        if (Math.Abs(OpacitySlider.Value - value) > 0.001)
        {
            _syncingSlider = true;
            OpacitySlider.Value = value;
            _syncingSlider = false;
        }
        ScheduleSave();
    }

    void ApplyBackground()
    {
        byte alpha = (byte)Math.Max(2, Math.Round(_data.BgOpacity * 255)); // at 0, clicks would fall through to the desktop
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x1E, 0x1F, 0x24));
        UpdateChrome();
    }

    void UpdateChrome()
    {
        // When (nearly) transparent, show the title bar and border only on hover
        TitleContent.Opacity = _hover ? 1 : _data.BgOpacity >= 0.5 ? 0.5 : 0;
        WindowGrip.Opacity = _hover ? 1 : 0;
        RootBorder.BorderBrush = new SolidColorBrush(
            _hover ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)
            : _data.BgOpacity < 0.1 ? Colors.Transparent
            : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    }

    void SetMode(string mode)
    {
        _data.Mode = mode;
        Topmost = mode == Modes.Top;
        if (mode == Modes.Bottom) PushToBottom();
        UpdatePinButton();
        ScheduleSave();
    }

    void Lock_Click(object sender, RoutedEventArgs e) => SetLocked(!_data.Locked);

    void SetLocked(bool locked)
    {
        _data.Locked = locked;
        ApplyLock();
        ScheduleSave();
    }

    void ApplyLock()
    {
        bool locked = _data.Locked;
        ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResize; // also disables the WindowChrome resize border
        WindowGrip.Visibility = locked || WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
        MaxButton.IsEnabled = !locked;
        LockButton.Content = locked ? "" : "";
        LockButton.ToolTip = L.T(locked ? "tip.locked" : "tip.lock");
        if (locked) LockButton.Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0xB0, 0xFF));
        else LockButton.ClearValue(ForegroundProperty);
    }

    /// <summary>Resize the window to fit images, notes and ink. Content stays where it is on screen.</summary>
    void FitToContent()
    {
        const double pad = 12;
        Rect bounds = Rect.Empty;
        foreach (var item in _data.Items) bounds.Union(new Rect(item.X, item.Y, item.W, item.H));
        if (Ink.Strokes.Count > 0) bounds.Union(Ink.Strokes.GetBounds());
        if (bounds.IsEmpty) return;

        double chromeW = ActualWidth - Board.ActualWidth, chromeH = ActualHeight - Board.ActualHeight;
        double newW = Math.Max(MinWidth, bounds.Width + pad * 2 + chromeW);
        double newH = Math.Max(MinHeight, bounds.Height + pad * 2 + chromeH);
        var shift = new Vector(pad - bounds.X, pad - bounds.Y);

        foreach (var view in _views)
        {
            view.Item.X += shift.X;
            view.Item.Y += shift.Y;
            view.ApplyLayout();
        }
        if (Ink.Strokes.Count > 0)
        {
            var m = Matrix.Identity;
            m.Translate(shift.X, shift.Y);
            Ink.Strokes.Transform(m, false);
            _inkDirty = true;
        }
        Left -= shift.X;
        Top -= shift.Y;
        Width = newW;
        Height = newH;
        ScheduleSave();
    }

    void UpdatePinButton()
    {
        (PinButton.Content, PinButton.ToolTip) = _data.Mode switch
        {
            Modes.Top => ("", L.T("tip.mode.top")),
            Modes.Bottom => ("", L.T("tip.mode.bottom")),
            _ => ("", L.T("tip.mode.normal")),
        };
    }

    void PushToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A borderless window would cover the taskbar when maximized; limit it to the monitor work area
        if (msg == WM_GETMINMAXINFO)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                Marshal.StructureToPtr(mmi, lParam, false);
            }
        }

        // Desktop mode: keep the window at the bottom of the z-order even when clicked
        if (msg == WM_WINDOWPOSCHANGING && _data.Mode == Modes.Bottom)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    // ---------- Context menu ----------

    void Root_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        _menuPoint = e.GetPosition(Board);

    void Root_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var m = RootBorder.ContextMenu!;
        m.Items.Clear();
        var at = new Point(Math.Max(0, _menuPoint.X), Math.Max(0, _menuPoint.Y));

        m.Items.Add(MenuItemOf(L.T("menu.paste"), () => PasteFrom(GetClipboard(), at), "Ctrl+V"));
        m.Items.Add(MenuItemOf(L.T("menu.newNote"), () => AddNote(at, ""), L.T("gesture.doubleClick")));
        if (_trash.Count > 0) m.Items.Add(MenuItemOf(L.T("menu.undoDelete"), UndoDelete, "Ctrl+Z"));
        m.Items.Add(MenuItemOf(L.T("menu.pen"), () => SetPenMode(!_penMode), "P", _penMode));
        if (Ink.Strokes.Count > 0) m.Items.Add(MenuItemOf(L.T("menu.clearInk"), ClearInk));
        m.Items.Add(new Separator());

        m.Items.Add(MenuItemOf(L.T("menu.lock"), () => SetLocked(!_data.Locked), isChecked: _data.Locked));
        var fit = MenuItemOf(L.T("menu.fitToContent"), FitToContent);
        fit.IsEnabled = !_data.Locked && (_views.Count > 0 || Ink.Strokes.Count > 0);
        m.Items.Add(fit);

        var mode = new MenuItem { Header = L.T("menu.windowMode") };
        mode.Items.Add(MenuItemOf(L.T("menu.mode.normal"), () => SetMode(Modes.Normal), isChecked: _data.Mode == Modes.Normal));
        mode.Items.Add(MenuItemOf(L.T("menu.mode.top"), () => SetMode(Modes.Top), isChecked: _data.Mode == Modes.Top));
        mode.Items.Add(MenuItemOf(L.T("menu.mode.bottom"), () => SetMode(Modes.Bottom), isChecked: _data.Mode == Modes.Bottom));
        m.Items.Add(mode);

        var bg = new MenuItem { Header = L.T("menu.background") };
        foreach (var (name, value) in BgLevels)
            bg.Items.Add(MenuItemOf(L.T(name), () => SetBgOpacity(value),
                isChecked: Math.Abs(_data.BgOpacity - value) < 0.01));
        m.Items.Add(bg);

        bool startup = IsStartupEnabled();
        m.Items.Add(MenuItemOf(L.T("menu.startup"), () => SetStartup(!startup), isChecked: startup));
        var language = new MenuItem { Header = L.T("menu.language") };
        language.Items.Add(MenuItemOf(L.T("lang.auto"), () => SetLanguage(null), isChecked: _data.Language == null));
        foreach (var (code, nativeName) in L.Languages)
            language.Items.Add(MenuItemOf(nativeName, () => SetLanguage(code), isChecked: _data.Language == code));
        m.Items.Add(language);
        m.Items.Add(new Separator());
        m.Items.Add(MenuItemOf(L.T("menu.openFolder"), () =>
        {
            Directory.CreateDirectory(Storage.Dir);
            Process.Start("explorer.exe", Storage.Dir);
        }));
        m.Items.Add(MenuItemOf(L.T("menu.clearAll"), ClearAll));
        m.Items.Add(new Separator());
        m.Items.Add(MenuItemOf(L.T("menu.exit"), Close));
    }

    void SetLanguage(string? code)
    {
        if (_data.Language == code) return;
        _data.Language = code;
        SaveNow();
        if (MessageBox.Show(this, L.T("msg.restartLanguage"), "StickerBoard",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        App.RestartRequested = true;
        Close();
    }

    public static MenuItem MenuItemOf(string header, Action action, string? gesture = null, bool isChecked = false)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsChecked = isChecked };
        mi.Click += (_, _) => action();
        return mi;
    }

    // ---------- Run at startup ----------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "StickerBoard";

    static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) != null;
        }
        catch { return false; }
    }

    static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunName, false);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // ---------- Win32 ----------

    const int WM_WINDOWPOSCHANGING = 0x0046;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    const int WM_GETMINMAXINFO = 0x0024;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
