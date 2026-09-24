using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VirtualMarina.Core.Design;
using VirtualMarina.WinForms.Resources;

namespace VirtualMarina.WinForms;

/// <summary>Decodes image files into <see cref="ReferenceImage"/>s for the designer (GDI+: PNG, JPEG, BMP, GIF, TIFF).</summary>
public static class ReferenceImageLoader
{
    /// <summary>Largest width or height kept; bigger images are scaled down to stay within common GPU texture limits.</summary>
    public const int MaxDimension = 8192;

    /// <summary>The EXIF tag saying which way up a photo was taken.</summary>
    private const int ExifOrientation = 0x0112;

    private const string PngType = "image/png";
    private const string JpegType = "image/jpeg";

    /// <summary>File filter for an <see cref="OpenFileDialog"/>, with the descriptions in the current language.</summary>
    public static string FileDialogFilter =>
        $"{Strings.FileDialogFilterImages}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|{Strings.FileDialogFilterAll}|*.*";

    /// <summary>Loads and decodes an image file, keeping the file itself so a design can store the picture.</summary>
    /// <param name="path">The image file.</param>
    /// <exception cref="ArgumentException">The file is not a supported image.</exception>
    public static ReferenceImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var data = File.ReadAllBytes(path);
        return FromBytes(data, ContentTypeOf(data) ?? ContentTypeOf(path));
    }

    /// <summary>
    /// Decodes an image from a stream. The bytes are kept, so the picture can be stored in a design; what kind of
    /// image they are is read from the bytes themselves.
    /// </summary>
    /// <param name="stream">The image data.</param>
    /// <exception cref="ArgumentException">The stream is not a supported image.</exception>
    public static ReferenceImage FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();
        return FromBytes(data, ContentTypeOf(data) ?? PngType);
    }

    /// <summary>
    /// Decodes image file bytes, keeping them on the result so it can be written to a marina file. This is also how
    /// a picture that came out of a design is made drawable: a desktop renderer needs pixels, not a PNG.
    /// </summary>
    /// <param name="encodedData">The PNG/JPEG/BMP file contents.</param>
    /// <param name="contentType">MIME type of the data, e.g. "image/png".</param>
    /// <exception cref="ArgumentException">The bytes are not a supported image.</exception>
    public static ReferenceImage FromBytes(byte[] encodedData, string contentType = PngType)
    {
        ArgumentNullException.ThrowIfNull(encodedData);
        if (encodedData.Length == 0) throw new ArgumentException("The image data is empty.", nameof(encodedData));

        using var stream = new MemoryStream(encodedData, writable: false);
        using var source = Image.FromStream(stream);
        return FromImage(source, encodedData, contentType);
    }

    /// <summary>
    /// The same picture with pixels a desktop renderer can draw. A picture loaded from a marina file arrives as an
    /// undecoded PNG; this turns it into one the OpenGL backend can upload, keeping the bytes for the next save.
    /// Returns the image unchanged when it already has pixels.
    /// </summary>
    /// <param name="image">The picture to decode.</param>
    /// <exception cref="ArgumentException">The stored bytes are not a supported image.</exception>
    public static ReferenceImage Decoded(ReferenceImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.Rgba is not null || image.EncodedData is not { Length: > 0 } data
            ? image
            : FromBytes(data, image.ContentType ?? ContentTypeOf(data) ?? PngType);
    }

    /// <summary>
    /// Converts a GDI+ image to RGBA pixels: turned the right way up when it carries an EXIF orientation, and scaled
    /// down when larger than <see cref="MaxDimension"/>.
    /// </summary>
    /// <param name="image">The picture to convert.</param>
    /// <param name="encodedData">The file it was decoded from, when known, so a design can store it.</param>
    /// <param name="contentType">MIME type of <paramref name="encodedData"/>.</param>
    /// <remarks>
    /// A picture that had to be turned or scaled is no longer the file it came from, so the design stores the turned or
    /// scaled copy (as a PNG, or a JPEG when it was one) rather than dropping the picture or storing it the wrong way up.
    /// </remarks>
    public static ReferenceImage FromImage(Image image, byte[]? encodedData = null, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var turn = RotationOf(image);
        using var turnedCopy = turn is RotateFlipType.RotateNoneFlipNone ? null : Turned(image, turn);
        var turned = turnedCopy ?? image;
        var scale = Math.Min(1d, (double)MaxDimension / Math.Max(turned.Width, turned.Height));
        var width = Math.Max(1, (int)Math.Round(turned.Width * scale));
        var height = Math.Max(1, (int)Math.Round(turned.Height * scale));

        using var bitmap = scale < 1d ? Scaled(turned, width, height) : Unscaled(turned);
        var rgba = ReadRgba(bitmap);

        // The stored file describes the original pixels: a turned or scaled copy is stored as itself instead.
        if (scale >= 1d && turnedCopy is null) return new ReferenceImage(width, height, rgba, encodedData, contentType);

        var jpeg = string.Equals(contentType, JpegType, StringComparison.OrdinalIgnoreCase);
        return new ReferenceImage(width, height, rgba, Encode(bitmap, jpeg), jpeg ? JpegType : PngType);
    }

    /// <summary>The MIME type of image bytes, read from the signature at their start; null when it is none GDI+ reads.</summary>
    private static string? ContentTypeOf(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature)) return PngType;
        if (data.StartsWith(JpegSignature)) return JpegType;
        if (data.StartsWith(GifSignature)) return "image/gif";
        if (data.StartsWith(BmpSignature)) return "image/bmp";
        if (data.StartsWith(TiffIntelSignature) || data.StartsWith(TiffMotorolaSignature)) return "image/tiff";
        return null;
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> GifSignature => [0x47, 0x49, 0x46, 0x38];

    private static ReadOnlySpan<byte> BmpSignature => [0x42, 0x4D];

    private static ReadOnlySpan<byte> TiffIntelSignature => [0x49, 0x49, 0x2A, 0x00];

    private static ReadOnlySpan<byte> TiffMotorolaSignature => [0x4D, 0x4D, 0x00, 0x2A];

    // Upper-cased rather than lower-cased: casing an extension is a normalisation, and only the
    // upper-case direction round-trips for every culture (CA1308).
    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".JPG" or ".JPEG" => JpegType,
        ".BMP" => "image/bmp",
        ".GIF" => "image/gif",
        ".TIF" or ".TIFF" => "image/tiff",
        _ => PngType,
    };

    /// <summary>What turns a photo the right way up, from its EXIF orientation (1 to 8); nothing when it has none.</summary>
    private static RotateFlipType RotationOf(Image image)
    {
        if (Array.IndexOf(image.PropertyIdList, ExifOrientation) < 0) return RotateFlipType.RotateNoneFlipNone;
        try
        {
            var value = image.GetPropertyItem(ExifOrientation)?.Value;
            var orientation = value is { Length: >= 2 } bytes ? BitConverter.ToUInt16(bytes, 0) : 1;
            return orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.RotateNoneFlipY,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone,
            };
        }
        catch (ArgumentException)
        {
            return RotateFlipType.RotateNoneFlipNone;   // a damaged tag: take the picture as it is stored
        }
    }

    private static Bitmap Turned(Image image, RotateFlipType turn)
    {
        var copy = new Bitmap(image);
        copy.RotateFlip(turn);
        return copy;
    }

    /// <summary>The picture at its own size in the one pixel format read below; a straight conversion, no resampling.</summary>
    private static Bitmap Unscaled(Image image)
    {
        if (image is Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        }

        var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.DrawImageUnscaled(image, 0, 0);
        return copy;
    }

    private static Bitmap Scaled(Image image, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.DrawImage(image, 0, 0, width, height);
        return bitmap;
    }

    /// <summary>The bitmap's pixels as R, G, B, A bytes, top row first.</summary>
    private static byte[] ReadRgba(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = width * 4;
            var rgba = new byte[rowBytes * height];
            for (var y = 0; y < height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, rgba, y * rowBytes, rowBytes);

            // Format32bppArgb is stored as B, G, R, A: swap blue and red in place.
            for (var i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            return rgba;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] Encode(Bitmap bitmap, bool jpeg)
    {
        using var output = new MemoryStream();
        bitmap.Save(output, jpeg ? ImageFormat.Jpeg : ImageFormat.Png);
        return output.ToArray();
    }
}
