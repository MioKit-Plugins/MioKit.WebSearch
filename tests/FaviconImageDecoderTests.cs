using System.Buffers.Binary;
using WebSearch.Services;

namespace MioKit.WebSearch.Tests;

public sealed class FaviconImageDecoderTests
{
    [Fact]
    public void HtmlPlaceholder_IsNonImage()
    {
        var html = """
            <!DOCTYPE html>
            <html lang="en">
            <head><title>Registered &amp; Protected by Markmonitor</title></head>
            <body><h1>This domain is registered</h1></body>
            </html>
            """u8.ToArray();

        Assert.True(FaviconImageDecoder.IsNonImagePayload(html));
        Assert.False(FaviconImageDecoder.TryLoadBitmap(html, out _));
    }

    [Fact]
    public void SvgMarkup_IsNonImage()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"></svg>"""u8.ToArray();

        Assert.True(FaviconImageDecoder.IsNonImagePayload(svg));
    }

    [Fact]
    public void PngSignature_IsImage()
    {
        Assert.False(FaviconImageDecoder.IsNonImagePayload(OneByOnePng));
    }

    [Fact]
    public void HtmlContentType_IsUnusable()
    {
        Assert.True(FaviconService.IsUnusableMediaType("text/html; charset=utf-8"));
        Assert.False(FaviconService.IsUnusableMediaType("image/x-icon"));
        Assert.False(FaviconService.IsUnusableMediaType(null));
    }

    [Fact]
    public void PngWrappedInIco_ExtractsPngPayload()
    {
        var ico = WrapAsIco(OneByOnePng, 1, 1, 32);

        Assert.True(FaviconImageDecoder.IsIcoOrCur(ico));
        Assert.True(FaviconImageDecoder.TryExtractIcoPayload(ico, out var payload));
        Assert.Equal(OneByOnePng, payload);
    }

    [Fact]
    public void Classic32BppIco_DecodesExpectedPixels()
    {
        var ico = Build32BppIco(
            2,
            2,
            [
                0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
                0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
            ]);

        Assert.True(FaviconImageDecoder.TryDecodeIcoDib(ico, out var width, out var height, out var bgra));
        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(
            [
                0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
                0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
            ],
            bgra);
    }

    [Fact]
    public void Timeout_IsHardTransportFailure()
    {
        Assert.True(FaviconService.IsHardTransportFailure(new TaskCanceledException("timeout")));
        Assert.True(FaviconService.IsExpectedTransportFailure(new TaskCanceledException("timeout")));
    }

    private static readonly byte[] OneByOnePng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82
    ];

    private static byte[] WrapAsIco(byte[] image, byte width, byte height, ushort bitCount)
    {
        var ico = new byte[22 + image.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), 1);
        ico[6] = width;
        ico[7] = height;
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(12), bitCount);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(14), image.Length);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(18), 22);
        image.CopyTo(ico, 22);
        return ico;
    }

    private static byte[] Build32BppIco(int width, int height, byte[] topDownBgra)
    {
        const int headerSize = 40;
        var xorStride = width * 4;
        var andStride = ((width + 31) / 32) * 4;
        var dib = new byte[headerSize + xorStride * height + andStride * height];
        BinaryPrimitives.WriteInt32LittleEndian(dib, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);

        for (var y = 0; y < height; y++)
        {
            var src = (height - 1 - y) * xorStride;
            Buffer.BlockCopy(topDownBgra, src, dib, headerSize + y * xorStride, xorStride);
        }

        return WrapAsIco(dib, (byte)width, (byte)height, 32);
    }
}
