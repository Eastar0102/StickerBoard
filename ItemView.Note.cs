using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace StickerBoard;

/// <summary>Notes: rich text, format bar, checklists, fonts and the text-only style</summary>
public sealed partial class ItemView
{
    const string CheckOff = "☐";
    const string CheckOn = "☑";
    
    // A fully transparent color (alpha 0) means a text-only note: black RGB = white text, white RGB = black text
    public static readonly (string Name, string Hex)[] NoteColors =
    {
        ("color.yellow", "#FFF7E07A"), ("color.pink", "#FFF5B3C8"), ("color.sky", "#FFAED6F5"),
        ("color.lime", "#FFBDE5A8"), ("color.white", "#FFF4F4F4"), ("color.black", "#FF2B2B30"),
        ("color.textOnlyWhite", "#00000000"), ("color.textOnlyBlack", "#00FFFFFF"),
    };

    static readonly (string Name, string Hex)[] TextColors =
    {
        ("color.default", ""), ("color.red", "#FFE53935"), ("color.orange", "#FFFB8C00"), ("color.yellow", "#FFFDD835"),
        ("color.green", "#FF43A047"), ("color.blue", "#FF1E88E5"), ("color.purple", "#FF8E24AA"), ("color.gray", "#FF8A8A8A"),
        ("color.white", "#FFFFFFFF"), ("color.black", "#FF111111"),
    };

    static readonly (string Name, string Hex)[] HighlightColors =
    {
        ("color.none", ""), ("color.yellow", "#FFFFF176"), ("color.lime", "#FFB9F6CA"), ("color.pink", "#FFFFCDD2"),
        ("color.sky", "#FFBBDEFB"), ("color.orange", "#FFFFE0B2"),
    };

    // Shown at the top of the font menu when installed (Korean, Japanese, Chinese and Latin favorites)
    static readonly string[] PreferredFonts =
    {
        "맑은 고딕", "나눔고딕", "나눔바른고딕", "나눔스퀘어", "나눔스퀘어 네오", "Pretendard", "나눔명조",
        "나눔손글씨 펜", "나눔손글씨 붓", "배달의민족 주아", "배달의민족 도현", "배달의민족 한나체 Pro",
        "굴림", "돋움", "바탕", "궁서",
        "Yu Gothic UI", "Meiryo UI", "Microsoft YaHei UI", "Microsoft JhengHei UI", "DengXian",
        "Segoe UI", "Consolas", "Segoe Print", "Comic Sans MS",
    };

    static List<(string Name, FontFamily Family)>? _fontCache;

