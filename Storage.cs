using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Ink;
using System.Windows.Media.Imaging;

namespace StickerBoard;

public static class ItemTypes
{
    public const string Image = "image";
    public const string Note = "note";
    public const string Link = "link";
}

public static class Modes
{
    public const string Normal = "normal";
    public const string Top = "top";
    public const string Bottom = "bottom";
}

public class BoardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = ItemTypes.Image;
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public int Z { get; set; }
    public string? Image { get; set; }
    /// <summary>Note text, or a link's title (null = not fetched yet, empty = show the address)</summary>
    public string? Text { get; set; }
    public string? Url { get; set; }
    /// <summary>Rich note content (FlowDocument XAML). If missing, Text is used as plain text.</summary>
    public string? Rich { get; set; }
    public string? Font { get; set; }
    public double FontSize { get; set; } = 14;
    public string Color { get; set; } = "#FFF7E07A";
}

public class BoardData
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 520;
    public double Height { get; set; } = 420;
    public string Mode { get; set; } = Modes.Normal;
    public double BgOpacity { get; set; } = 0.6;
    public bool Locked { get; set; }
    public bool Maximized { get; set; }
    /// <summary>UI language code (see L.Languages); null follows the Windows language.</summary>
    public string? Language { get; set; }
    public string PenColor { get; set; } = "#FFFF4D4D";
    public double PenSize { get; set; } = 4;
    public List<BoardItem> Items { get; set; } = new();
}

public static class Storage
{
    public static readonly string Dir =
        Environment.GetEnvironmentVariable("STICKERBOARD_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickerBoard");
    public static string ImageDir => Path.Combine(Dir, "images");
    static string BoardFile => Path.Combine(Dir, "board.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static BoardData Load()
    {
        try
        {
            if (File.Exists(BoardFile))
                return JsonSerializer.Deserialize<BoardData>(File.ReadAllText(BoardFile), JsonOptions) ?? new BoardData();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            // Keep a copy of a corrupt file before it gets overwritten
            try { File.Copy(BoardFile, BoardFile + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
        }
        return new BoardData();
    }

    public static void Save(BoardData data)
    {
        Directory.CreateDirectory(Dir);
        var tmp = BoardFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(tmp, BoardFile, true);
    }

    static string InkFile => Path.Combine(Dir, "ink.isf");

    public static StrokeCollection LoadInk()
    {
        try
        {
            if (File.Exists(InkFile))
            {
                using var fs = File.OpenRead(InkFile);
                return new StrokeCollection(fs);
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return new StrokeCollection();
    }

    public static void SaveInk(StrokeCollection strokes)
    {
        Directory.CreateDirectory(Dir);
        if (strokes.Count == 0)
        {
            File.Delete(InkFile);
            return;
        }
        var tmp = InkFile + ".tmp";
        using (var fs = File.Create(tmp))
            strokes.Save(fs, true);
        File.Move(tmp, InkFile, true);
    }

    public static string SaveImage(BitmapSource bmp)
    {
        Directory.CreateDirectory(ImageDir);
        var name = Guid.NewGuid().ToString("N") + ".png";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(ImageDir, name));
        encoder.Save(fs);
        return name;
    }

    public static string ImportFile(string path)
    {
        Directory.CreateDirectory(ImageDir);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant();
        File.Copy(path, Path.Combine(ImageDir, name));
        return name;
    }

    public static BitmapSource? LoadImage(string name) => ImageData.LoadFile(Path.Combine(ImageDir, name));

    /// <summary>Delete image files no longer referenced by the board</summary>
    public static void CleanupImages(BoardData data)
    {
        try
        {
            if (!Directory.Exists(ImageDir)) return;
            var used = data.Items.Where(i => i.Image != null)
                .Select(i => i.Image!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(ImageDir))
                if (!used.Contains(Path.GetFileName(file)))
                    File.Delete(file);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
