# The sea and the shore

What surrounds the marina: the mainland it stands against, and the shipping that passes it. Both are optional, and a
marina without either sits on open water.

Both are part of the layout rather than the style — they are saved in the `.marina.json` file with the piers and
berths, not with the colours — but neither is something a host can click on or select.

| | Type | Set with |
|---|---|---|
| The mainland | `Shoreline` | `marina.SetShoreline(...)`, `marina.Shoreline` |
| Passing traffic | `MarineTraffic` | `marina.SetMarineTraffic(...)`, `marina.MarineTraffic` |

## The mainland behind the shore

Without it a marina looks like an island in an empty sea. A `Shoreline` is an open line of points with the land on
one side of it:

```csharp
// A straight coast running east (+X), with the land to the north (−Z).
marina.SetShoreline(new Shoreline(
    new[] { new Vector2(-500, -40), new Vector2(500, -40) },
    landOnLeft: false));
```

Two points are a straight coast; more bend it into bays and headlands. The line is **open, not a ring**: the stretch
before the first point and the stretch after the last one run on without end, so the land never runs out however far
the camera pulls back.

`LandOnLeft` picks the half. "Left" is meant in **plan coordinates** (plan Y is world Z): true puts the land on the
side reached by turning the line's direction from +X toward +Z, so a line running east has its land to the south.
Plan coordinates look mirrored from above, because +Z points south, toward the bottom of a north-up view — so seen from
above, `LandOnLeft = true` is the **right-hand** side of someone walking the line from the first point to the last, the
side `Pier.Right` points to for a pier running the same way. The name stays as it is because saved designs depend on it.
See [coordinate conventions](20-coordinate-conventions.md). The designer sidesteps all of this: you click the side that
should be land.

The line, carried on without end at both ends, must split the plan cleanly in two, or "the land side" means nothing.
`Validate()` reports each way it can fail:

- fewer than two points, or a point that is not a finite position;
- two neighbouring points in the same place (within a centimeter);
- the drawn line crossing or touching itself;
- the two endless stretches crossing each other;
- either endless stretch running back across the drawn line.

`SetShoreline` throws `MarinaLayoutException` with those messages, and a design holding such a shoreline fails to load
with them. (Earlier versions accepted a line crossing itself and drew land in the wrong places.)

| Property | Default | |
|---|---|---|
| `Points` | | The open line, in plan coordinates |
| `LandOnLeft` | | Which side of it is land |
| `Height` | 1.4 m | How far the ground stands above the water |
| `Kind` | `Grass` | Its surface, from `LandKind` |
| `Scenery` | `Countryside` | What covers it: `None`, `Countryside`, `Fields` or `Town` |
| `ScenerySeed` | 1 | Keeps that covering the same between sessions |

The scenery is scattered in a band along the coast and thins out inland. It is generated from the seed rather than
stored, so a wooded coast costs no more in the file than a bare one.

