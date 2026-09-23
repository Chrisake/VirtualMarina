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

    /// <summary>File filter for an <see cref="OpenFileDialog"/>, with the descriptions in the current language.</summary>
    public static string FileDialogFilter =>
        $"{Strings.FileDialogFilterImages}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|{Strings.FileDialogFilterAll}|*.*";

    /// <summary>Loads and decodes an image file, keeping the file itself so a design can store the picture.</summary>
    /// <exception cref="ArgumentException">The file is not a supported image.</exception>
    public static ReferenceImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromBytes(File.ReadAllBytes(path), ContentTypeOf(path));
    }

    /// <summary>Decodes an image from a stream. The bytes are kept, so the picture can be stored in a design.</summary>
    /// <exception cref="ArgumentException">The stream is not a supported image.</exception>
    public static ReferenceImage FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromBytes(buffer.ToArray(), "image/png");
    }

    /// <summary>
    /// Decodes image file bytes, keeping them on the result so it can be written to a marina file. This is also how
    /// a picture that came out of a design is made drawable: a desktop renderer needs pixels, not a PNG.
    /// </summary>
    /// <param name="encodedData">The PNG/JPEG/BMP file contents.</param>
    /// <param name="contentType">MIME type of the data, e.g. "image/png".</param>
    /// <exception cref="ArgumentException">The bytes are not a supported image.</exception>
    public static ReferenceImage FromBytes(byte[] encodedData, string contentType = "image/png")
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
            : FromBytes(data, image.ContentType ?? "image/png");
    }

    // Upper-cased rather than lower-cased: casing an extension is a normalisation, and only the
    // upper-case direction round-trips for every culture (CA1308).
    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".JPG" or ".JPEG" => "image/jpeg",
        ".BMP" => "image/bmp",
        ".GIF" => "image/gif",
        ".TIF" or ".TIFF" => "image/tiff",
        _ => "image/png",
    };

    /// <summary>Converts a GDI+ image to RGBA pixels (scaled down when larger than <see cref="MaxDimension"/>).</summary>
    /// <param name="image">The picture to convert.</param>
    /// <param name="encodedData">The file it was decoded from, when known, so a design can store it.</param>
    /// <param name="contentType">MIME type of <paramref name="encodedData"/>.</param>
    public static ReferenceImage FromImage(Image image, byte[]? encodedData = null, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var scale = Math.Min(1d, (double)MaxDimension / Math.Max(image.Width, image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, width, height);
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rgba = new byte[width * height * 4];
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                // Format32bppArgb is stored as B, G, R, A.
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                var offset = y * width * 4;
                for (var x = 0; x < row.Length; x += 4)
                {
                    rgba[offset + x] = row[x + 2];
                    rgba[offset + x + 1] = row[x + 1];
                    rgba[offset + x + 2] = row[x];
                    rgba[offset + x + 3] = row[x + 3];
                }
            }

            // The stored file describes the original pixels; a scaled-down copy is no longer that file.
            var source = scale < 1d ? null : encodedData;
            return new ReferenceImage(width, height, rgba, source, contentType);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
