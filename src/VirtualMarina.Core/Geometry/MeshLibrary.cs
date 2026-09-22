using System.Numerics;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Geometry;

/// <summary>Well-known mesh ids referenced by render objects.</summary>
public static class MeshIds
{
    /// <summary>The water grid (drawn by the water pass).</summary>
    public const int Water = 1;

    /// <summary>1 m white cube centered at the origin (decks, fingers, land).</summary>
    public const int UnitBox = 2;

    /// <summary>Wooden octagonal piling, 1 m diameter, Y 0–1.</summary>
    public const int Piling = 3;

    /// <summary>1 × 1 m flat quad (status pads).</summary>
    public const int BerthPad = 4;

    /// <summary>Spinning gold selection marker.</summary>
    public const int SelectionMarker = 5;

    /// <summary>White sphere, 0.5 m radius (status buoys, boom floats).</summary>
    public const int Buoy = 6;

    /// <summary>White cylinder, 1 m diameter, Y 0–1 (steel piles, bollards).</summary>
    public const int Cylinder = 7;

    /// <summary>First id of the text glyph meshes (see <see cref="GlyphFont"/>).</summary>
    public const int GlyphBase = 300;

    /// <summary>The mainland behind the shore (see <see cref="Domain.Shoreline"/>), drawn beneath the land areas.</summary>
    public const int Shoreline = 9_000;

    /// <summary>What stands on the mainland — trees, crops, a town — kept apart from the ground so it can cast a shadow.</summary>
    public const int ShorelineScenery = 9_001;

    /// <summary>First id of the per-land-area meshes (see <see cref="ForLand"/>).</summary>
    public const int LandBase = 10_000;

    /// <summary>First id of the per-land-area tree meshes (see <see cref="ForLandTrees"/>).</summary>
    public const int LandTreesBase = 20_000;

    private const int BoatBase = 100;

    /// <summary>
    /// Mesh id of a land area's mesh slot (world-space geometry built by <see cref="LandMeshFactory"/>). Loading a layout assigns
    /// slots 0, 1, ... in layout order; land areas added later get the next free slot.
    /// </summary>
    public static int ForLand(int slot) => LandBase + slot;

    /// <summary>
    /// Mesh id of the trees standing on a land area. They are a mesh of their own rather than part of the ground, so
    /// that they can be squashed onto it to cast a shadow.
    /// </summary>
    /// <param name="slot">The same slot the land area's ground uses (see <see cref="ForLand"/>).</param>
    public static int ForLandTrees(int slot) => LandTreesBase + slot;

    /// <summary>Mesh id of a boat model (100 + type). Register a <see cref="MeshData"/> under this id to replace the model.</summary>
    public static int ForBoat(BoatType type) => BoatBase + (int)type;
}

/// <summary>
/// The set of meshes a scene can reference. Renderers upload each mesh once, keyed by id,
/// and pick up meshes added later on the next frame.
/// </summary>
public sealed class MeshLibrary
{
    private readonly Dictionary<int, MeshData> _meshes = new();

    /// <summary>Incremented on every <see cref="Register"/>; renderers re-upload meshes when it changes.</summary>
    public int Version { get; private set; }

    /// <summary>Every registered mesh.</summary>
    public IReadOnlyCollection<MeshData> All => _meshes.Values;

    /// <summary>A library with the water grid, primitives, text glyphs and one procedural model per <see cref="BoatType"/>.</summary>
    /// <param name="waterSize">Edge length of the water grid in meters.</param>
    /// <param name="waterResolution">Grid cells per side.</param>
    /// <param name="waterCenter">Grid center in plan coordinates.</param>
    public static MeshLibrary CreateDefault(float waterSize, int waterResolution, Vector2 waterCenter)
    {
        var library = new MeshLibrary();
        library.Register(MarinaMeshFactory.CreateWaterGrid(MeshIds.Water, waterSize, waterResolution, waterCenter));
        library.Register(MarinaMeshFactory.CreateUnitBox(MeshIds.UnitBox));
        library.Register(MarinaMeshFactory.CreatePiling(MeshIds.Piling));
        library.Register(MarinaMeshFactory.CreateBerthPad(MeshIds.BerthPad));
        library.Register(MarinaMeshFactory.CreateSelectionMarker(MeshIds.SelectionMarker));
        library.Register(MarinaMeshFactory.CreateBuoy(MeshIds.Buoy));
        library.Register(MarinaMeshFactory.CreateCylinder(MeshIds.Cylinder));
        foreach (var glyph in GlyphFont.CreateAll()) library.Register(glyph);
        foreach (var type in BoatTypeCatalog.All)
        {
            library.Register(BoatMeshFactory.Create(type, MeshIds.ForBoat(type)));
        }

        return library;
    }

    /// <summary>Adds or replaces a mesh (e.g. swap a procedural boat for one loaded from GLTF).</summary>
    /// <example><code>marina.Meshes.Register(new MeshData(MeshIds.ForBoat(BoatType.MotorYacht), "Yacht.gltf", vertices, indices));</code></example>
    public void Register(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _meshes[mesh.Id] = mesh;
        Version++;
    }

    /// <summary>Removes a mesh. Returns false when no mesh has this id.</summary>
    public bool Unregister(int id)
    {
        if (!_meshes.Remove(id)) return false;
        Version++;
        return true;
    }

    /// <summary>The mesh with this id.</summary>
    /// <exception cref="KeyNotFoundException">No mesh is registered under the id.</exception>
    public MeshData Get(int id) =>
        _meshes.TryGetValue(id, out var mesh) ? mesh : throw new KeyNotFoundException($"Mesh {id} is not registered.");

    /// <summary>Looks up a mesh by id.</summary>
    public bool TryGet(int id, out MeshData mesh) => _meshes.TryGetValue(id, out mesh!);
}
