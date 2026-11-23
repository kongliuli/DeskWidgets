using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskWidgets;
internal sealed record ClipboardImage(byte[] Original, byte[] Thumbnail, int Width, int Height, string Hash);
internal static class ClipboardImages
{
    internal const int MaximumPixels = 16 * 1024 * 1024, MaximumEncodedBytes = 16 * 1024 * 1024;
    internal static bool ValidDimensions(long width, long height) => width > 0 && height > 0 && width <= 16384 && height <= 16384 && width * height <= MaximumPixels;
    internal static ClipboardImage FromPng(byte[] data)
    {
        if (data.Length is < 24 or > MaximumEncodedBytes || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ArgumentException("图片不是有效 PNG 或超过 16 MiB。");
        int width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4)), height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        if (!ValidDimensions(width, height)) throw new ArgumentException("图片尺寸超过 1600 万像素安全上限。");
        using var stream = new MemoryStream(data, false);
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (bitmap.PixelWidth != width || bitmap.PixelHeight != height) throw new ArgumentException("图片尺寸不一致。");
        return FromBitmap(bitmap, data);
    }
    internal static ClipboardImage FromBitmap(BitmapSource bitmap, byte[]? original = null)
    {
        if (!ValidDimensions(bitmap.PixelWidth, bitmap.PixelHeight)) throw new ArgumentException("图片尺寸超过 1600 万像素安全上限。");
        original ??= Encode(bitmap);
        if (original.Length > MaximumEncodedBytes) throw new ArgumentException("图片超过 16 MiB，未缓存。");
        double scale = Math.Min(1, Math.Min(640d / bitmap.PixelWidth, 400d / bitmap.PixelHeight));
        BitmapSource thumbnail = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
        return new ClipboardImage(original, Encode(thumbnail), bitmap.PixelWidth, bitmap.PixelHeight, Convert.ToHexString(SHA256.HashData(original)));
    }
    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    internal static System.Windows.DataObject CreateDataObject(ClipboardImage image)
    {
        var data = new System.Windows.DataObject(); data.SetImage(DecodeOriginal(image));
        data.SetData("PNG", new MemoryStream(image.Original, false)); return data;
    }
    internal static BitmapSource DecodeOriginal(ClipboardImage image)
    {
        using var stream = new MemoryStream(image.Original, false);
        return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }
}
