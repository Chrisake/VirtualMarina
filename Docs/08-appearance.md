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
    Intensity = 0.45f,   // 0-1, as a share of MaximumVessels
    Clearance = 300f,    // meters: how near the middle of the marina the nearest lane passes
    LaneCount = 3,
    LaneSpacing = 160f,  // meters between one lane and the next
    SpeedKnots = 8f,
    Seed = 12,           // the same seed always puts the same traffic in the same place
});
```

| Property | Default | Notes |
|---|---|---|
| `IsEnabled` | false | Off until asked for |
| `Intensity` | 0.5 | How busy, as a share of `MaximumVessels` |
| `MaximumVessels` | 24 | The most on the water at once, up to `MarineTraffic.VesselLimit` (60) |
| `Clearance` | 300 m | The nearest lane's closest approach to the middle of the marina |
| `LaneCount` | 2 | How many lanes, up to `MarineTraffic.LaneLimit` (8) |
| `LaneSpacing` | 160 m | Between one lane and the next, and how loosely vessels sit in them |
| `SpeedKnots` | 8 | How fast they cross |
| `Reach` | 6000 m | How far each end runs on past the coast: where a vessel appears and where it fades |
| `Seed` | 1 | Fixes the vessels on the lanes |
| `Vessels` | empty | The mix to draw from; empty means `MarineTraffic.DefaultVessels` |

### Where the lanes go

The lanes follow the coast. Each is the [shoreline](07-land-and-shoreline.md) pushed out to sea: its ends run
alongside the shoreline's endless segments and its middle curves between them, so the shipping reads as passing along
the coast rather than cutting across it at an angle of its own. A marina with no shoreline behind it has nothing to be
parallel to and gets straight lanes instead.

`Clearance` is how far out the **nearest** lane is pushed, measured as its closest approach to the middle of the
marina — not a margin added to the marina's own size. That is what makes the setting mean something on its own: 300 m
puts the near lane 300 m off whether the marina is 100 m across or 2 km. `LaneSpacing` then steps each further lane
out to sea from there.

The one thing that overrides `Clearance` is land: lanes that would cross a land area are pushed out until none does,
and then brought back in as near as the land allows, so they can end up a little further out than asked but never over
a quay or through the piers.

### Which way they run, and why nothing looks ruled

Every vessel in a lane runs the same way, and neighbouring lanes run opposite ways, as a traffic separation scheme
does. So vessels overtake within a lane and pass between lanes, and **nothing ever meets head-on** — which is the
reason for the fixed direction rather than a coin toss per vessel. Each lane stores its points in the direction its
traffic travels, so `At(along)` always faces the way the vessels are going; `Reversed` says which lanes run the other
way.

Two things keep it off a ruled line, both bounded well inside `LaneSpacing` so lanes never cross and vessels from
neighbouring lanes never meet:

- each lane **bulges gently seaward** along its length by a differing amount, so the lanes are not quite parallel and
  the gap between two of them opens and closes as they run;
- each vessel **holds its own offset** within its lane and wanders slowly across it as it goes, taking about seventy
  seconds to cross and back. Widening `LaneSpacing` widens both, so wide lanes look looser.

`TrafficLanes` gives the planned lanes back, nearest the marina first — each with its `Points`, its `Length`,
`Reversed`, `DistanceTo(point)` for the closest approach and `At(along)` for a position and heading a fraction of the
way along. It is empty when the traffic is off or its settings are unsound.

```csharp
marina.ShowTrafficLanes = true;   // draws the lanes on the water while the spacing is being set
```

`ShowTrafficLanes` is a working aid, not a style: it draws the lanes, tinted by which way each one runs, so the
clearance and the spacing can be judged by eye. It is not saved with the design. The Designer offers it as **Show the
lanes** in the traffic card of the Look panel.

### The vessels on them

`Intensity` and `MaximumVessels` decide how many vessels there are in total; `LaneCount` shares those out rather than
multiplying them, so adding lanes spreads the same sea thinner. Both ends of every lane sit far out in the flat sea
beyond the detailed water, so a vessel appears and disappears where nobody is looking, crosses within view of the
marina, and carries on out the other side. The fade at each end is deliberately short — a vessel is at full strength a
twentieth of the way along — so it is never caught materialising. They are decoration: they are not berths, they
cannot be clicked or hit-tested, and they take no part in selection. `GetTrafficVessels()` returns where they are
right now, for a host that wants to draw its own marker.

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
