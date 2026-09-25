using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// One place along a row of berths where a divider can stand: a boundary between two neighbours, or the outer edge of a
/// berth at the end of the row (or beside a stretch of open pier).
/// </summary>
/// <param name="Index">Its place among the row's boundaries, counted from the pier's start.</param>
/// <param name="Along">Distance from the pier's start.</param>
/// <param name="BerthLength">Length of the longer berth beside it, which a divider there runs along.</param>
internal readonly record struct DividerSlot(int Index, float Along, float BerthLength);

/// <summary>
/// Works out where <see cref="DesignTool.PlaceDividers"/> can put dividers along one side of a pier, and which dividers
/// already stand there.
/// </summary>
/// <remarks>
/// The boundaries of a row are the edges of its berths, in order along the pier. Two berths with no more than
/// <see cref="BerthConnections.MaxGap"/> of water between them share one boundary, halfway across that water; berths further
/// apart each have an edge of their own, since a boat could not lie across both of them anyway.
/// </remarks>
internal sealed class DividerPlanner
{
    /// <summary>How far along the pier a divider may stand from a boundary and still be the one on it, in meters.</summary>
    private const float SlotTolerance = 0.6f;

    /// <summary>How far past the ends of a row the pointer may be and still reach the boundary at that end, in meters.</summary>
    private const float Reach = 3f;

    private readonly MarinaVisualizer _marina;

    public DividerPlanner(MarinaVisualizer marina) => _marina = marina;

    /// <summary>The boundaries of the berths on one side of a pier, in order along it; empty when that side has no berths.</summary>
    public IReadOnlyList<DividerSlot> Slots(Pier pier, PierSide side)
    {
        var spans = new List<(float Min, float Max, float Length)>();
        foreach (var berth in _marina.GetBerthsByPier(pier.Id))
        {
            if (PierGeometry.SideOf(pier, berth.Center) != side) continue;
            var along = Vector2.Dot(berth.Center - pier.Start, pier.Direction);
            var extent = MathF.Abs(Vector2.Dot(berth.Right, pier.Direction)) * berth.Width * 0.5f +
                MathF.Abs(Vector2.Dot(berth.Forward, pier.Direction)) * berth.Length * 0.5f;
            if (extent > 0.01f) spans.Add((along - extent, along + extent, berth.Length));
        }

        spans.Sort((a, b) => a.Min.CompareTo(b.Min));
        var slots = new List<DividerSlot>(spans.Count + 1);
        (float Max, float Length)? open = null;
        foreach (var (min, max, length) in spans)
        {
            if (open is not { } previous)
            {
                Add(min, length);
            }
            else if (min - previous.Max <= BerthConnections.MaxGap)
            {
                // Neighbours close enough to share the water between them share the boundary in the middle of it.
                Add((previous.Max + min) * 0.5f, MathF.Max(previous.Length, length));
            }
            else
            {
                Add(previous.Max, previous.Length);
                Add(min, length);
            }

            open = open is { } before && before.Max >= max ? before : (max, length);
        }

        if (open is { } last) Add(last.Max, last.Length);
        return slots;

        void Add(float along, float length)
        {
            // Berths that overlap along the pier can put two edges in one place; they are one boundary.
            if (slots.Count > 0 && along - slots[^1].Along < SlotTolerance) return;
            slots.Add(new DividerSlot(slots.Count, along, length));
        }
    }

    /// <summary>
    /// The boundary nearest <paramref name="along"/>, or null when that side has no berths or the point is well past the
    /// ends of its row.
    /// </summary>
    public static DividerSlot? Nearest(IReadOnlyList<DividerSlot> slots, float along)
    {
        if (slots.Count == 0 || along < slots[0].Along - Reach || along > slots[^1].Along + Reach) return null;
        return slots.MinBy(slot => MathF.Abs(slot.Along - along));
    }

    /// <summary>
    /// The boundaries a click at <paramref name="clicked"/> works on: that one, or with <paramref name="wholeRow"/> every
    /// <paramref name="interval"/>-th boundary of the row counting from it, in both directions.
    /// </summary>
    public static IReadOnlyList<DividerSlot> Pattern(IReadOnlyList<DividerSlot> slots, DividerSlot clicked, bool wholeRow, int interval)
    {
        if (!wholeRow) return [clicked];
        var step = Math.Max(1, interval);
        return slots.Where(slot => ((slot.Index - clicked.Index) % step + step) % step == 0).ToArray();
    }

    /// <summary>The divider standing on a boundary, or null. Any type counts: a boundary holds one divider.</summary>
    public Divider? At(Pier pier, PierSide side, DividerSlot slot)
    {
        var outward = pier.Right * (side == PierSide.Right ? 1f : -1f);
        foreach (var divider in _marina.GetDividersByPier(pier.Id))
        {
            // It runs away from the pier on this side, starting near its edge, level with the boundary.
            if (Vector2.Dot(divider.Direction, outward) < 0.9f) continue;
            var along = Vector2.Dot(divider.Start - pier.Start, pier.Direction);
            var lateral = Vector2.Dot(divider.Start - pier.Center, outward);
            if (MathF.Abs(along - slot.Along) <= SlotTolerance && lateral > 0f && lateral < pier.Width * 0.5f + slot.BerthLength * 0.5f) return divider;
        }

        return null;
    }

    /// <summary>
    /// New dividers of <paramref name="type"/> for the boundaries that have none yet, with ids following those the pier
    /// already has (<c>A-L-D07</c>, ...). Their ids are only reserved against each other and the marina as it stands now.
    /// </summary>
    public List<Divider> Plan(Pier pier, PierSide side, IEnumerable<DividerSlot> slots, DividerType type)
    {
        var prefix = BerthGenerator.DividerPrefix(pier, side);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var number = 0;
        foreach (var existing in _marina.GetDividers())
        {
            used.Add(existing.Id);
            number = Math.Max(number, DesignNaming.ParseNumber(existing.Id, prefix));
        }

        var dividers = new List<Divider>();
        foreach (var slot in slots)
        {
            if (At(pier, side, slot) is not null) continue;
            string id;
            do id = $"{prefix}{++number:00}"; while (!used.Add(id));
            dividers.Add(BerthGenerator.DividerAtPier(pier, id, side, slot.Along, slot.BerthLength, type));
        }

        return dividers;
    }

    /// <summary>The dividers standing on these boundaries.</summary>
    public List<Divider> Existing(Pier pier, PierSide side, IEnumerable<DividerSlot> slots) =>
        slots.Select(slot => At(pier, side, slot)).OfType<Divider>().DistinctBy(divider => divider.Id, StringComparer.OrdinalIgnoreCase).ToList();
}
