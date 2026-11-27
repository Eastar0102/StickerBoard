using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickerBoard;

/// <summary>Link stickers: a card with the site icon, page title and address; double-click opens it</summary>
public sealed partial class ItemView
{
    Border? _linkIcon;
    TextBlock? _linkTitle;
    TextBlock? _linkAddress;

    Uri? LinkUri => Links.Normalize(Item.Url);

    void BuildLink()
    {
        _linkIcon = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        _linkTitle = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _linkAddress = new TextBlock
        {
            FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var text = new Grid();
        text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_linkAddress, 1);
        text.Children.Add(_linkTitle);
        text.Children.Add(_linkAddress);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_linkIcon, Dock.Left);
        dock.Children.Add(_linkIcon);
        dock.Children.Add(text);

        var card = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x26, 0x27, 0x2C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll,
            Child = dock,
        };
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            OpenLink();
            e.Handled = true;
        };
        AttachDrag(card, resize: false);
        Children.Add(card);

        UpdateLinkText();
        var icon = Item.Image != null ? Storage.LoadImage(Item.Image) : null;
        if (icon == null) Item.Image = null;
        ShowLinkIcon(icon);
        // A title of null was never fetched; a missing icon is retried on each start
        if (Item.Text == null || Item.Image == null) _ = LoadLinkInfo();
    }

    /// <summary>The site icon on a light tile (so dark icons stay visible), or a globe when there is none</summary>
    void ShowLinkIcon(BitmapSource? icon)
    {
        if (_linkIcon == null) return;
        if (icon == null)
        {
            _linkIcon.Background = null;
            _linkIcon.Child = new TextBlock
            {
                Text = "", // globe
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0xB0, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }
        var img = new Image { Source = icon, Width = 18, Height = 18, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        _linkIcon.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        _linkIcon.Child = img;
    }

    void UpdateLinkText()
    {
        if (_linkTitle == null || _linkAddress == null) return;
        var uri = LinkUri;
        var address = uri != null ? Links.Display(uri) : Item.Url ?? "";
        _linkTitle.Text = string.IsNullOrWhiteSpace(Item.Text) ? address : Item.Text;
        _linkAddress.Text = address;
        _linkAddress.Visibility = string.IsNullOrWhiteSpace(Item.Text) ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = (uri?.AbsoluteUri ?? Item.Url) + "\n" + L.T("link.doubleClick");
    }

    async System.Threading.Tasks.Task LoadLinkInfo()
    {
        if (LinkUri is not Uri uri) return;
        var info = await Links.FetchPageAsync(uri, wantTitle: Item.Text == null, wantIcon: Item.Image == null);
        if (info.Title != null && Item.Text == null)
        {
            Item.Text = info.Title;
            UpdateLinkText();
        }
        if (info.Icon != null && Item.Image == null)
        {
            try
            {
                Item.Image = Storage.SaveImage(info.Icon);
                ShowLinkIcon(info.Icon);
            }
            catch (Exception ex) { App.Log(ex); }
        }
        _owner.ScheduleSave();
    }

    void OpenLink()
    {
        if (LinkUri is Uri uri) Links.Open(uri);
    }

    public void CopyLink()
    {
        var text = LinkUri?.AbsoluteUri ?? Item.Url;
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (Exception ex) { App.Log(ex); }
    }

    void RenameLink()
    {
        Links.Prompt(this, L.T("link.titlePrompt"), Item.Text ?? "", text =>
        {
            // An empty title shows the address instead
            Item.Text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
            UpdateLinkText();
            _owner.ScheduleSave();
            return true;
        });
    }

    ContextMenu BuildLinkMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(MainWindow.MenuItemOf(L.T("link.open"), OpenLink, L.T("gesture.doubleClick")));
        m.Items.Add(MainWindow.MenuItemOf(L.T("link.copy"), CopyLink, "Ctrl+C"));
        m.Items.Add(MainWindow.MenuItemOf(L.T("link.rename"), RenameLink));
        m.Items.Add(new Separator());
        AddOrderItems(m);
        m.Items.Add(new Separator());
        m.Items.Add(MainWindow.MenuItemOf(L.T("menu.delete"), () => _owner.DeleteItem(this), "Del"));
        return m;
    }
}
