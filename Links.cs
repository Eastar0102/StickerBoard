using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickerBoard;

/// <summary>URL detection, opening, page titles, hyperlinks in notes and the small input popup</summary>
public static class Links
{
    static readonly Regex UrlRegex = new(@"(?:https?://|www\.)[^\s<>""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // euc-kr, shift_jis, gb2312 pages
        var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) StickerBoard");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(CultureInfo.CurrentUICulture.Name);
        return http;
    }

    /// <summary>Turn user text into an openable URL (adds https:// when the scheme is missing). Null if it isn't one.</summary>
    public static Uri? Normalize(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace)) return null;
        if (!text.Contains("://") && !text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is "http" or "https") return uri.Host.Contains('.') || uri.IsLoopback ? uri : null;
        return uri.Scheme == "mailto" ? uri : null;
    }

    /// <summary>The URL when the whole text is a single web address (for pasting onto the board)</summary>
    public static Uri? AsSingleUrl(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var m = UrlRegex.Match(text);
        return m.Success && m.Index == 0 && m.Length == text.Length ? Normalize(text) : null;
    }

    public static void Open(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>Address without the scheme, for display</summary>
    public static string Display(Uri uri)
    {
        if (uri.Scheme == "mailto") return uri.OriginalString["mailto:".Length..];
        var s = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        var rest = uri.PathAndQuery == "/" ? "" : Uri.UnescapeDataString(uri.PathAndQuery);
        return s + rest;
    }

    // ---------- Page titles and site icons ----------

    const int IconPixels = 64;

    public record PageInfo(string? Title, BitmapSource? Icon);

    public static async Task<PageInfo> FetchPageAsync(Uri uri, bool wantTitle, bool wantIcon)
    {
        if (uri.Scheme is not ("http" or "https")) return new(null, null);
        string? html = null;
        var baseUri = uri;
        try
        {
            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            baseUri = resp.RequestMessage?.RequestUri ?? uri; // after redirects
            var type = resp.Content.Headers.ContentType;
            if (resp.IsSuccessStatusCode && (type?.MediaType is not string mt || mt.Contains("html")))
            {
                var (buf, n) = await ReadLimited(resp, 512 * 1024);
                html = DecodeHtml(buf, n, type?.CharSet);
            }
        }
        catch { }

        var title = wantTitle && html != null ? ParseTitle(html) : null;
        BitmapSource? icon = null;
        if (wantIcon)
            foreach (var candidate in IconCandidates(html, baseUri))
                if ((icon = await FetchIconAsync(candidate)) != null) break;
        return new(title, icon);
    }

    static async Task<(byte[] Buffer, int Length)> ReadLimited(HttpResponseMessage resp, int limit)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buf = new byte[limit];
        int n = 0, read;
        while (n < buf.Length && (read = await stream.ReadAsync(buf.AsMemory(n))) > 0) n += read;
        return (buf, n);
    }

    /// <summary>Icons declared by the page, best size for a small tile first, then /favicon.ico</summary>
    static List<Uri> IconCandidates(string? html, Uri baseUri)
    {
        var found = new List<(Uri Uri, int Rank)>();
        if (html != null)
        {
            foreach (Match tag in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var rel = Attr(tag.Value, "rel")?.ToLowerInvariant();
                if (rel == null || !rel.Split(' ').Any(r => r is "icon" or "apple-touch-icon" or "apple-touch-icon-precomposed")) continue;
                var href = Attr(tag.Value, "href");
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (Attr(tag.Value, "type")?.Contains("svg") == true || href.Split('?')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    continue; // WPF cannot draw SVG
                if (!Uri.TryCreate(baseUri, href.Trim(), out var icon)) continue;

                int size = 0;
                foreach (Match s in Regex.Matches(Attr(tag.Value, "sizes") ?? "", @"(\d+)x\d+"))
                    size = Math.Max(size, int.Parse(s.Groups[1].Value));
                if (size == 0 && rel.Contains("apple-touch-icon")) size = 180;
                // Smallest icon of at least 32px first, unknown sizes next, then smaller ones
                int rank = size == 0 ? 64 : size >= 32 ? size : 10000 - size;
                found.Add((icon, rank));
            }
        }
        var result = found.OrderBy(f => f.Rank).Select(f => f.Uri).Distinct().ToList();
        var fallback = new Uri(baseUri, "/favicon.ico");
        if (!result.Contains(fallback)) result.Add(fallback);
        return result;
    }

    static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
    }

    static async Task<BitmapSource?> FetchIconAsync(Uri uri)
    {
        try
        {
            byte[] bytes;
            if (uri.Scheme == "data")
            {
                var m = Regex.Match(uri.OriginalString, @"^data:image/[\w.+-]+;base64,(.+)$", RegexOptions.Singleline);
                if (!m.Success) return null;
                bytes = Convert.FromBase64String(m.Groups[1].Value);
            }
            else if (uri.Scheme is "http" or "https")
            {
                using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return null;
                var (buf, n) = await ReadLimited(resp, 1024 * 1024);
                bytes = buf[..n];
            }
            else return null;
            return DecodeIcon(bytes);
        }
        catch { return null; }
    }

    /// <summary>The largest frame (an .ico holds several sizes), scaled down to at most IconPixels</summary>
    static BitmapSource? DecodeIcon(byte[] bytes)
    {
        try
        {
            var dec = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            if (frame.PixelWidth < 8 || frame.PixelHeight < 8) return null;
            double scale = Math.Min(1.0, (double)IconPixels / Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale < 1) frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            frame.Freeze();
            return frame;
        }
        catch { return null; }
    }

    static string DecodeHtml(byte[] buf, int length, string? charset)
    {
        if (string.IsNullOrEmpty(charset))
        {
            var head = Encoding.ASCII.GetString(buf, 0, Math.Min(length, 4096));
            var m = Regex.Match(head, @"<meta[^>]+charset\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
            if (m.Success) charset = m.Groups[1].Value;
        }
        Encoding enc;
        try { enc = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch { enc = Encoding.UTF8; }
        return enc.GetString(buf, 0, length);
    }

    static string? ParseTitle(string html)
    {
        string? title = null;
        foreach (Match meta in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(meta.Value, @"(?:property|name)\s*=\s*[""']og:title[""']", RegexOptions.IgnoreCase)) continue;
            var c = Regex.Match(meta.Value, @"content\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
            if (c.Success) title = c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value;
            break;
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            var t = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (t.Success) title = t.Groups[1].Value;
        }
        if (string.IsNullOrWhiteSpace(title)) return null;
        title = Regex.Replace(WebUtility.HtmlDecode(title), @"\s+", " ").Trim();
        return title.Length > 200 ? title[..200] + "…" : title;
    }

    // ---------- Hyperlinks in notes ----------

    public static Hyperlink? LinkAt(TextPointer? pos)
    {
        for (var e = pos?.Parent as DependencyObject; e != null; e = (e as TextElement)?.Parent)
            if (e is Hyperlink h) return h;
        return null;
    }

    /// <summary>Replace a hyperlink with its contents</summary>
    public static void Unwrap(Hyperlink link)
    {
        var siblings = link.SiblingInlines;
        if (siblings == null) return;
        foreach (var inline in link.Inlines.ToList())
        {
            link.Inlines.Remove(inline);
            siblings.InsertBefore(link, inline);
        }
        siblings.Remove(link);
    }

    /// <summary>Number of text characters between the paragraph start and <paramref name="pos"/></summary>
    public static int CharOffset(Paragraph p, TextPointer pos)
    {
        int count = 0;
        for (var q = p.ContentStart; q != null && q.CompareTo(pos) < 0; )
        {
            if (q.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = q.GetTextRunLength(LogicalDirection.Forward);
                int toPos = q.GetOffsetToPosition(pos);
                if (toPos <= len) return count + toPos;
                count += len;
                q = q.GetPositionAtOffset(len);
            }
            else q = q.GetNextContextPosition(LogicalDirection.Forward);
        }
        return count;
    }

    /// <summary>The position <paramref name="chars"/> text characters into the paragraph</summary>
    public static TextPointer AtCharOffset(Paragraph p, int chars)
    {
        var q = p.ContentStart;
        while (q.CompareTo(p.ContentEnd) < 0)
        {
            if (q.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = q.GetTextRunLength(LogicalDirection.Forward);
                if (chars <= len) return q.GetPositionAtOffset(chars)!;
                chars -= len;
                q = q.GetPositionAtOffset(len)!;
            }
            else q = q.GetNextContextPosition(LogicalDirection.Forward)!;
        }
        return p.ContentEnd;
    }

    /// <summary>The paragraph's text, matching the offsets of CharOffset</summary>
    public static string CharText(Paragraph p)
    {
        var sb = new StringBuilder();
        for (var q = p.ContentStart; q != null && q.CompareTo(p.ContentEnd) < 0; q = q.GetNextContextPosition(LogicalDirection.Forward))
            if (q.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                sb.Append(q.GetTextInRun(LogicalDirection.Forward));
        return sb.ToString();
    }

    public static Hyperlink MakeLink(TextPointer start, TextPointer end, Uri uri) =>
        new(start, end) { NavigateUri = uri };

    /// <summary>Turn plain-text URLs in the blocks into hyperlinks</summary>
    public static void Linkify(IEnumerable<Block> blocks)
    {
        var found = new List<(TextPointer Start, TextPointer End, Uri Uri)>();
        foreach (var block in blocks)
            foreach (var run in Runs(block))
            {
                foreach (Match m in UrlRegex.Matches(run.Text))
                {
                    var text = TrimTrailing(m.Value);
                    if (Normalize(text) is not Uri uri) continue;
                    var start = run.ContentStart.GetPositionAtOffset(m.Index);
                    var end = run.ContentStart.GetPositionAtOffset(m.Index + text.Length);
                    if (start == null || end == null) continue;
                    found.Add((start, end, uri));
                }
            }
        foreach (var (start, end, uri) in found)
        {
            try { MakeLink(start, end, uri); }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    // Sentence punctuation right after a URL is not part of it; ")" only when unbalanced
    static string TrimTrailing(string url)
    {
        while (url.Length > 0)
        {
            char c = url[^1];
            if (".,;:!?'\"]}>。、".Contains(c)
                || (c == ')' && url.Count(ch => ch == '(') < url.Count(ch => ch == ')')))
                url = url[..^1];
            else break;
        }
        return url;
    }

    static IEnumerable<Run> Runs(Block block)
    {
        switch (block)
        {
            case Paragraph p:
                foreach (var r in Runs(p.Inlines)) yield return r;
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var b in item.Blocks)
                        foreach (var r in Runs(b)) yield return r;
                break;
            case Section s:
                foreach (var b in s.Blocks)
                    foreach (var r in Runs(b)) yield return r;
                break;
        }
    }

    static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            if (inline is Run r) yield return r;
            else if (inline is Span s and not Hyperlink)
                foreach (var inner in Runs(s.Inlines)) yield return inner;
        }
    }

    // ---------- Input popup ----------

    /// <summary>A one-line input below <paramref name="target"/>. Enter calls <paramref name="commit"/>; returning false keeps it open.</summary>
    public static void Prompt(FrameworkElement target, string label, string initial, Func<string, bool> commit, Action? closed = null)
    {
        var white = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        var box = new TextBox
        {
            Text = initial, Width = 260, FontSize = 13, Padding = new Thickness(4, 3, 4, 3),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Foreground = white, CaretBrush = white, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)),
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Margin = new Thickness(1, 0, 0, 4),
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            FontFamily = new FontFamily(L.T("ui.font")),
        });
        panel.Children.Add(box);
        var popup = new Popup
        {
            PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 4,
            StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x22, 0x23, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = panel,
            },
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (commit(box.Text)) popup.IsOpen = false;
                else box.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
        };
        popup.Opened += (_, _) =>
        {
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        };
        popup.Closed += (_, _) => closed?.Invoke();
        popup.IsOpen = true;
    }
}