    static readonly double[] PresetSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 60, 72 };
    const double MinFontSize = 6;
    const double MaxFontSize = 96;

    TextBox? _sizeBox;
    Border? _noteBody;
    Border? _noteGrip;
    Canvas? _formatHost;
    Border? _formatBar;
    bool _textDirty;

    public RichTextBox? Editor { get; private set; }

    bool IsTextOnly => ParseColor(Item.Color).A == 0;

    // ---------- Construction ----------

    void BuildNote()
    {
        _noteGrip = new Border
        {
            Height = 14,
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Cursor = Cursors.SizeAll,
            ToolTip = L.T("note.dragToMove"),
        };
        DockPanel.SetDock(_noteGrip, Dock.Top);
        AttachDrag(_noteGrip, resize: false);

        Editor = new RichTextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Padding = new Thickness(4, 2, 4, 14),
            IsInactiveSelectionHighlightEnabled = true,
            Document = LoadDocument(),
        };
        Editor.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph))
        {
            Setters = { new Setter(Block.MarginProperty, new Thickness(0)) },
        });
        Editor.Resources.Add(typeof(List), new Style(typeof(List))
        {
            Setters = { new Setter(Block.MarginProperty, new Thickness(0)) },
        });
        ApplyDocumentDefaults();

        Editor.TextChanged += (_, _) =>
        {
            _textDirty = true;
            _owner.ScheduleSave();
        };
        Editor.PreviewMouseWheel += OnNoteWheel;
        Editor.PreviewMouseLeftButtonDown += OnEditorMouseDown;
        Editor.PreviewKeyDown += OnEditorKeyDown;
        Editor.SelectionChanged += (_, _) => UpdateSizeBox();
        DataObject.AddPastingHandler(Editor, OnEditorPasting);

        var dock = new DockPanel();
        dock.Children.Add(_noteGrip);
        dock.Children.Add(Editor);
        _noteBody = new Border { CornerRadius = new CornerRadius(4), Child = dock };
        Children.Add(_noteBody);

        // The format bar is drawn outside the note bounds, so it lives on a zero-size Canvas (a Grid would clip it)
        _formatBar = BuildFormatBar();
        _formatHost = new Canvas
        {
            Width = 0, Height = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };
        _formatHost.Children.Add(_formatBar);
        Children.Add(_formatHost);

        ApplyNoteColor();
    }

    FlowDocument LoadDocument()
    {
        if (!string.IsNullOrEmpty(Item.Rich))
        {
            try
            {
                if (XamlReader.Parse(Item.Rich) is FlowDocument doc) return doc;
            }
            catch (Exception ex) { App.Log(ex); }
        }
        var plain = new FlowDocument();
        foreach (var line in (Item.Text ?? "").Replace("\r\n", "\n").Split('\n'))
            plain.Blocks.Add(new Paragraph(new Run(line)));
        return plain;
    }

    /// <summary>Note-wide font and size come from BoardItem (overriding document properties in the saved XAML)</summary>
    void ApplyDocumentDefaults()
    {
        var doc = Editor!.Document;
        doc.PagePadding = new Thickness(0);
        doc.FontFamily = new FontFamily(Item.Font ?? L.T("font.default"));
        doc.FontSize = Item.FontSize;
        doc.ClearValue(FlowDocument.BackgroundProperty);
    }

    /// <summary>Write the edited document back to BoardItem (called right before saving)</summary>
    public void FlushText()
    {
        if (!_textDirty || Editor == null) return;
        _textDirty = false;
        try
        {
            Item.Rich = XamlWriter.Save(Editor.Document);
            Item.Text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.TrimEnd('\r', '\n');
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void ApplyNoteColor()
    {
        if (_noteBody == null || _noteGrip == null || Editor == null) return;
        var color = ParseColor(Item.Color);
        Color fg;
        if (color.A == 0)
        {
            // Text only: no background, a contrasting glow around the glyphs keeps it readable on any wallpaper
            bool whiteText = color.R < 128;
            fg = whiteText ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);
            _noteBody.Background = new SolidColorBrush(Color.FromArgb(1, 0xFF, 0xFF, 0xFF)); // alpha 0 would let clicks fall through
            _noteGrip.Background = new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80));
            Editor.Effect = new DropShadowEffect
            {
                Color = whiteText ? Colors.Black : Colors.White,
                ShadowDepth = 0,
                BlurRadius = 5,
                Opacity = 1,
            };
        }
        else
        {
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            fg = luminance < 0.5 ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x22, 0x22, 0x22);
            _noteBody.Background = new SolidColorBrush(color);
            _noteGrip.Background = new SolidColorBrush(luminance < 0.5
                ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x26, 0, 0, 0));
            Editor.Effect = null;
        }
        Editor.Document.Foreground = new SolidColorBrush(fg);
        Editor.CaretBrush = new SolidColorBrush(fg);
        UpdateNoteChrome();
    }

    void UpdateNoteChrome()
    {
        if (_noteGrip == null || _formatHost == null || _formatBar == null) return;
        // Text-only notes hide the drag grip unless hovered or selected
        _noteGrip.Opacity = !IsTextOnly || _selected || _dragging || IsMouseOver ? 1 : 0;

        _formatHost.Visibility = _selected ? Visibility.Visible : Visibility.Collapsed;
        if (!_selected) return;
        UpdateSizeBox();
        _formatBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var bar = _formatBar.DesiredSize;
        bool below = Item.Y < bar.Height + 6;
        bool alignRight = Item.X + bar.Width > _owner.Board.ActualWidth && Item.X + Item.W > bar.Width;
        Canvas.SetTop(_formatBar, below ? Item.H + 6 : -bar.Height - 6);
        Canvas.SetLeft(_formatBar, alignRight ? Item.W - bar.Width : 0);
    }

    ContextMenu BuildNoteMenu()
    {
        var m = new ContextMenu();
        var colors = new MenuItem { Header = L.T("note.color") };
        foreach (var (name, hex) in NoteColors)
        {
            var mi = MainWindow.MenuItemOf(L.T(name), () =>
            {
                Item.Color = hex;
                ApplyNoteColor();
                _owner.ScheduleSave();
            });
            var c = ParseColor(hex);
            mi.Icon = c.A == 0
                ? new Border
                {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(2),
                    Background = Brushes.Gray,
                    Child = new TextBlock
                    {
                        Text = L.T("font.sample"), FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = c.R < 128 ? Brushes.White : Brushes.Black,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                }
                : new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(c),
                    BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5),
                };
            colors.Items.Add(mi);
        }
        m.Items.Add(colors);
        m.Items.Add(MainWindow.MenuItemOf(L.T("note.bigger"), () => ChangeFontSize(+2), L.T("gesture.ctrlWheel")));
        m.Items.Add(MainWindow.MenuItemOf(L.T("note.smaller"), () => ChangeFontSize(-2)));
        m.Items.Add(new Separator());
        AddOrderItems(m);
        m.Items.Add(new Separator());
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.delete"), () => _owner.DeleteItem(this)));
        return m;
    }

    // ---------- Format bar ----------

    Border BuildFormatBar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        panel.Children.Add(FormatButton(BarLabel("B", weight: FontWeights.Bold), L.T("fmt.bold"),
            () => EditingCommands.ToggleBold.Execute(null, Editor)));
        panel.Children.Add(FormatButton(BarLabel("I", style: FontStyles.Italic, font: "Georgia"), L.T("fmt.italic"),
            () => EditingCommands.ToggleItalic.Execute(null, Editor)));
        panel.Children.Add(FormatButton(BarLabel("U", decorations: TextDecorations.Underline), L.T("fmt.underline"),
            () => EditingCommands.ToggleUnderline.Execute(null, Editor)));
        panel.Children.Add(FormatButton(BarLabel("S", decorations: TextDecorations.Strikethrough), L.T("fmt.strike"),
            ToggleStrike));
        panel.Children.Add(BarSeparator());

        var colorLabel = new StackPanel();
        colorLabel.Children.Add(BarLabel("A", weight: FontWeights.Bold));
        colorLabel.Children.Add(new Border { Height = 3, Width = 14, Margin = new Thickness(0, -1, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) });
        Border? colorButton = null;
        colorButton = FormatButton(colorLabel, L.T("fmt.textColor"), () => ShowMenu(colorButton!, ColorMenu(TextColors, ApplyTextColor)), opensMenu: true);
        panel.Children.Add(colorButton);

        Border? highlightButton = null;
        highlightButton = FormatButton(Glyph(""), L.T("fmt.highlight"),
            () => ShowMenu(highlightButton!, ColorMenu(HighlightColors, ApplyHighlight)), opensMenu: true);
        panel.Children.Add(highlightButton);

        panel.Children.Add(FormatButton(Glyph(""), L.T("fmt.bigger"), () => ChangeSelectionFontSize(+2)));
        panel.Children.Add(FormatButton(Glyph(""), L.T("fmt.smaller"), () => ChangeSelectionFontSize(-2)));
        panel.Children.Add(BuildSizeBox());
        panel.Children.Add(BarSeparator());

        panel.Children.Add(FormatButton(Glyph(""), L.T("fmt.bullets"),
            () => EditingCommands.ToggleBullets.Execute(null, Editor)));
        panel.Children.Add(FormatButton(BarLabel(CheckOn, font: "Segoe UI Symbol"), L.T("fmt.checklist"), ToggleChecklist));
        panel.Children.Add(BarSeparator());

        Border? fontButton = null;
        fontButton = FormatButton(BarLabel(L.T("fmt.fontButton")), L.T("fmt.font"),
            () => ShowMenu(fontButton!, FontMenu()), opensMenu: true);
        fontButton.Width = 34;
        panel.Children.Add(fontButton);

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(4, 3, 4, 3),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x22, 0x23, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
    }

    Border FormatButton(FrameworkElement content, string tip, Action click, bool opensMenu = false)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var hover = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        var b = new Border
        {
            Width = 26, Height = 24, CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent, Child = content, ToolTip = tip, Cursor = Cursors.Hand,
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        // Must not take focus, or the text selection would be lost
        b.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (Editor == null) return;
            click();
            // Refocusing here would instantly close a menu we just opened (ShowMenu refocuses on close)
            if (!opensMenu) Editor.Focus();
        };
        return b;
    }

    static TextBlock BarLabel(string text, FontWeight? weight = null, FontStyle? style = null,
        TextDecorationCollection? decorations = null, string? font = null) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = weight ?? FontWeights.Normal,
        FontStyle = style ?? FontStyles.Normal,
        TextDecorations = decorations,
        FontFamily = new FontFamily(font ?? L.T("ui.font")),
        Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
    };

    static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
    };

    static Border BarSeparator() => new()
    {
        Width = 1, Height = 16, Margin = new Thickness(4, 0, 4, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
    };

    void ShowMenu(FrameworkElement target, ContextMenu menu)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.Closed += (_, _) => Editor?.Focus();
        menu.IsOpen = true;
    }

    ContextMenu ColorMenu((string Name, string Hex)[] colors, Action<string> apply)
    {
        var m = new ContextMenu();
        foreach (var (name, hex) in colors)
        {
            var mi = MainWindow.MenuItemOf(L.T(name), () => apply(hex));
            mi.Icon = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                Background = hex == "" ? Brushes.Transparent : new SolidColorBrush(ParseColor(hex)),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(hex == "" ? 1 : 0.5),
            };
            m.Items.Add(mi);
        }
        return m;
    }

    ContextMenu FontMenu()
    {
        var m = new ContextMenu();
        var fonts = AllFonts();
        var byName = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
            foreach (var name in family.FamilyNames.Values)
                byName.TryAdd(name, family);

        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in PreferredFonts)
            if (byName.TryGetValue(name, out var family) && shown.Add(family.Source))
                m.Items.Add(FontItem(name, family, preview: true));

        // Fill the full list lazily on first open
        var all = new MenuItem { Header = L.F("font.all", fonts.Count) };
        all.Items.Add(new MenuItem { Header = "…" });
        all.SubmenuOpened += (_, _) =>
        {
            if (all.Items.Count > 1 || all.Items[0] is not MenuItem { Header: "…" }) return;
            all.Items.Clear();
            foreach (var (name, family) in fonts) all.Items.Add(FontItem(name, family, preview: false));
        };
        if (m.Items.Count > 0) m.Items.Add(new System.Windows.Controls.Separator());
        m.Items.Add(all);
        return m;
    }

    MenuItem FontItem(string name, FontFamily family, bool preview)
    {
        var mi = new MenuItem
        {
            Header = preview ? new TextBlock { Text = name, FontFamily = family, FontSize = 14 } : name,
        };
        mi.Click += (_, _) => ApplyFont(family);
        return mi;
    }

    static List<(string Name, FontFamily Family)> AllFonts()
    {
        if (_fontCache != null) return _fontCache;
        var ko = XmlLanguage.GetLanguage("ko-kr");
        var en = XmlLanguage.GetLanguage("en-us");
        _fontCache = Fonts.SystemFontFamilies
            .Select(f => (Name: f.FamilyNames.TryGetValue(ko, out var k) ? k
                              : f.FamilyNames.TryGetValue(en, out var e) ? e : f.Source,
                          Family: f))
            .GroupBy(x => x.Name).Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();
        return _fontCache;
    }

    // ---------- Applying formatting ----------

    void ToggleStrike()
    {
        var sel = Editor!.Selection;
        var current = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool has = current?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;
        var result = new TextDecorationCollection();
        if (current != null)
            foreach (var d in current.Where(d => d.Location != TextDecorationLocation.Strikethrough))
                result.Add(d);
        if (!has) result.Add(TextDecorations.Strikethrough);
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, result);
    }

    void ApplyTextColor(string hex) =>
        Editor!.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
            hex == "" ? Editor.Document.Foreground : new SolidColorBrush(ParseColor(hex)));

    void ApplyHighlight(string hex) =>
        Editor!.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
            hex == "" ? null : new SolidColorBrush(ParseColor(hex)));

    void ApplyFont(FontFamily family)
    {
        var sel = Editor!.Selection;
        if (sel.IsEmpty)
        {
            Item.Font = family.Source;
            Editor.Document.FontFamily = family;
            _owner.ScheduleSave();
        }
        else
        {
            sel.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
        }
    }

    /// <summary>Size of the selected text; the whole note's size when nothing is selected. Null if the selection is mixed.</summary>
    double? CurrentFontSize()
    {
        var sel = Editor!.Selection;
        if (sel.IsEmpty) return Item.FontSize;
        return sel.GetPropertyValue(TextElement.FontSizeProperty) is double d ? d : null;
    }

    void ChangeSelectionFontSize(double delta) => SetFontSize((CurrentFontSize() ?? Item.FontSize) + delta);

    /// <summary>Apply a size to the selected text, or to the whole note when nothing is selected</summary>
    void SetFontSize(double size)
    {
        size = Math.Round(Math.Clamp(size, MinFontSize, MaxFontSize), 1);
        var sel = Editor!.Selection;
        if (sel.IsEmpty) SetNoteFontSize(size);
        else sel.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        UpdateSizeBox(force: true);
    }

    void ChangeFontSize(double delta) => SetNoteFontSize(Item.FontSize + delta);

    void SetNoteFontSize(double size)
    {
        Item.FontSize = Math.Round(Math.Clamp(size, MinFontSize, MaxFontSize), 1);
        if (Editor != null) Editor.Document.FontSize = Item.FontSize;
        _owner.ScheduleSave();
        UpdateSizeBox(force: true);
    }

    // ---------- Font size box ----------

    FrameworkElement BuildSizeBox()
    {
        var white = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        _sizeBox = new TextBox
        {
            Width = 32, Height = 20, Margin = new Thickness(3, 0, 0, 0), MaxLength = 4,
            FontSize = 12, TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Foreground = white, CaretBrush = white, SelectionBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)),
            ToolTip = L.T("fmt.fontSize"),
        };
        _sizeBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // First click focuses and selects the number so it can be typed over
            if (_sizeBox.IsKeyboardFocusWithin) return;
            _sizeBox.Focus();
            e.Handled = true;
        };
        _sizeBox.GotKeyboardFocus += (_, _) => _sizeBox.SelectAll();
        _sizeBox.LostKeyboardFocus += (_, _) => ApplySizeBox();
        _sizeBox.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ApplySizeBox();
                    Editor?.Focus();
                    break;
                case Key.Escape:
                    UpdateSizeBox(force: true);
                    Editor?.Focus();
                    break;
                case Key.Up:
                case Key.Down:
                    SetFontSize((CurrentFontSize() ?? Item.FontSize) + (e.Key == Key.Up ? 1 : -1));
                    _sizeBox.SelectAll();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
        _sizeBox.PreviewMouseWheel += (_, e) =>
        {
            SetFontSize((CurrentFontSize() ?? Item.FontSize) + (e.Delta > 0 ? 1 : -1));
            e.Handled = true;
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_sizeBox);
        Border? presets = null;
        presets = FormatButton(Glyph(""), L.T("fmt.fontSize"), () => ShowMenu(presets!, SizeMenu()), opensMenu: true);
        presets.Width = 18;
        ((TextBlock)presets.Child).FontSize = 9;
        panel.Children.Add(presets);
        return panel;
    }

    ContextMenu SizeMenu()
    {
        var m = new ContextMenu();
        var current = CurrentFontSize();
        foreach (var size in PresetSizes)
            m.Items.Add(MainWindow.MenuItemOf(size.ToString(CultureInfo.InvariantCulture), () => SetFontSize(size),
                isChecked: current is double c && Math.Abs(c - size) < 0.05));
        return m;
    }

    void ApplySizeBox()
    {
        if (_sizeBox == null || Editor == null) return;
        var text = _sizeBox.Text.Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0)
        {
            if (CurrentFontSize() is not double current || Math.Abs(current - size) > 0.05)
                SetFontSize(size);
            else
                UpdateSizeBox(force: true);
        }
        else
        {
            UpdateSizeBox(force: true); // not a number: show the real size again
        }
    }

    void UpdateSizeBox(bool force = false)
    {
        if (_sizeBox == null || Editor == null) return;
        if (!force && _sizeBox.IsKeyboardFocusWithin) return; // don't overwrite what the user is typing
        _sizeBox.Text = CurrentFontSize() is double size ? size.ToString("0.#", CultureInfo.InvariantCulture) : "";
    }

    void OnNoteWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ChangeFontSize(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    void OnEditorPasting(object sender, DataObjectPastingEventArgs e)
    {
        // Paste as plain text so fonts/colors/backgrounds from other apps do not leak in
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
            e.DataObject = new DataObject(DataFormats.UnicodeText, text);
            e.FormatToApply = DataFormats.UnicodeText;
        }
        else
        {
            e.CancelCommand();
        }
    }

    // ---------- Checklists ----------

    static TextPointer? NextChar(TextPointer p) => p.GetNextInsertionPosition(LogicalDirection.Forward);

    /// <summary>The check mark at the start of a paragraph (null if none)</summary>
    static (TextRange? Range, string? Mark) CheckMark(Paragraph p)
    {
        var start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var next = NextChar(start);
        if (next == null || next.CompareTo(p.ContentEnd) > 0) return (null, null);
        var range = new TextRange(start, next);
        return range.Text is CheckOff or CheckOn ? (range, range.Text) : (null, null);
    }

    // Font fallback would draw ☑ with the emoji font (unlike ☐), so pin marks to a symbol font
    static readonly FontFamily MarkFont = new("Segoe UI Symbol");

    static void ToggleCheck(TextRange mark)
    {
        mark.Text = mark.Text == CheckOff ? CheckOn : CheckOff;
        mark.ApplyPropertyValue(TextElement.FontFamilyProperty, MarkFont);
    }

    static void AddCheckMark(Paragraph p)
    {
        p.ContentStart.GetInsertionPosition(LogicalDirection.Forward).InsertTextInRun(CheckOff + " ");
        CheckMark(p).Range?.ApplyPropertyValue(TextElement.FontFamilyProperty, MarkFont);
    }

    List<Paragraph> SelectedParagraphs()
    {
        var result = new List<Paragraph>();
        var sel = Editor!.Selection;
        Block? block = sel.Start.Paragraph;
        var last = sel.End.Paragraph;
        while (block != null)
        {
            if (block is Paragraph p) result.Add(p);
            if (block == last) break;
            block = block.NextBlock;
        }
        return result;
    }

    /// <summary>Add or remove check boxes on the selected lines</summary>
    void ToggleChecklist()
    {
        var paragraphs = SelectedParagraphs();
        if (paragraphs.Count == 0) return;
        bool allHave = paragraphs.All(p => CheckMark(p).Range != null);
        foreach (var p in paragraphs)
        {
            var (mark, _) = CheckMark(p);
            if (allHave && mark != null)
            {
                var end = mark.End;
                if (NextChar(end) is { } after && new TextRange(end, after).Text == " ") end = after;
                new TextRange(mark.Start, end).Text = "";
            }
            else if (!allHave && mark == null)
            {
                AddCheckMark(p);
            }
        }
    }

    void OnEditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking the check box character toggles it
        var pos = Editor!.GetPositionFromPoint(e.GetPosition(Editor), false);
        if (pos?.Paragraph is not Paragraph p) return;
        var (mark, _) = CheckMark(p);
        if (mark == null || pos.CompareTo(mark.Start) < 0 || pos.CompareTo(mark.End) > 0) return;
        ToggleCheck(mark);
        e.Handled = true;
    }

    void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.X && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ToggleStrike();
            e.Handled = true;
            return;
        }
        // Enter pressed during IME composition (e.g. Korean) arrives as ImeProcessed
        bool composing = e.Key == Key.ImeProcessed;
        if ((composing ? e.ImeProcessedKey : e.Key) != Key.Enter) return;
        var p = Editor!.CaretPosition.Paragraph;
        if (p == null) return;
        var (mark, _) = CheckMark(p);

        if (mods == ModifierKeys.Control)
        {
            // Ctrl+Enter: toggle the check on a checklist line, otherwise add a check box
            if (mark != null) ToggleCheck(mark);
            else AddCheckMark(p);
            e.Handled = true;
            return;
        }
        if (mods != ModifierKeys.None || mark == null) return;

        if (!composing && new TextRange(p.ContentStart, p.ContentEnd).Text.Trim() is CheckOff or CheckOn)
        {
            // Enter on an empty checklist line ends the list
            new TextRange(p.ContentStart, p.ContentEnd).Text = "";
            e.Handled = true;
            return;
        }

        // Let the editor insert the line break itself (handling it here pushes an uncommitted IME character to the next line).
        // Once the break is in, continue the checklist on the new line.
        Dispatcher.BeginInvoke(() =>
        {
            var current = Editor.CaretPosition.Paragraph;
            if (current == null || current == p || current.PreviousBlock != p) return;
            if (CheckMark(current).Range != null) return;
            AddCheckMark(current);
            var caret = current.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            caret = NextChar(caret) ?? caret;
            caret = NextChar(caret) ?? caret;
            Editor.CaretPosition = caret;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
