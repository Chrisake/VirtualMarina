using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Picking;

/// <summary>
/// Everything a pick ray can hit — berth pads and boats — prepared once per state of the scene, with a uniform grid
/// over the plan so a ray only tests what stands in the cells it crosses.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScenePicker.Pick"/> works everything out on every call: which berths are shown, where each boat stands
/// and how to turn a ray into its model's frame, and then tries every pad and every boat. That is fine once per click
/// but not on every pointer move over a marina of thousands of berths. This keeps all of it — boats with their
/// inverted placements and world bounds, pads with their heights — until the scene changes, and answers a ray from the
/// grid cells under it, without allocating.
/// </para>
/// <para>
/// It gives the same answer as <see cref="ScenePicker.Pick"/>: candidates are tried in the same order, so where two
/// hits are equally near the same one wins. Not thread-safe (it reuses its scratch lists), like the visualizer.
/// </para>
/// </remarks>
internal sealed class PickSet
{
    /// <summary>Most cells along either side of the grid, however large the marina.</summary>
    private const int MaxCellsPerSide = 256;

    private readonly Pad[] _pads;
    private readonly BoatEntry[] _boats;
    private readonly Vector2 _gridOrigin;
    private readonly float _cellSize;
    private readonly int _columns;
    private readonly int _rows;
    private readonly int[] _cellStart;
    private readonly int[] _cellItems;
    private readonly BoundingBox _worldBounds;
    private readonly int[] _seen;
    private readonly List<int> _padCandidates = [];
    private readonly List<int> _boatCandidates = [];
    private int _stamp;

    /// <summary>Prepares the pick set.</summary>
    /// <param name="berths">Berths whose pads can be hit (visible and not filtered out), in layout order.</param>
    /// <param name="boats">Boats that can be hit, in the order the scene draws them.</param>
    /// <param name="meshes">Boat meshes.</param>
    /// <param name="groundHeight">Land height of a land berth (null for water berths).</param>
    /// <param name="version">What the pick set was built for (see <see cref="Version"/>).</param>
    public PickSet(IEnumerable<Berth> berths, IEnumerable<BoatInstance> boats, MeshLibrary meshes, Func<Berth, float?> groundHeight, long version)
    {
        ArgumentNullException.ThrowIfNull(berths);
        ArgumentNullException.ThrowIfNull(boats);
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(groundHeight);
        Version = version;

        var planMin = new Vector2(float.MaxValue);
        var planMax = new Vector2(float.MinValue);
        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);

        var pads = new List<Pad>();
        foreach (var berth in berths)
        {
            var height = BerthPlacement.PadHeightFor(groundHeight(berth));
            var (min, max) = berth.Bounds.GetAxisAlignedBounds();
            pads.Add(new Pad(berth, height, min, max));
            planMin = Vector2.Min(planMin, min);
            planMax = Vector2.Max(planMax, max);
            worldMin = Vector3.Min(worldMin, Vector3.Min(MarinaMath.ToWorld(min, height), MarinaMath.ToWorld(max, height)));
            worldMax = Vector3.Max(worldMax, Vector3.Max(MarinaMath.ToWorld(min, height), MarinaMath.ToWorld(max, height)));
        }

        var entries = new List<BoatEntry>();
        foreach (var boat in boats)
        {
            if (!meshes.TryGet(MeshIds.ForBoat(boat.Boat.Type), out var mesh) || !Matrix4x4.Invert(boat.World, out var toLocal)) continue;

            var (boxMin, boxMax) = TransformBounds(mesh.Bounds, boat.World);
            var min = Vector2.Min(MarinaMath.ToPlan(boxMin), MarinaMath.ToPlan(boxMax));
            var max = Vector2.Max(MarinaMath.ToPlan(boxMin), MarinaMath.ToPlan(boxMax));
            entries.Add(new BoatEntry(boat, mesh, toLocal, min, max));
            planMin = Vector2.Min(planMin, min);
            planMax = Vector2.Max(planMax, max);
            worldMin = Vector3.Min(worldMin, boxMin);
            worldMax = Vector3.Max(worldMax, boxMax);
        }

        _pads = pads.ToArray();
        _boats = entries.ToArray();
        var count = _pads.Length + _boats.Length;
        _seen = new int[count];
        // A little slack, so a ray meeting a pad exactly at its height is not lost to rounding at the box's face.
        _worldBounds = new BoundingBox(worldMin - new Vector3(0.05f), worldMax + new Vector3(0.05f));

        if (count == 0)
        {
            _cellStart = [0];
            _cellItems = [];
            _cellSize = 1f;
            _columns = _rows = 1;
            return;
        }

        // Roughly one element per cell, a few meters at the least, and never more cells than the cap.
        var span = Vector2.Max(planMax - planMin, new Vector2(1f));
        var cell = MathF.Max(4f, MathF.Sqrt(span.X * span.Y / count));
        cell = MathF.Max(cell, MathF.Max(span.X, span.Y) / MaxCellsPerSide);
        _cellSize = cell;
        _gridOrigin = planMin;
        _columns = Math.Clamp((int)MathF.Ceiling(span.X / cell), 1, MaxCellsPerSide);
        _rows = Math.Clamp((int)MathF.Ceiling(span.Y / cell), 1, MaxCellsPerSide);

