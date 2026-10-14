using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Markup;

namespace StickerBoard;

/// <summary>
/// UI strings. Each language is an embedded JSON file in Strings/ (flat key → text).
/// Missing keys fall back to English. To add a language, add Strings/&lt;code&gt;.json and list it in <see cref="Languages"/>.
/// </summary>
public static class L
{
    public static readonly (string Code, string NativeName)[] Languages =
    {
        ("en", "English"),
        ("ko", "한국어"),
        ("ja", "日本語"),
        ("zh-Hans", "简体中文"),
        ("zh-Hant", "繁體中文"),
    };

    static Dictionary<string, string> _strings = new();
    static Dictionary<string, string> _fallback = new();

    public static string Current { get; private set; } = "en";

    /// <param name="preferred">Language code from settings, or null to follow the Windows UI language.</param>
    public static void Init(string? preferred)
    {
        Current = Languages.Any(l => l.Code == preferred) ? preferred! : FromCulture(CultureInfo.CurrentUICulture);
        _fallback = LoadTable("en");
        _strings = Current == "en" ? _fallback : LoadTable(Current);
    }

    public static string T(string key) =>
        _strings.TryGetValue(key, out var s) ? s
        : _fallback.TryGetValue(key, out var f) ? f
        : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    static string FromCulture(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            bool traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("MO", StringComparison.OrdinalIgnoreCase);
            return traditional ? "zh-Hant" : "zh-Hans";
        }
        return "en";
    }

    static Dictionary<string, string> LoadTable(string code)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream($"StickerBoard.Strings.{code}.json");
            if (stream == null) return new();
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? new();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return new();
        }
    }
}

/// <summary>XAML: <c>ToolTip="{local:Tr tip.close}"</c></summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
