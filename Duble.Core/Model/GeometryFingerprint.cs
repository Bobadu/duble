namespace Duble.Core.Model;

/// <summary>Numbers describing a model's shape — the ones that survive a re-export.</summary>
public class GeometryFingerprint
{
    public int Vertices { get; set; }
    public int Triangles { get; set; }
    public int Meshes { get; set; }

    /// <summary>How many LOD levels the drawable carries (High, Med, Low, VLow).</summary>
    public int LodLevels { get; set; }

    public int Bones { get; set; }

    /// <summary>Size of one vertex in bytes (48 / 64 / 72).</summary>
    public int Stride { get; set; }

    /// <summary>Box dimensions in metres: X, Y, Z.</summary>
    public float[]? BoundingBox { get; set; }

    /// <summary>
    /// Distances from the centre of mass to every vertex, bucketed and normalised by the mean distance.
    /// Independent of vertex order and of scale, so it survives a re-export — which always shuffles the
    /// vertex buffer.
    /// </summary>
    public float[]? ShapeHistogram { get; set; }

    /// <summary>
    /// A hash of the sorted vertex positions rounded to a millimetre. Equal means the same mesh up to a
    /// shuffled vertex buffer. A stronger signal than the histogram, but a brittle one: any rescale or
    /// translation breaks it.
    /// </summary>
    public string? PositionHash { get; set; }

    public const int HistogramBuckets = 64;

    /// <summary>The histogram spans 0 to 2.5 mean distances.</summary>
    public const float HistogramRange = 2.5f;
}
