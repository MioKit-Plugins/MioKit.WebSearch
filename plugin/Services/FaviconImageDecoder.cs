using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WebSearch.Services;

internal static class FaviconImageDecoder
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Gif87a => "GIF87a"u8;
    private static ReadOnlySpan<byte> Gif89a => "GIF89a"u8;

    public static bool IsNonImagePayload(ReadOnlySpan<byte> bytes)
    {
        var span = SkipBomAndWhitespace(bytes);
        if (span.Length == 0)
        {
            return true;
        }

        if (span.StartsWith("<!DOCTYPE"u8)
            || span.StartsWith("<!doctype"u8)
            || span.StartsWith("<html"u8)
            || span.StartsWith("<HTML"u8)
            || span.StartsWith("{"u8)
            || LooksLikeSvg(span))
        {
            return true;
        }

        return false;
    }

    public static bool TryLoadBitmap(byte[] bytes, [NotNullWhen(true)] out Bitmap? bitmap)
    {
        bitmap = null;
        if (bytes.Length < 8 || IsNonImagePayload(bytes))
        {
            return false;
        }

        if (IsIcoOrCur(bytes))
        {
            return TryLoadIco(bytes, out bitmap);
        }

        return TryLoadSkia(bytes, out bitmap);
    }

    internal static bool IsIcoOrCur(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 6
        && BinaryPrimitives.ReadUInt16LittleEndian(bytes) == 0
        && BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) is 1 or 2;

    internal static bool TryExtractIcoPayload(byte[] bytes, [NotNullWhen(true)] out byte[]? payload)
    {
        payload = null;
        if (!TrySelectIcoImage(bytes, out var offset, out var size, out _, out _))
        {
            return false;
        }

        payload = bytes.AsSpan(offset, size).ToArray();
        return true;
    }

    internal static bool TryDecodeIcoDib(
        byte[] bytes,
        out int width,
        out int height,
        [NotNullWhen(true)] out byte[]? bgra)
    {
        width = 0;
        height = 0;
        bgra = null;
        if (!TrySelectIcoImage(bytes, out var offset, out var size, out var entryWidth, out var entryHeight))
        {
            return false;
        }

        var payload = bytes.AsSpan(offset, size);
        if (payload.StartsWith(PngSignature))
        {
            return false;
        }

        return TryDecodeDib(payload, entryWidth, entryHeight, out width, out height, out bgra);
    }

    private static bool TryLoadIco(byte[] bytes, [NotNullWhen(true)] out Bitmap? bitmap)
    {
        bitmap = null;
        if (!TryExtractIcoPayload(bytes, out var payload))
        {
            return false;
        }

        if (payload.StartsWith(PngSignature)
            || payload.StartsWith(JpegSignature)
            || payload.StartsWith(Gif87a)
            || payload.StartsWith(Gif89a))
        {
            return TryLoadSkia(payload, out bitmap);
        }

        if (!TrySelectIcoImage(bytes, out _, out _, out var entryWidth, out var entryHeight)
            || !TryDecodeDib(payload, entryWidth, entryHeight, out var width, out var height, out var bgra))
        {
            return false;
        }

        bitmap = CreateBgraBitmap(width, height, bgra);
        return true;
    }

    private static bool TryLoadSkia(byte[] bytes, [NotNullWhen(true)] out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            bitmap = new Bitmap(stream);
            return bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or NotSupportedException)
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    private static Bitmap CreateBgraBitmap(int width, int height, byte[] bgra)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using var framebuffer = bitmap.Lock();
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(
                bgra,
                y * width * 4,
                framebuffer.Address + y * framebuffer.RowBytes,
                width * 4);
        }

        return bitmap;
    }

    private static bool TrySelectIcoImage(
        byte[] bytes,
        out int offset,
        out int size,
        out int entryWidth,
        out int entryHeight)
    {
        offset = 0;
        size = 0;
        entryWidth = 0;
        entryHeight = 0;
        if (!IsIcoOrCur(bytes))
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        if (count == 0)
        {
            return false;
        }

        var bestScore = -1;
        var bestOffset = 0;
        var bestSize = 0;
        var bestWidth = 0;
        var bestHeight = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            if (entry + 16 > bytes.Length)
            {
                return false;
            }

            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 6, 2));
            var bytesInRes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 8, 4));
            var imageOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 12, 4));
            if (imageOffset < 0
                || bytesInRes <= 0
                || imageOffset >= bytes.Length)
            {
                continue;
            }

            var payloadSize = Math.Min(bytesInRes, bytes.Length - imageOffset);
            if (payloadSize < 8)
            {
                continue;
            }

            var score = width * height * 10 + bitCount;
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = imageOffset;
                bestSize = payloadSize;
                bestWidth = width;
                bestHeight = height;
            }
        }

        if (bestScore < 0)
        {
            return false;
        }

        offset = bestOffset;
        size = bestSize;
        entryWidth = bestWidth;
        entryHeight = bestHeight;
        return true;
    }

    private static bool TryDecodeDib(
        ReadOnlySpan<byte> dib,
        int entryWidth,
        int entryHeight,
        out int width,
        out int height,
        [NotNullWhen(true)] out byte[]? bgra)
    {
        width = 0;
        height = 0;
        bgra = null;
        if (dib.Length < 40)
        {
            return false;
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize is < 40 or > 124 || headerSize > dib.Length)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (width is <= 0 or > 512 || planes != 1 || compression is not (0 or 3))
        {
            return false;
        }

        if (entryWidth > 0)
        {
            width = entryWidth;
        }

        var xorHeight = Math.Abs(rawHeight);
        if (xorHeight == 0 || xorHeight > 1024)
        {
            return false;
        }

        height = ResolveXorHeight(xorHeight, entryHeight);
        if (width * height > 512 * 512)
        {
            return false;
        }

        var paletteEntries = bitCount switch
        {
            1 => colorsUsed == 0 ? 2 : (int)colorsUsed,
            4 => colorsUsed == 0 ? 16 : (int)colorsUsed,
            8 => colorsUsed == 0 ? 256 : (int)colorsUsed,
            _ => 0
        };
        if (bitCount is not (1 or 4 or 8 or 24 or 32) || paletteEntries is < 0 or > 256)
        {
            return false;
        }

        var paletteOffset = headerSize + (compression == 3 ? 12 : 0);
        var xorOffset = paletteOffset + paletteEntries * 4;
        var xorStride = ((width * bitCount + 31) / 32) * 4;
        var andStride = ((width + 31) / 32) * 4;
        var xorSize = xorStride * height;
        var andOffset = xorOffset + xorSize;
        if (xorOffset < 0 || xorOffset + xorSize > dib.Length)
        {
            return false;
        }

        var andMask = andOffset + andStride * height <= dib.Length
            ? dib.Slice(andOffset, andStride * height)
            : ReadOnlySpan<byte>.Empty;

        bgra = new byte[width * height * 4];
        var topDown = rawHeight < 0;
        var palette = dib.Slice(paletteOffset, paletteEntries * 4);
        for (var y = 0; y < height; y++)
        {
            var srcY = topDown ? y : height - 1 - y;
            var xorRow = dib.Slice(xorOffset + srcY * xorStride, xorStride);
            var destRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                GetXorPixel(xorRow, palette, bitCount, x, out var b, out var g, out var r, out var a);
                var i = destRow + x * 4;
                bgra[i] = b;
                bgra[i + 1] = g;
                bgra[i + 2] = r;
                bgra[i + 3] = a;
            }
        }

        ApplyAndMaskIfNeeded(bgra, width, height, andMask, andStride, topDown);
        return true;
    }

    private static int ResolveXorHeight(int xorHeight, int entryHeight)
    {
        if (entryHeight > 0 && (xorHeight == entryHeight || xorHeight == entryHeight * 2))
        {
            return entryHeight;
        }

        return xorHeight;
    }

    private static void GetXorPixel(
        ReadOnlySpan<byte> row,
        ReadOnlySpan<byte> palette,
        int bitCount,
        int x,
        out byte b,
        out byte g,
        out byte r,
        out byte a)
    {
        a = 255;
        switch (bitCount)
        {
            case 32:
            {
                var i = x * 4;
                b = row[i];
                g = row[i + 1];
                r = row[i + 2];
                a = row[i + 3];
                return;
            }
            case 24:
            {
                var i = x * 3;
                b = row[i];
                g = row[i + 1];
                r = row[i + 2];
                return;
            }
            case 8:
            {
                ReadPaletteColor(palette, row[x], out b, out g, out r);
                return;
            }
            case 4:
            {
                var packed = row[x / 2];
                var index = (x & 1) == 0 ? packed >> 4 : packed & 0x0F;
                ReadPaletteColor(palette, index, out b, out g, out r);
                return;
            }
            default:
            {
                var packed = row[x / 8];
                var index = (packed >> (7 - (x & 7))) & 1;
                ReadPaletteColor(palette, index, out b, out g, out r);
                return;
            }
        }
    }

    private static void ReadPaletteColor(ReadOnlySpan<byte> palette, int index, out byte b, out byte g, out byte r)
    {
        var i = index * 4;
        if ((uint)i + 2 >= (uint)palette.Length)
        {
            b = g = r = 0;
            return;
        }

        b = palette[i];
        g = palette[i + 1];
        r = palette[i + 2];
    }

    private static void ApplyAndMaskIfNeeded(
        byte[] bgra,
        int width,
        int height,
        ReadOnlySpan<byte> andMask,
        int andStride,
        bool topDown)
    {
        if (andMask.IsEmpty)
        {
            return;
        }

        var anyAlpha = false;
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                anyAlpha = true;
                break;
            }
        }

        if (anyAlpha)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var srcY = topDown ? y : height - 1 - y;
            var maskRow = andMask.Slice(srcY * andStride, andStride);
            for (var x = 0; x < width; x++)
            {
                var packed = maskRow[x / 8];
                var transparent = ((packed >> (7 - (x & 7))) & 1) != 0;
                bgra[(y * width + x) * 4 + 3] = transparent ? (byte)0 : (byte)255;
            }
        }
    }

    private static ReadOnlySpan<byte> SkipBomAndWhitespace(ReadOnlySpan<byte> bytes)
    {
        var span = bytes;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        while (span.Length > 0 && span[0] is 0x09 or 0x0A or 0x0D or 0x20)
        {
            span = span[1..];
        }

        return span;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> span)
    {
        if (span.StartsWith("<svg"u8) || span.StartsWith("<SVG"u8))
        {
            return true;
        }

        var head = span.Length > 256 ? span[..256] : span;
        return head.IndexOf("<svg"u8) >= 0 || head.IndexOf("<SVG"u8) >= 0;
    }
}
