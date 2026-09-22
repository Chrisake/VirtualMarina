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
- **Placement:** flat on the water just past the berth's open (seaward) end.
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
| `Size` | 1400 m | Grid edge length. Set at construction only (`MarinaVisualizerOptions.Water`) |
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

## Passing traffic

Vessels crossing the bay beyond the marina, so the sea is not empty. It is off until it is asked for:

```csharp
marina.SetMarineTraffic(MarineTraffic.None with
{
    IsEnabled = true,
    Intensity = 0.45f,   // 0-1, up to MarineTraffic.MaximumVessels
    Clearance = 300f,    // meters a lane must keep from the marina and any land
    SpeedKnots = 8f,
    Seed = 12,           // the same seed always puts the same traffic in the same place
});
```

| Property | Default | Notes |
|---|---|---|
| `IsEnabled` | false | Off until asked for |
| `Intensity` | 0.5 | Scales the vessel count up to `MaximumVessels` (24) |
| `Clearance` | 300 m | How far lanes stay from the marina, the land areas and the mainland |
| `SpeedKnots` | 8 | How fast they cross |
| `Reach` | 600 m | Half a lane's length; capped by what the water grid covers |
| `Seed` | 1 | Fixes the lanes and the vessels on them |
| `Vessels` | empty | The mix to draw from; empty means `MarineTraffic.DefaultVessels` |

Lanes are straight lines planned once, and a lane is only kept when **every** point along it stays `Clearance` away
from the marina, from every land area and from the mainland behind the shore — so nothing ever appears to sail over a
quay or through the piers. Asking for more clearance than the open water allows leaves fewer lanes; `TrafficLaneCount`
says how many were found room for, and 0 means the traffic cannot be drawn at all.

Vessels fade in at one end of their lane and out at the other, so nothing pops into view. They are decoration: they are
not berths, they cannot be clicked or hit-tested, and they take no part in selection. `GetTrafficVessels()` returns
where they are right now, for a host that wants to draw its own marker.

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
