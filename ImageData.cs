using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace StickerBoard;

public record ClipImage(BitmapSource Bitmap, string? File);

/// <summary>Extracts images from clipboard / drag-and-drop data</summary>
public static class ImageData
{
    static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico" };

    public static bool IsImageFile(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static List<ClipImage> Extract(IDataObject data)
    {
        var list = new List<ClipImage>();

        if (TryGet(data, DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files.Where(IsImageFile))
                if (LoadFile(f) is { } b) list.Add(new ClipImage(b, f));
            if (list.Count > 0) return list;
        }

        var bmp = FromPng(data) ?? FromDib(data) ?? TryGet(data, DataFormats.Bitmap) as BitmapSource;
        if (bmp != null)
        {
            if (bmp.CanFreeze && !bmp.IsFrozen) bmp.Freeze();
            list.Add(new ClipImage(bmp, null));
        }
        return list;
    }

    public static BitmapSource? LoadFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Decode(fs);
        }
        catch { return null; }
    }

    public static void CopyToClipboard(BitmapSource bmp)
    {
        var obj = new DataObject();
        obj.SetImage(bmp);
        var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(ms);
        obj.SetData("PNG", ms, false);
        Clipboard.SetDataObject(obj, true);
    }

    static object? TryGet(IDataObject data, string format)
    {
        try { return data.GetDataPresent(format) ? data.GetData(format) : null; }
        catch { return null; }
    }

    static BitmapSource? Decode(Stream s)
    {
        try
        {
            s.Position = 0;
            var dec = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = dec.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch { return null; }
    }

    // Browsers and screenshot tools also provide a "PNG" format that preserves transparency
    static BitmapSource? FromPng(IDataObject data) =>
        TryGet(data, "PNG") is MemoryStream ms ? Decode(ms) : null;

    // WPF's Clipboard.GetImage() mangles some DIBs, so prepend a BMP file header and decode it ourselves
    static BitmapSource? FromDib(IDataObject data)
    {
        if (TryGet(data, DataFormats.Dib) is not MemoryStream dibStream) return null;
        var dib = dibStream.ToArray();
        if (dib.Length < 40) return null;

        int headerSize = BitConverter.ToInt32(dib, 0);
        int bitCount = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int clrUsed = BitConverter.ToInt32(dib, 32);
        int colorTable = clrUsed != 0 ? clrUsed : (bitCount <= 8 ? 1 << bitCount : 0);
        int masks = compression == 3 && headerSize == 40 ? 12 : 0;
        int offset = 14 + headerSize + masks + colorTable * 4;

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(offset).CopyTo(bmp, 10);
        dib.CopyTo(bmp, 14);
        return Decode(new MemoryStream(bmp));
    }
}
