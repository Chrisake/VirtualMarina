# Appearance

## Berth labels on the water

```csharp
marina.BerthLabelMode = BerthLabelMode.NonOccupied;
```

| `BerthLabelMode` | Labeled berths |
|---|---|
| `None` (default) | None |
| `OnlyFree` | Free |
| `NonOccupied` | Free, Reserved, Temporarily Free |
| `All` | Every berth |

- **Text:** each labeled berth shows its `DisplayName` (its `Label`, or else its `Id`).
- **Placement:** flat on the water just past the berth's open (seaward) end. A berth ashore gets its label the same
  way, floating 0.45 m above the ground it stands on so it is still readable from a low camera instead of
  disappearing into the surface.
- **Size:** at most 65% of the berth width and between 0.3 m and 1 m tall, so labels of neighboring berths stay apart.
- **Orientation:** the top of the text points away from the pier, so it reads upright to someone on the pier looking at the berth.
- **Colors:** white; yellow while the berth is hovered or selected; gray for disabled berths.
- **Waves:** the text sits just above the highest point the waves can reach (the sum of wave amplitudes × `Water.WaveAmplitude`), so waves never cover it from any camera position.
- **Which berths:** hidden and filtered-out berths get no label.
- **Characters:** labels use a built-in stroke font (no textures) covering `A–Z`, `0–9` and `- _ + . , : / ( ) # ?`. Lowercase is drawn as uppercase and other characters as `?`.

### How the letters look

Two settings, chosen separately, so any typeface can be had in any weight:

```csharp
marina.Style.Labels.Typeface = LabelTypeface.Serif;    // the shape of the letters
marina.Style.Labels.FontFamily = LabelFont.Bold;       // their weight and width
```

| `Typeface` | |
|---|---|
| `Sans` | Plain strokes with open ends. The default, and the one to read at a glance |
| `Serif` | Finer strokes finished with small feet, in the manner of a book face |
| `Slab` | Heavier strokes with square feet, which hold up at a distance and on a busy background |

| `FontFamily` | |
|---|---|
| `Regular`, `Bold` | Even or heavy strokes at normal width |
| `Condensed`, `Wide` | Narrower for long names, wider for big berths |

The letters are **drawn as strokes**, not set in an installed font: they are meshes lying flat on the water, so
OpenGL and WebGL render exactly the same thing, the library carries no font files, and the text stays crisp at any
zoom. That is also why real font names such as Arial or Times are not among the choices, and why there is no
monospaced one — every glyph already sits on the same grid and advances by the same step, so it would be the same
letters as `Sans`.

`BerthLabelModeExtensions.Includes(mode, status)` and `GetDisplayName(mode)` help build a mode picker.

## Status colors and overlays