        // Compressed rows: count what falls in each cell, then fill.
        _cellStart = new int[(_columns * _rows) + 1];
        for (var item = 0; item < count; item++)
        {
            ForEachCell(item, cellIndex => _cellStart[cellIndex + 1]++);
        }

        for (var i = 1; i < _cellStart.Length; i++) _cellStart[i] += _cellStart[i - 1];
        _cellItems = new int[_cellStart[^1]];
        var fill = new int[_columns * _rows];
        for (var item = 0; item < count; item++)
        {
            ForEachCell(item, cellIndex => _cellItems[_cellStart[cellIndex] + fill[cellIndex]++] = item);
        }
    }

    /// <summary>What the pick set was built for; the visualizer builds a new one when this no longer matches.</summary>
    public long Version { get; }

    /// <summary>The nearest pad or boat along <paramref name="ray"/>, as <see cref="ScenePicker.Pick"/> finds it.</summary>
    public BerthHit? Pick(Ray ray)
    {
        // A pointer far outside the view, or not a number at all, gives a ray that is not one.
        if (_seen.Length == 0 || !IsFinite(ray.Origin) || !IsFinite(ray.Direction) || !ClipToBounds(ray, out var enter, out var exit)) return null;

        CollectCandidates(MarinaMath.ToPlan(ray.GetPoint(enter)), MarinaMath.ToPlan(ray.GetPoint(exit)));

        // Berth areas (the colored pads) first, then boats, which may rise far above the pads and overhang neighbouring berths.
        return PickBoat(ray, PickPad(ray));
    }

    /// <summary>The nearest berth area (a colored pad), on the water or on land, among the candidates.</summary>
    private BerthHit? PickPad(Ray ray)
    {
        BerthHit? best = null;
        foreach (var index in _padCandidates)
        {
            var pad = _pads[index];
            if (!ray.IntersectHorizontalPlane(pad.Height, out var padDistance) || (best is not null && padDistance >= best.Value.Distance)) continue;

            var point = ray.GetPoint(padDistance);
            if (pad.Berth.Bounds.Contains(MarinaMath.ToPlan(point)))
            {
                best = new BerthHit(pad.Berth.Id, padDistance, point, HitBoat: false);
            }
        }

        return best;
    }

    /// <summary>The nearest boat among the candidates when it is nearer than <paramref name="best"/>; otherwise <paramref name="best"/>.</summary>
    private BerthHit? PickBoat(Ray ray, BerthHit? best)
    {
        foreach (var index in _boatCandidates)
        {
            var entry = _boats[index];
            best = ScenePicker.HitBoat(ray, entry.Boat, entry.Mesh, entry.ToLocal, best) ?? best;
        }

        return best;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Where the ray is inside the box around everything, as distances along it.</summary>
    private bool ClipToBounds(Ray ray, out float enter, out float exit)
    {
        enter = 0f;
        exit = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = ray.Origin[axis];
            var direction = ray.Direction[axis];
            var min = _worldBounds.Min[axis];
            var max = _worldBounds.Max[axis];
            if (MathF.Abs(direction) < 1e-8f)
            {
                if (origin < min || origin > max) return false;
                continue;
            }

            var t1 = (min - origin) / direction;
            var t2 = (max - origin) / direction;
            if (t1 > t2) (t1, t2) = (t2, t1);
            enter = MathF.Max(enter, t1);
            exit = MathF.Min(exit, t2);
            if (enter > exit) return false;
        }

        return true;
    }

    /// <summary>
    /// Walks the grid cells the plan segment from <paramref name="from"/> to <paramref name="to"/> crosses and gathers
    /// what stands in them, each once, in the order the unindexed picker would try them.
    /// </summary>
    private void CollectCandidates(Vector2 from, Vector2 to)
    {
        _padCandidates.Clear();
        _boatCandidates.Clear();
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }

        // Grid coordinates, where a cell is one unit, cut to the grid: the walk below has to start inside it, or it
        // steps out of the first cell at the wrong moment and misses the ones after.
        var a = (from - _gridOrigin) / _cellSize;
        var b = (to - _gridOrigin) / _cellSize;
        if (!ClipToGrid(ref a, ref b)) return;

        var x = Math.Clamp((int)MathF.Floor(a.X), 0, _columns - 1);
        var y = Math.Clamp((int)MathF.Floor(a.Y), 0, _rows - 1);
        var endX = Math.Clamp((int)MathF.Floor(b.X), 0, _columns - 1);
        var endY = Math.Clamp((int)MathF.Floor(b.Y), 0, _rows - 1);

        // Amanatides and Woo: step into whichever neighbouring cell the segment reaches first.
        var delta = b - a;
        var stepX = Math.Sign(delta.X);
        var stepY = Math.Sign(delta.Y);
        var tDeltaX = CellCrossing(delta.X, stepX);
        var tDeltaY = CellCrossing(delta.Y, stepY);
        var tMaxX = FirstCrossing(a.X, stepX, tDeltaX);
        var tMaxY = FirstCrossing(a.Y, stepY, tDeltaY);

        for (var steps = _columns + _rows + 2; steps > 0; steps--)
        {
            GatherCell((y * _columns) + x);
            if (x == endX && y == endY) break;
            if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                y += stepY;
                tMaxY += tDeltaY;
            }

            if (x < 0 || x >= _columns || y < 0 || y >= _rows) break;
        }

        _padCandidates.Sort();
        _boatCandidates.Sort();
    }

    /// <summary>How far along the segment it takes to cross one whole cell on an axis; never, when it does not move on that axis.</summary>
    private static float CellCrossing(float delta, int step) => step != 0 ? MathF.Abs(1f / delta) : float.MaxValue;

    /// <summary>How far along the segment it first crosses a cell boundary on an axis, starting from <paramref name="start"/>.</summary>
    private static float FirstCrossing(float start, int step, float cellCrossing) =>
        step > 0 ? (MathF.Floor(start) + 1f - start) * cellCrossing
        : step < 0 ? (start - MathF.Floor(start)) * cellCrossing
        : float.MaxValue;

    /// <summary>Cuts the segment from <paramref name="a"/> to <paramref name="b"/> to the grid (Liang and Barsky). False when it misses it.</summary>
    private bool ClipToGrid(ref Vector2 a, ref Vector2 b)
    {
        var enter = 0f;
        var exit = 1f;
        var delta = b - a;
        if (!ClipAxis(-delta.X, a.X, ref enter, ref exit) || !ClipAxis(delta.X, _columns - a.X, ref enter, ref exit) ||
            !ClipAxis(-delta.Y, a.Y, ref enter, ref exit) || !ClipAxis(delta.Y, _rows - a.Y, ref enter, ref exit))
        {
            return false;
        }

        var start = a + delta * enter;
        var end = a + delta * exit;

        // What rounding leaves a hair outside is brought back in.
        var limit = new Vector2(_columns, _rows) - new Vector2(1e-4f);
        a = Vector2.Clamp(start, Vector2.Zero, limit);
        b = Vector2.Clamp(end, Vector2.Zero, limit);
        return true;

        static bool ClipAxis(float p, float q, ref float enter, ref float exit)
        {
            if (MathF.Abs(p) < 1e-12f) return q >= -1e-3f;
            var t = q / p;
            if (p < 0f) enter = MathF.Max(enter, t);
            else exit = MathF.Min(exit, t);
            return enter <= exit + 1e-6f;
        }
    }

    private void GatherCell(int cellIndex)
    {
        for (var i = _cellStart[cellIndex]; i < _cellStart[cellIndex + 1]; i++)
        {
            var item = _cellItems[i];
            if (_seen[item] == _stamp) continue;
            _seen[item] = _stamp;
            if (item < _pads.Length) _padCandidates.Add(item);
            else _boatCandidates.Add(item - _pads.Length);
        }
    }

    /// <summary>Calls <paramref name="action"/> with every cell an element's plan box overlaps.</summary>
    private void ForEachCell(int item, Action<int> action)
    {
        var (min, max) = item < _pads.Length ? (_pads[item].Min, _pads[item].Max) : (_boats[item - _pads.Length].Min, _boats[item - _pads.Length].Max);
        var x0 = Math.Clamp((int)MathF.Floor((min.X - _gridOrigin.X) / _cellSize), 0, _columns - 1);
        var x1 = Math.Clamp((int)MathF.Floor((max.X - _gridOrigin.X) / _cellSize), 0, _columns - 1);
        var y0 = Math.Clamp((int)MathF.Floor((min.Y - _gridOrigin.Y) / _cellSize), 0, _rows - 1);
        var y1 = Math.Clamp((int)MathF.Floor((max.Y - _gridOrigin.Y) / _cellSize), 0, _rows - 1);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++) action((y * _columns) + x);
        }
    }

    /// <summary>The world-space box around a model-space box placed by <paramref name="world"/>.</summary>
    private static (Vector3 Min, Vector3 Max) TransformBounds(BoundingBox box, Matrix4x4 world)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? box.Min.X : box.Max.X,
                (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                (corner & 4) == 0 ? box.Min.Z : box.Max.Z);
            var point = Vector3.Transform(local, world);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return (min, max);
    }

    /// <summary>A berth's pad: the berth, the height it lies at and its plan box.</summary>
    private readonly record struct Pad(Berth Berth, float Height, Vector2 Min, Vector2 Max);

    /// <summary>A boat with its mesh, the transform into the mesh's frame and its plan box.</summary>
    private readonly record struct BoatEntry(BoatInstance Boat, MeshData Mesh, Matrix4x4 ToLocal, Vector2 Min, Vector2 Max);
}
