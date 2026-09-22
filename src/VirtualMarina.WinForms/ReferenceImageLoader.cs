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

    /// <summary>Loads and decodes an image file.</summary>
    /// <exception cref="ArgumentException">The file is not a supported image.</exception>
    public static ReferenceImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return FromStream(stream);
    }

    /// <summary>Decodes an image from a stream.</summary>
    /// <exception cref="ArgumentException">The stream is not a supported image.</exception>
    public static ReferenceImage FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var source = Image.FromStream(stream);
        return FromImage(source);
    }

    /// <summary>Converts a GDI+ image to RGBA pixels (scaled down when larger than <see cref="MaxDimension"/>).</summary>
    public static ReferenceImage FromImage(Image image)
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

            return new ReferenceImage(width, height, rgba);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