There is only ever one shoreline: setting another replaces it, and `SetShoreline(null)` or `RemoveShoreline()` takes it away. It is drawn
**beneath** the [land areas](03-layout.md#land-areas) placed by hand, so a quay traced along the shore sits on top of
it and the two read as one piece of ground.

`BuildOutline()` returns the closed shape covering the land side, `Contains(point)` says whether a point is on land,
`DistanceToShore(point)` how far it is from the drawn line, `LandSideNormal(segment)` which way is inland from a stretch
of it, and `EndsAtTheMapEdge()` where the two endless stretches finally reach the edge of the world (at least
`Shoreline.Reach`, 12 km, out). The shape is worked out once and kept, so testing many points is cheap. `Shoreline` is a
record with value equality: two shorelines with the same points and settings are equal. Drawing a shoreline by hand is covered in the
[designer guide](12-designer.md#the-mainland).

## Passing traffic

Vessels crossing the bay beyond the marina. Off until it is asked for:

```csharp
marina.SetMarineTraffic(MarineTraffic.None with
{
    IsEnabled = true,
    Clearance = 300f,        // how near the marina the nearest lane passes
    LaneCount = 3,
    SpeedPercent = 120f,     // a share of what each kind of vessel really does
});
```

| Property | Default | |
|---|---|---|
| `IsEnabled` | false | Off until asked for |
| `Clearance` | 300 m | How near the middle of the marina the nearest lane passes |
| `EdgeClearance` | 700 m | How far off the coast a lane sits where it leaves the map |
| `LaneCount` | 2 | How many lanes, up to `MarineTraffic.LaneLimit` (8) |
| `LaneSpacing` | 160 m | Between one lane and the next, on average |
| `SpeedPercent` | 100 | Scales every vessel's own speed up or down |
| `MaximumVessels` | 16 | The most on the water at once, up to `MarineTraffic.VesselLimit` (60) |
| `SpawnDelaySeconds` | 25 | Roughly how long after one leaves before another appears |
| `Reach` | 8000 m | How far a lane runs when there is no shoreline to take its ends from |
| `Seed` | 1 | Fixes the lanes and the traffic on them |
| `Vessels` | empty | The mix to draw from; empty means `MarineTraffic.DefaultVessels` |

Only the settings are stored. The lanes are worked out from the shoreline and the traffic from `Seed`, so a busy sea
costs no more to save than an empty one.

### Where the lanes go

A lane is a smooth curve through three points: one at each edge of the map, out where the mainland ends and
`EdgeClearance` off the coast, and one in the middle passing the marina at `Clearance`. So a lane sweeps in towards
the marina and back out again, with both ends far enough away that a vessel appears and disappears out of sight.

`Clearance` is the **closest approach to the middle of the marina**, not a margin added to the size of it: 300 m
means 300 m whether the marina is 100 m across or 2 km. Together with `EdgeClearance` it decides how sharply a lane
sweeps in. Lanes beyond the first step out to sea by `LaneSpacing` on average, give or take, so they are not ruled
parallel.

Every vessel in a lane runs the same way, so nothing ever meets head-on. A vessel swings smoothly round the bend in
its lane rather than turning at each point. Half the lanes run one way and half the
other, in an order drawn from `Seed` — a marina always has traffic in both directions, but not in a fixed
arrangement. `Reversed` says which way a lane runs, and its `Points` are stored in that direction.

`Reach` is only consulted when there is no shoreline to take the lane ends from. A reach shorter than `Clearance` is
not an error: the lane is simply run out far enough to be worth crossing.

```csharp
marina.ShowTrafficLanes = true;   // draw the lanes on the water while the clearances are being set
```

`ShowTrafficLanes` is a working aid rather than a style: it draws the lanes, tinted by which way each one runs, and
is not saved with the design. The Designer offers it as **Show the lanes**.

### How fast they go

Every kind of vessel travels at its own speed, from `MarineTraffic.CruisingKnots`:

| | Fishing | Monohull | Catamaran | Day motor | Cat. motor | Ferry | Motor yacht | Jet ski |
|---|---|---|---|---|---|---|---|---|
| knots | 7 | 8 | 9.5 | 14 | 15 | 18 | 22 | 30 |

`SpeedPercent` scales all of them together rather than replacing them, so a fishing boat still plods and a jet ski
still tears past however fast the sea is turned up. Each vessel then differs from its kind by up to a sixth either
way, so no two of a kind keep station while the average is still the real figure.

### How they come and go

The sea starts full: `MaximumVessels` of them (`VesselCount`), scattered along the lanes and already under way, so it
is busy from the first frame. When the marina's layout changes under it, the lanes are planned again and the traffic
carries on: every vessel keeps its lane, kind, speed and progress, and only one whose lane is gone moves to another. Each vessel crosses its lane **once** and is gone off the far edge. About `SpawnDelaySeconds` after
one leaves, another appears at the start of a lane — a lane of its own choosing, of its own kind, at its own speed
and its own offset within the lane.

`MarineTrafficField` holds that: `Advance(seconds)` moves it on and `Vessels` says where everything is — a live view that
the next `Advance` rewrites, so copy it (`Vessels.ToArray()`) to keep it. The visualizer keeps one and advances it as
time passes; `GetTrafficVessels()` returns where everything is now for a host that wants to draw its own marker on top.
A `TrafficVessel`'s `HeadingDegrees` uses the library's heading convention (0° = +Z, south; 90° = +X, east), not a
compass bearing: a bearing `b` is the heading `180 − b`. The vessels are decoration: not berths, not clickable, and they take no part in
selection.

The vessels are a render layer of their own (`RenderLayerKind.Traffic`), so while traffic is on only that layer is sent
to the GPU each frame; the piers, berths and boats stay uploaded as they are. Moving vessels count as animation
(`IsAnimating`), so a view drawing on demand keeps drawing while there is traffic, and can rest again once it is switched off.

## See also

- [Layout](03-layout.md) — land areas, quays and breakwaters inside the marina
- [Appearance](08-appearance.md) — water and lighting
- [Designer](12-designer.md) — drawing a coastline by hand
- [Marina files](13-marina-file-format.md) — how both are stored
