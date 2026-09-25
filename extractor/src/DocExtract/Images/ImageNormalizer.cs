using DocExtract.Contract;
using DocExtract.Limits;
using DocExtract.Sniff;
using SkiaSharp;

namespace DocExtract.Images;

public readonly record struct NormalizedImage(byte[] Bytes, string Mime, string Extension, int Width, int Height, bool Downscaled);

public static class ImageNormalizer
{
    public static bool TryGetPixelSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        if (ImageHeader.TryGetSize(data, out width, out height))
        {
            return true;
        }

        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                width = 0;
                height = 0;
                return false;
            }

            width = codec.Info.Width;
            height = codec.Info.Height;
            return true;
        }
        catch (Exception)
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    public static void EnsureWithinPixelCap(int width, int height, long maxPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw ExtractionException.Corrupt();
        }

        var pixels = (long)width * height;
        if (pixels > maxPixels || pixels < 0)
        {
            throw ExtractionException.Limit(ErrorCodes.LimitPixels);
        }
    }

    public static bool TryNormalize(byte[] data, out NormalizedImage image, out string skipReason)
    {
        image = default;
        skipReason = "";
        SKBitmap? bitmap = null;
        try
        {
            bitmap = SKBitmap.Decode(data);
        }
        catch (Exception)
        {
            bitmap = null;
        }

        if (bitmap is null)
        {
            skipReason = "decode failed";
            return false;
        }

        using (bitmap)
        {
            var downscaled = false;
            var working = bitmap;
            SKBitmap? owned = null;
            try
            {
                var longEdge = Math.Max(working.Width, working.Height);
                if (longEdge > LimitBudget.MaxImageLongEdge && longEdge > 0)
                {
                    var scale = LimitBudget.MaxImageLongEdge / (double)longEdge;
                    var w = Math.Max(1, (int)Math.Round(working.Width * scale));
                    var h = Math.Max(1, (int)Math.Round(working.Height * scale));
                    owned = working.Resize(new SKImageInfo(w, h, working.ColorType, working.AlphaType), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    if (owned is null)
                    {
                        skipReason = "resize failed";
                        return false;
                    }

                    working = owned;
                    downscaled = true;
                }

                for (var attempt = 0; attempt < 6; attempt++)
                {
                    if (TryEncode(working, out var bytes, out var mime, out var ext, out var encodedDown))
                    {
                        image = new NormalizedImage(bytes, mime, ext, working.Width, working.Height, downscaled || encodedDown);
                        return true;
                    }

                    var nextW = Math.Max(1, (int)(working.Width * 0.75));
                    var nextH = Math.Max(1, (int)(working.Height * 0.75));
                    if (nextW == working.Width && nextH == working.Height)
                    {
                        break;
                    }

                    var smaller = working.Resize(new SKImageInfo(nextW, nextH, working.ColorType, working.AlphaType), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    if (smaller is null)
                    {
                        break;
                    }

                    if (!ReferenceEquals(working, bitmap))
                    {
                        working.Dispose();
                    }

                    owned = smaller;
                    working = smaller;
                    downscaled = true;
                }
            }
            finally
            {
                if (owned is not null && !ReferenceEquals(owned, bitmap))
                {
                    owned.Dispose();
                }
            }
        }

        skipReason = "encoded image exceeds 3.75 MB";
        return false;
    }

    private static bool TryEncode(SKBitmap bitmap, out byte[] bytes, out string mime, out string extension, out bool reducedQuality)
    {
        bytes = [];
        mime = "image/png";
        extension = ".png";
        reducedQuality = false;
        var hasAlpha = bitmap.AlphaType is not SKAlphaType.Opaque and not SKAlphaType.Unknown;
        using var image = SKImage.FromBitmap(bitmap);
        if (!hasAlpha)
        {
            int[] qualities = [85, 70, 55, 40];
            foreach (var quality in qualities)
            {
                using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                if (jpeg is null)
                {
                    continue;
                }

                var encoded = jpeg.ToArray();
                if (encoded.Length <= LimitBudget.MaxImageBytes)
                {
                    bytes = encoded;
                    mime = "image/jpeg";
                    extension = ".jpg";
                    reducedQuality = quality < 85 || encoded.Length < jpeg.Size;
                    return true;
                }
            }

            reducedQuality = true;
            return false;
        }

        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        if (png is null)
        {
            return false;
        }

        var pngBytes = png.ToArray();
        if (pngBytes.Length <= LimitBudget.MaxImageBytes)
        {
            bytes = pngBytes;
            return true;
        }

        return false;
    }
}
