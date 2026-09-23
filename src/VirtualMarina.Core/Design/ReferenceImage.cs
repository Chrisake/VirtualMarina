namespace VirtualMarina.Core.Design;

/// <summary>
/// A picture (typically a top-down aerial or satellite screenshot of the real marina) shown under or over the scene while
/// designing, so the layout can be traced on it. North is at the top of the image.
/// </summary>
/// <remarks>
/// The core library has no image codecs, so the host supplies the pixels: either decoded RGBA
/// (<see cref="ReferenceImage(int, int, byte[], byte[], string)"/>, e.g. from a WinForms <c>Bitmap</c>) or the original PNG/JPEG bytes plus the pixel size
/// (<see cref="FromEncoded"/>, which the browser decodes). Every renderer accepts RGBA; the WebGL renderer also accepts encoded data.
/// Instances are immutable: the constructors copy the buffers they are given, so changing the caller's array afterwards
/// changes nothing here. Create a new one to change the picture. The arrays <see cref="Rgba"/> and <see cref="EncodedData"/>
/// hand out are the image's own and must be treated as read-only.
/// </remarks>
public sealed class ReferenceImage
{
    private static int _nextKey;

    /// <summary>Creates an image from decoded pixels.</summary>
    /// <param name="pixelWidth">Width in pixels.</param>
    /// <param name="pixelHeight">Height in pixels.</param>
    /// <param name="rgba">4 bytes per pixel (red, green, blue, alpha), rows from the top of the image down.</param>
    /// <param name="encodedData">
    /// The PNG/JPEG file the pixels were decoded from, when it is at hand. Renderers ignore it — they draw
    /// <paramref name="rgba"/> — but a marina file stores this rather than the pixels, which would be far larger.
    /// </param>
    /// <param name="contentType">MIME type of <paramref name="encodedData"/>, e.g. "image/png".</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    /// <exception cref="ArgumentException">The buffer is not <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> × 4 bytes.</exception>
    public ReferenceImage(int pixelWidth, int pixelHeight, byte[] rgba, byte[]? encodedData = null, string? contentType = null)
        : this(pixelWidth, pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.LongLength != (long)pixelWidth * pixelHeight * 4)
        {
            throw new ArgumentException($"Expected {(long)pixelWidth * pixelHeight * 4} RGBA bytes for {pixelWidth} × {pixelHeight} pixels.", nameof(rgba));
        }

        Rgba = (byte[])rgba.Clone();
        EncodedData = encodedData is { Length: > 0 } ? (byte[])encodedData.Clone() : null;
        ContentType = EncodedData is null ? null : string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
    }

    private ReferenceImage(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Key = Interlocked.Increment(ref _nextKey);
    }

    /// <summary>
    /// Creates an image from PNG, JPEG or WebP file bytes whose pixel size the host already knows. Only renderers that can decode
    /// images themselves (the WebGL renderer) can draw it; desktop hosts should decode to RGBA instead.
    /// </summary>
    /// <param name="encodedData">The file contents.</param>
    /// <param name="pixelWidth">Decoded width in pixels.</param>
    /// <param name="pixelHeight">Decoded height in pixels.</param>
    /// <param name="contentType">MIME type, e.g. "image/png".</param>
    public static ReferenceImage FromEncoded(byte[] encodedData, int pixelWidth, int pixelHeight, string contentType = "image/png")
    {
        ArgumentNullException.ThrowIfNull(encodedData);
        if (encodedData.Length == 0) throw new ArgumentException("The image data is empty.", nameof(encodedData));
        return new ReferenceImage(pixelWidth, pixelHeight)
        {
            EncodedData = (byte[])encodedData.Clone(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType,
        };
    }

    /// <summary>Width in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>Height in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>Decoded pixels (RGBA, top row first), or null for an encoded image. Read-only: the image's own copy.</summary>
    public byte[]? Rgba { get; }

    /// <summary>The original file bytes when they are known, whether or not <see cref="Rgba"/> is also set. Read-only: the image's own copy.</summary>
    public byte[]? EncodedData { get; private init; }

    /// <summary>MIME type of <see cref="EncodedData"/>, or null when there is none.</summary>
    public string? ContentType { get; private init; }

    /// <summary>True when the original file bytes are present, so this image can be stored in a marina file.</summary>
    public bool CanBeSaved => EncodedData is { Length: > 0 };

    /// <summary>Process-unique number identifying this image; renderers upload a texture once per key.</summary>
    public int Key { get; }
}