See [Berth status, boats and flags → Colors](04-status-and-flags.md#colors): `SetStatusColor`, `SetDisabledColor`, `SetOverlayOpacity`, `ResetStatusColors`.

`ColorRgba` is a linear RGBA color (0–1):

```csharp
var c1 = ColorRgba.FromHex("#3A73E8");          // #RGB, #RRGGBB or #RRGGBBAA
var c2 = ColorRgba.FromBytes(58, 115, 232);
var c3 = new ColorRgba(0.2f, 0.45f, 0.9f).WithAlpha(0.5f);
string hex = c1.ToHex();
```

## Lighting

`marina.Lighting` (`LightingSettings`), applied on the next frame:

| Property | Default | Meaning |
|---|---|---|
| `SunDirection` | (0.45, 0.8, 0.35), normalized | Unit vector toward the sun |
| `SetSunAngles(azimuth, elevation)` | | Sets `SunDirection` from compass angles |
| `SunColor` | (1, 0.95, 0.86) | Sunlight color |
| `AmbientColor` | (0.36, 0.40, 0.48) | Ambient light |
| `SpecularStrength`, `Shininess` | 0.35, 32 | Highlights on objects |
| `SkyColor` | (0.56, 0.72, 0.88) | Reflected by the water at grazing angles |
| `FogColor` | (0.76, 0.85, 0.92) | Horizon color and background |
| `FogDensity` | 0.0022 | Squared-exponential fog; 0 disables it |

```csharp
marina.Lighting.SetSunAngles(azimuthDegrees: 250, elevationDegrees: 12);   // evening light
marina.Lighting.SunColor = new Vector3(1.0f, 0.75f, 0.55f);
```

## Water

`marina.Water` (`WaterSettings`):

| Property | Default | Notes |
|---|---|---|
| `Size` | 4200 m | Edge length of the **detailed** water. Can be changed at any time |
| `GridResolution` | 160 | Cells per side. Construction only |
| `DeepColor`, `ShallowColor` | teal tones | Water body colors |
| `WaveAmplitude` | 0.08 m | Base wave height |
| `WaveFrequency` | 1 | Larger means shorter waves |
| `WaveSpeed` | 1 | 0 freezes the water and floating objects |

```csharp
var marina = new MarinaVisualizer(new MarinaVisualizerOptions
{
    Water = new WaterSettings { Size = 2400, GridResolution = 240, WaveAmplitude = 0.05f },
});
marina.Water.WaveSpeed = 0;   // calm, static water
```

The water grid is re-centered on the layout by `InitializeLayout`.

**The sea does not end.** `Size` is only the part drawn in detail, and it can be changed while the marina is on
screen — the visualizer notices and rebuilds the grid on the next frame. Around it the grid carries a flat skirt of eight
triangles reaching `MarinaMeshFactory.SeaReach` (30 km), and the shader fades the waves, the sky reflection and the
sun glints out over the outer third of the detailed grid. So the water runs to the horizon and the fog takes it into
the sky, while everything that costs anything to draw stays near the marina. Widen `Size` to push the detailed water
further out; the skirt follows on its own.

## Shadows

The boats and the piers cast shadows on the ground. On by default:

```csharp
marina.Style.Shadows.IsEnabled = false;   // off
marina.Style.Shadows.Strength = 0.35f;    // darker (0-1, default 0.25)
```

Everything standing on the marina casts one: the boats, the piers and their kerbs, piles, bollards and service
pedestals, the cradles ashore, the trees on the land areas, and the trees and town on the mainland behind the shore.

Each shadow is the object itself squashed onto the ground along the sun's rays, so it follows
`Lighting.SunDirection` — move the sun and the shadows move with it. It lands on the ground the object stands over:
a boat afloat shades the water, a boat ashore shades the yard it is cradled in, a tree shades its own lawn.

That costs one extra instance per object that casts, which is why the toggle is there: on a marina of several hundred
berths it roughly doubles the scene. It needs no depth pass and no shadow map, so it behaves the same in the OpenGL
and WebGL views.

What it does not do, by construction:

- **One plane per object.** A boat's shadow falls on the water, not up the side of the pier beside it.
- **No self-shadowing.** A cabin does not shade its own deck.
- **The ground casts none**, being what the shadows land on. The trees and the hinterland are separate meshes for
  exactly this reason, so they do.
- **Overlap darkens.** A flattened object covers itself, so a shadow is darker than `Strength` alone — much darker
  for something like a tree crown, which is several rounded blobs on top of one another — and can look blotchy past
  about 0.4.
- **Nothing below about 4° of elevation**, where a shadow would stretch to the horizon.

## Passing traffic

Vessels crossing the bay beyond the marina, so the sea is not empty. It is off until it is asked for:

```csharp
marina.SetMarineTraffic(MarineTraffic.None with
{
    IsEnabled = true,
    Clearance = 300f,          // meters: how near the marina the nearest lane passes
    EdgeClearance = 700f,      // meters: how far off the coast it leaves the map
    LaneCount = 3,
    LaneSpacing = 160f,
    SpeedPercent = 120f,       // a share of what each kind of vessel really does
    MaximumVessels = 16,
    SpawnDelaySeconds = 25f,
});
```

| Property | Default | Notes |
|---|---|---|
| `IsEnabled` | false | Off until asked for |
| `Clearance` | 300 m | How near the middle of the marina the nearest lane passes |
| `EdgeClearance` | 700 m | How far off the coast a lane sits where it leaves the map |
| `LaneCount` | 2 | How many lanes, up to `MarineTraffic.LaneLimit` (8) |
| `LaneSpacing` | 160 m | Between one lane and the next, on average |
| `SpeedPercent` | 100 | Scales every vessel's own speed up or down |
| `MaximumVessels` | 16 | The most on the water at once, up to `MarineTraffic.VesselLimit` (60) |
| `SpawnDelaySeconds` | 25 | Roughly how long after one leaves before another appears |
| `Reach` | 8000 m | How far the lanes run when there is no shoreline to take their ends from |
| `Seed` | 1 | Fixes the lanes and the traffic on them |
| `Vessels` | empty | The mix to draw from; empty means `MarineTraffic.DefaultVessels` |

### Where the lanes go

A lane is drawn through **three points** and curved smoothly between them:

- one at each edge of the map, out where the [mainland](07-land-and-shoreline.md) ends, sitting `EdgeClearance` off
  the coast;
- one in the middle, passing the marina at `Clearance`.

So a lane sweeps in towards the marina and back out again, and both its ends are far enough away that a vessel
appears and disappears out of sight. `Clearance` is the closest approach to the middle of the marina, not a margin
added to the size of it, so 300 m means 300 m whether the marina is 100 m across or 2 km. The two settings together
say how sharply a lane sweeps in.

Three points is the whole of it, and that is the point. Pushing the coastline itself out to sea -- which is what this
used to do -- folds over on itself wherever the coast turns in, since a bay narrower than the push becomes a loop.
Lanes came out with a right-angle kink in them and a six-kilometre jump between the first two points.

Lanes beyond the first step out to sea by `LaneSpacing` on average, give or take, so they are not ruled parallel.
Every vessel in a lane runs the same way and nothing meets head-on; one or two lanes run opposite ways as a traffic
separation scheme does, and beyond that the directions are drawn at random. `TrafficLanes` gives the planned lanes
back, nearest first, each with its `Points`, `Length`, `Reversed`, `DistanceTo(point)` and `At(along)`.

```csharp
marina.ShowTrafficLanes = true;   // draws the lanes on the water while the clearances are being set
```

`ShowTrafficLanes` is a working aid, not a style: it draws the lanes, tinted by which way each runs. It is not saved
with the design. The Designer offers it as **Show the lanes** in the traffic card of the Look panel.

### How fast they go

Every kind of vessel travels at its own speed, from `MarineTraffic.CruisingKnots`: a fishing boat plods at 7 knots
and a jet ski tears past at 30. One figure for the whole sea had them all keeping station, which reads as wrong
immediately.

| | Fishing | Monohull | Catamaran | Day motor | Cat. motor | Ferry | Motor yacht | Jet ski |
|---|---|---|---|---|---|---|---|---|
| knots | 7 | 8 | 9.5 | 14 | 15 | 18 | 22 | 30 |

`SpeedPercent` scales all of them together rather than replacing them, so the mix keeps its character however fast
the sea is turned up. Each vessel then differs from its kind by up to a sixth either way, so no two keep station
while the average across the sea is still the real figure.

### How they come and go

A random number of vessels, up to `MaximumVessels`, is out there to begin with, already under way so the marina does
not open with a row of them on the horizon. Each crosses its lane **once** and is gone off the far edge. About
`SpawnDelaySeconds` after one leaves, another appears at the start of a lane -- a lane of its own choosing, of its own
kind, at its own speed and its own offset within the lane. So the sea stays near the number it started with rather
than filling to the cap, and it never repeats itself.

`MarineTrafficField` holds that: `Advance(seconds)` moves it on, `Vessels` says where everything is. The visualizer
keeps one and advances it as time passes; `GetTrafficVessels()` returns the same snapshot for a host that wants to
draw its own marker. They are decoration: not berths, not clickable, and they take no part in selection.

Because the vessels move, the scene's instance data is rebuilt on every frame while traffic is on. Switching it off
hands the renderer the still scene again, which it can leave uploaded.

## Replacing boat models

Boats are procedural low-poly placeholders (`BoatMeshFactory`). To use your own models:
1. Load them into a `MeshData` in the same model space:
   - bow toward **+Z** and waterline at **Y = 0**, centered on X/Z;
   - authored at the nominal dimensions from `BoatTypeCatalog.GetNominalDimensions`.
2. Register each one under the boat type's id.

```csharp
// Interleaved vertices: position xyz, normal xyz, color rgb (MeshData.VertexStride = 9); uint triangle indices.
marina.Meshes.Register(new MeshData(MeshIds.ForBoat(BoatType.MotorYacht), "Yacht.gltf", vertices, indices));
```

Both renderers re-upload changed meshes automatically (`MeshLibrary.Version`). `MeshBuilder` helps build meshes from boxes, lofts, cylinders, spheres and plates. The procedural models use `MeshBuilder` too.

| `MeshIds` | Mesh |
|---|---|
| `Water` | Water grid |
| `UnitBox` | 1 m cube (decks, fingers, land) |
| `Piling` | Wooden piling |
| `BerthPad` | Status pad quad |
| `SelectionMarker` | Selection marker |
| `Buoy` | Sphere (buoys, boom floats) |
| `Cylinder` | Cylinder (steel piles, bollards) |
| `ForBoat(type)` | Boat models (100 + type) |
| `GlyphBase` + n | Label glyphs |
