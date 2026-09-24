# Appearance

How the marina itself is drawn: the names on the water, the status colours, the light, the sea and the shadows, and
the boat models. Everything here lives on `marina.Style` or on the visualizer directly, and all of it is saved with
the design, so a host loading a `.marina.json` gets the look it was drawn with and need not configure anything.

What surrounds the marina — the mainland and the passing shipping — is in
[The sea and the shore](17-sea-and-shore.md).

## The style object

`marina.Style` (a `MarinaStyle`, also `MarinaViewControl.Style` and `<MarinaView MarinaStyle="...">`) groups the settings
in sections:

| Section | What it holds |
|---|---|
| `Lighting` | Sun, ambient light, highlights, sky and fog (also `marina.Lighting`) |
| `Water` | Water colors, waves, reflections, ripples, glints, boat motion (also `marina.Water`) |
| `Status` | Status colors, pad and boat opacities, disabled color, status markers |
| `Land` | Quay, lawn, rock, tree and building colors, and whether trees are shown |
| `Piers` | Colors of wooden and concrete piers, floats, fenders, bollards, piles, booms and pedestals |
| `Labels` | The lettering and colors of berth names |
| `Selection` | The selection marker and the hover and selection highlights |
| `View` | Camera field of view and smoothing |
| `Shadows` | Whether shadows are cast, and how dark they are |

Change any property at any time: every section raises `StyleSection.Changed` when a value actually changes, and the view
redraws (a view drawing [on demand](10-hosting-and-custom-views.md) wakes for it). Assign a whole new `MarinaStyle` to
switch themes; `style.Clone()` makes an independent deep copy to start one from. Numeric settings are held to the range
given with them: a value outside is clamped, and one that is not a number (NaN or infinity, which a file may carry) is
replaced with the default, since every value goes straight into the shaders.

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
- **Colors:** `Style.Labels.Color` (near-white) on the water, `AshoreColor` (dark, to read against concrete or grass) for
  berths ashore, `HighlightColor` (yellow) while the berth is hovered or selected, `DisabledColor` (gray) for disabled berths.
- **Waves:** the text sits just above the highest point the waves can reach (the sum of wave amplitudes × `Water.WaveAmplitude`), so waves never cover it from any camera position.
- **Which berths:** hidden and filtered-out berths get no label.
- **Characters:** labels use a built-in stroke font (no textures) covering `A–Z`, `0–9` and `- _ + . , : / ( ) # ?`. Lowercase is drawn as uppercase and other characters as `?`.

`BerthLabelModeExtensions.Includes(mode, status)` and `GetDisplayName(mode)` help build a mode picker.

### The font the labels are set in

By default labels use a built-in stroke font that ships with the library. A design can instead carry **a real font**,
captured into it:

```csharp
marina.Style.Labels.Font = capturedFont;   // a LabelFontDefinition
marina.Style.Labels.Font = null;           // back to the built-in lettering
```

A `LabelFontDefinition` is a set of glyph outlines — for each character, how far the pen moves and the closed
contours that draw it, with the baseline at y = 0 and the cap height at y = 1. The **outlines travel with the
design**, so the marina reads the same on a machine that has never had the font installed, and the web view gets the
same lettering as the desktop one. Counters (the hole in an O, the two in an 8) are contours of their own; which are
holes is worked out from which lie inside which, so the winding need not mean anything.

| Member | |
|---|---|
| `Name`, `IsBold` | What the font is called and which face was captured |
| `TryGetGlyph(c, out glyph)` | Whether the font carries a character |
| `AdvanceOf(c)`, `MeasureWidth(text)` | Layout, in multiples of the cap height |
| `CreateMeshes()` | One mesh per glyph, in the same model space as the built-in lettering |
| `Encode()` / `Decode(name, lines, bold)` | The form stored in a design file |

Setting `Labels.Font` registers the glyph meshes with `marina.Meshes` and setting it back to null removes them
again, so the renderers re-upload on their own. A character the captured font does not carry falls back to the
built-in lettering, so a name with something unusual in it still reads rather than vanishing.

Capturing a font is the one part that needs the machine it is installed on: the
[designer application](14-designer-app.md) offers every family it finds and writes the outlines into the design.

#### The built-in lettering

Used when no font is captured, and as the fallback for missing characters. Two settings, chosen separately:

```csharp
marina.Style.Labels.Typeface = LabelTypeface.Serif;    // the shape of the letters
marina.Style.Labels.FontFamily = LabelFont.Bold;       // their weight and width
```

| `Typeface` | | | `FontFamily` | |
|---|---|---|---|---|
| `Sans` | Plain strokes, the default | | `Regular`, `Bold` | Even or heavy strokes |
| `Serif` | Small feet on the stems | | `Condensed`, `Wide` | Narrower or wider |
| `Slab` | Heavier square feet | | | |

These are strokes on a fixed grid rather than real glyph shapes, so they stay legible at any size and cost nothing to
carry, but they are plain. A captured font looks considerably better and is worth preferring where the design can be
made on a machine that has one.

## Status colors and overlays

