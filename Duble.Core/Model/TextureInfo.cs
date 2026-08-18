using System.Text.Json.Serialization;

namespace Duble.Core.Model;

/// <summary>The fingerprint of a single texture.</summary>
public class TextureInfo
{
    /// <summary>Name of the .ytd file.</summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Where to read the file again when building a report or a preview. A plain path for a loose file;
    /// "path\to\archive.rpf|path\inside" for an entry in an archive.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Size and timestamp of the file (for an archive entry, of the archive too). Indexing skips a file whose
    /// stamp has not changed since the last run.
    /// </summary>
    public string? ChangeStamp { get; set; }

    /// <summary>Name of the texture inside the dictionary.</summary>
    public string? Name { get; set; }

    /// <summary>SHA-256 of the whole .ytd file.</summary>
    public string? Sha256 { get; set; }

    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int MipLevels { get; set; }

    /// <summary>BC1_UNORM / BC3_UNORM / BC7_UNORM / …</summary>
    public string? Format { get; set; }

    /// <summary>
    /// A 256-bit perceptual hash (16x16 DCT over a 64x64 greyscale image) in four words. Independent of
    /// resolution and compression: the same graphic at 1024² and 2048² gives the same fingerprint.
    ///
    /// WHY 256 BITS AND NOT 64: calibration over 9437 textures showed that at 64 bits the colour variants of
    /// one garment differ by 0 at the 5th percentile — in greyscale they are indistinguishable — while random
    /// pairs also reach down to 0, so no threshold existed. Clothing textures are atlases with a lot of empty
    /// background, and reducing them to 32x32 threw away too much.
    ///
    /// Null when the pixels could not be decoded.
    /// </summary>
    public ulong[]? PerceptualHash { get; set; }

    /// <summary>Whether the pixels could be decoded. When false, the hash, colour and alpha mean nothing.</summary>
    public bool IsDecoded { get; set; }

    /// <summary>
    /// Colour signature: an 8x8 grid, three bytes of RGB each — 192 bytes, base64. Necessary: in greyscale two
    /// colours of the same dress have an identical perceptual hash.
    /// </summary>
    public string? ColorSignature { get; set; }

    /// <summary>
    /// Standard deviation of brightness. On a flat texture (a low value) the hash bits come from noise and
    /// must not be trusted — then the colour alone decides.
    /// </summary>
    public float Variance { get; set; }

    /// <summary>Share of pixels with alpha below 250. Above zero, BC1 compression (1-bit alpha) is a loss.</summary>
    public float AlphaShare { get; set; }

    [JsonIgnore] public long PixelCount => (long)Width * Height;
}