See [Berth status, boats and flags → Colors](04-status-and-flags.md#colors): `SetStatusColor`, `SetDisabledColor`, `SetOverlayOpacity`, `ResetStatusColors`.

`ColorRgba` is an RGBA color with components 0–1, used by the shaders as it is, with no gamma conversion either way: the
components are the familiar sRGB values, so `FromHex` and `ToHex` round-trip byte for byte.

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
| `SetSunAngles(azimuth, elevation)` | | Sets `SunDirection` from an azimuth and an elevation (1–90°) |
| `SunColor` | (1, 0.95, 0.86) | Sunlight color |
| `AmbientColor` | (0.36, 0.40, 0.48) | Ambient light |
| `SpecularStrength`, `Shininess` | 0.35, 32 | Highlights on objects (0–2 and 1–1024) |
| `SkyColor` | (0.56, 0.72, 0.88) | Reflected by the water at grazing angles |
| `FogColor` | (0.76, 0.85, 0.92) | Horizon color and background |
| `FogDensity` | 0.0022 | Squared-exponential fog per meter, 0–1; 0 disables it |

```csharp
marina.Lighting.SetSunAngles(azimuthDegrees: 250, elevationDegrees: 12);   // evening light, low in the west-north-west
marina.Lighting.SunColor = new Vector3(1.0f, 0.75f, 0.55f);
```

`SunDirection` is in world coordinates (+Y up, +X east, +Z south); a zero or non-finite vector puts the sun overhead.
The azimuth of `SetSunAngles` uses the library's **heading** convention, not a compass bearing: 0° = +Z (south),
90° = +X (east), 180° = −Z (north). A compass bearing `b` is the azimuth `180 − b`. See
[coordinate conventions](20-coordinate-conventions.md). Colors may go above 1 per channel (up to 10), for a light
brighter than white.

## Water

`marina.Water` (`WaterSettings`):

| Property | Default | Notes |
|---|---|---|
| `Size` | 4200 m | Edge length of the **detailed** water, 50–100 000 m. Can be changed at any time |
| `GridResolution` | 160 | Cells per side, 2–400. Construction only |
| `DeepColor`, `ShallowColor` | teal tones | Water color in shade and where the sun lights it |
| `WaveAmplitude` | 0.08 m | Base wave height, 0–5; 0 is flat water |
| `WaveFrequency` | 1 | 0–10; larger means shorter, choppier waves |
| `WaveSpeed` | 1 | 0–10; 0 freezes the water and floating objects |
| `SkyReflection` | 1 | 0–1: the bright, cloud-like reflections toward the horizon; lower for a darker, calmer surface |
| `Ripples` | 1 | 0–2: the small ripples that break up the reflections; 0 is glassy |
| `SunGlints` | 1 | 0–2: the sparkle of the sun on the water |
| `BoatMotion` | 1 | 0–3: how much boats, buoys and boom floats move with the waves; 0 keeps them still |

```csharp
var marina = new MarinaVisualizer(new MarinaVisualizerOptions
{
    Water = new WaterSettings { Size = 2400, GridResolution = 240, WaveAmplitude = 0.05f },
});
marina.Water.WaveSpeed = 0;   // calm, static water
```

The water grid is re-centered on the layout by `InitializeLayout`, and grown when a layout or a reference image reaches past it.

Moving water is what keeps an on-demand view drawing: with `WaveSpeed` at 0, or both `WaveAmplitude` and `Ripples` at 0,
the water holds still, and a marina with nothing else moving (no pulse, no selection marker, no traffic) is not redrawn
until something changes.

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

Each shadow costs one extra instance, so on a marina of several hundred berths shadows roughly double the scene —
hence the toggle. They need no depth pass and no shadow map, and behave identically in the OpenGL and WebGL views.
Shadows are drawn **unlit** (`RenderAnimation.Unlit`): a flattened object has no normals worth lighting, so a shadow
is its tint alone, with fog, in a pass of its own between the water and the other transparent objects. They sit in
layers of their own too (`StructureShadows`, `BerthShadows`), so moving the sun re-sends the shadows and nothing else.

What projected shadows cannot do:

- **One plane per object.** A boat's shadow falls on the water, not up the side of the pier beside it.
- **No self-shadowing.** A cabin does not shade its own deck.
- **The ground casts none**, being what the shadows land on. Trees and the mainland scenery are separate meshes, so
  they do.
- **Overlap darkens.** A flattened object covers itself, so a shadow is darker than `Strength` alone — much darker
  for something like a tree crown, which is several rounded blobs on top of one another — and can look blotchy past
  about 0.4.
- **Nothing below about 4° of elevation** (`ShadowProjection.MinimumSunHeight`), where a shadow would stretch to the horizon.

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

Both renderers re-upload changed meshes automatically (`MeshLibrary.Version`). A renderer tells a changed mesh by its
object, so to change one, register a new `MeshData` under the id rather than editing the arrays of the one already
registered, which would never be uploaded again. `MeshBuilder` helps build meshes from boxes, lofts, cylinders, spheres and plates. The procedural models use `MeshBuilder` too.

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
| `Shoreline` | The mainland ground |
| `ForLand(slot)` | A land area's ground (at most `MaxLandSlots` land areas at once) |

Trees, rocks and the mainland scenery are drawn as instances of a few shared meshes the library registers itself. The ids
`ShorelineScenery` and `ForLandTrees(slot)` remain for a host that bakes those into single meshes of its own
(`LandMeshFactory.CreateShorelineScenery`, `CreateTrees`), but the visualizer no longer registers anything under them.
