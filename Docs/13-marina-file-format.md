# Marina files (.marina.json)

A marina file holds a whole marina in one JSON document: the layout that was drawn, how it should look and move, the berth labels and where the camera starts. The [designer application](14-designer-app.md) writes it and the host application loads it, so personalization travels with the design instead of living in host code.

```csharp
// Designer: save what the user drew.
MarinaDocument.FromVisualizer(marina, generator: "My Designer 1.0").Save(@"C:\marinas\harbor.marina.json");

// Host application: load it into a view.
MarinaDocument.Load(@"C:\marinas\harbor.marina.json").ApplyTo(marinaView.Marina);
```

That is the whole integration: `ApplyTo` sets the style (so the water grid is built at the stored size), loads the layout, sets the label mode, places the camera and restores the designer's tool settings.

## MarinaDocument

| Member | Meaning |
|---|---|
| `Name`, `Description` | Name of the marina (stored as `MarinaLayout.Name`) and a free-text note |
| `Layout` | Land areas, piers, dividers, berths and multi-berths |
| `Style` | The whole `MarinaStyle`: water and waves, lighting, status colors, land, piers, labels, selection, camera optics |
| `BerthLabels` | Which berths show their name on the water |
| `Camera` | Where the view opens (`CameraPose`), or null for the automatic overview |
| `Designer` | The designer's tool settings (`DesignerSettings`), so a design reopens the way it was left |
| `ReferenceImage` | The photo the marina was traced on, with its place and scale (`ReferenceImageRecord`: an `Image` holding the original file bytes as a `byte[]` in `EncodedData`, plus `Center`, `MetersPerPixel`, `Opacity`, `Visible`, `AboveScene`), or null |
| `CameraPresets`, `DisabledCameraPresets` | Saved views, and the built-in views that were switched off |
| `Version`, `Generator`, `SavedUtc` | Format version the file was written in, what wrote it, and when |
| `Extensions` | Anything else in the file: your own sections, and sections written by newer versions |
| `FromVisualizer(marina, generator, includeCamera, includeDesignerSettings, includeReferenceImage)` | Captures a live visualizer |
| `UpdateFrom(marina, ...)` | Refreshes a document that was loaded, keeping what the file carried that this version does not know |
| `ApplyTo(marina, applyStyle, applyCamera, applyDesignerSettings, applyReferenceImage)` | Loads it into a visualizer |
| `Parse(ReadOnlySpan<byte> utf8, allowNewerVersion)` / `ToUtf8Bytes(indented)` | Read and write UTF-8 bytes: a file's contents, a database column, a download |
| `LoadAsync(stream, allowNewerVersion, ct)` / `SaveAsync(stream, indented, ct)` | Read and write a stream |
| `Parse(json, allowNewerVersion)` / `ToJson(indented)` | Read and write text (wrappers around the UTF-8 members) |
| `Load(path, allowNewerVersion)` / `Save(path, indented)` | Read and write a file |
| `Validate()` | Errors in the stored layout, empty when it is sound |
| `SetExtension(key, value, typeInfo)` / `GetExtension(key, typeInfo)` | Your own data, stored next to the marina, serialized with your own (source-generated) metadata |
| `SetExtension(key, value)` / `GetExtension<T>(key)` | The same through reflection; not for trimmed or AOT-compiled applications |

`MarinaDocument.FileExtension` is `.marina.json` and `MarinaDocument.FileDialogFilter` is ready for an open/save dialog. A file that cannot be read raises `MarinaFormatException` — reading never throws anything else — and a file whose layout is unsound raises `MarinaLayoutException` from `ApplyTo`.

### Bytes, streams and large files

The format is UTF-8 JSON, and the members that take bytes or streams read and write it without going through a .NET string. That matters mostly for the tracing photo (`referenceImage`), which is by far the largest thing a design can carry: its `data` is base64 in the file, and it is decoded from the UTF-8 bytes straight into a `byte[]` and encoded straight back, so a 10 MB photo is not held two or three times over as UTF-16 text while a design is opened or saved. `Load(path)` reads the file's bytes; a file saved as UTF-16 (with its byte-order mark) by a text editor still opens.

```csharp
// ASP.NET Core / Blazor: straight from the request or the browser file, no string in between.
var document = await MarinaDocument.LoadAsync(request.Body, cancellationToken: ct);
await document.SaveAsync(response.Body, indented: false, ct);

// A database column.
byte[] stored = document.ToUtf8Bytes(indented: false);
var again = MarinaDocument.Parse(stored);
```

Keeping the photo in a separate file next to the design (or packing both into one archive) would shrink the JSON further; it is not done, because a single self-contained file is what the designer and hosts pass around. It may come as an opt-in later.

### Saving safely

`Save(path)` writes the whole file to a temporary file in the same folder first (`.harbor.marina.json.1a2b3c4d.tmp`) and then moves it over the old one in one step. A crash, a full disk or an exception part-way through leaves the previous file exactly as it was and removes the temporary one. `SavedUtc` is only stamped once the file is safely written; `SaveAsync(stream)` stamps it once the stream has been written and flushed.

## What the file looks like

```json
{
  "format": "virtualmarina.marina",
  "formatVersion": "2.0",
  "generator": "VirtualMarina Designer 1.0",
  "savedUtc": "2026-09-21T20:35:07+00:00",
  "marina": { "name": "VirtualMarina Test Harbor" },
  "layout": {
    "shoreline": { "line": [[-400, -40], [-60, -52], [180, -44]], "landOnLeft": false,
      "height": 1.4, "kind": "Grass", "scenery": "Countryside", "scenerySeed": 41 },
    "marineTraffic": { "enabled": true, "clearance": 260, "edgeClearance": 700, "speedPercent": 100, "spawnDelaySeconds": 25,
      "reach": 8000, "maximumVessels": 16, "laneCount": 2, "laneSpacing": 160, "seed": 12 },
    "landAreas": [
      { "id": "quay", "name": "Main quay", "kind": "Quay", "height": 1, "outline": [[-130, -32], [150, -32], [150, -6], [-130, -6]] },
      { "id": "lawn", "kind": "Grass", "height": 1.15, "outline": [[68, -30], [100, -31], [122, -26]],
        "trees": [ { "position": [97.6, -18], "height": 8.87, "crownRadius": 3.36, "shape": "Broadleaf" } ] }
    ],
    "piers": [
      { "id": "E", "name": "Pier E", "start": [110.75, -6], "headingDegrees": 0, "length": 50, "width": 2.5,
        "type": "FloatingConcrete", "deckHeight": 0.7, "pilingSpacing": 6, "berthingSides": "Right", "services": "PowerAndWater",
        "metadata": { "erpId": "PONT-07" } }
    ],
    "dividers": [ { "id": "E-R-D01", "pierId": "E", "start": [109.5, -6], "headingDegrees": -90, "length": 10, "width": 0.4, "type": "SinglePile", "spacing": 1.6 } ],
    "berths": [
      { "id": "E-R01", "pierId": "E", "label": "E-1", "center": [104.5, -3.5], "headingDegrees": 90, "length": 10, "width": 5,
        "maxDraft": 3, "hasFingerPiers": false, "services": "Power", "metadata": { "contract": "2026-114" } }
    ],
    "multiBerths": [ { "id": "B-VIP", "berthIds": ["B-L10", "B-L11"], "status": "Occupied", "style": "Alongside",
      "boat": { "id": "BT-70001", "name": "Meltemi Star", "type": "MotorYacht", "lengthMeters": 21.5, "ownerName": "P. Dubois" } } ]
  },
  "presentation": {
    "water": { "waveAmplitude": 0.08, "waveSpeed": 1, "boatMotion": 1, "deepColor": [0.03, 0.2, 0.3] },
    "lighting": { "sunDirection": [0.45, 0.8, 0.35], "fogDensity": 0.0022 },
    "status": { "free": "#33C751", "occupied": "#E6332E", "padOpacity": 0.45 },
    "land": { "quay": "#BDB8AB", "grass": "#66944D", "building": "#CCC7BA", "roof": "#965440", "showTrees": true },
    "structures": { "pedestal": "#D1D4D9", "power": "#F2C21C", "water": "#2985D9" },
    "berthLabels": "None"
  },
  "camera": { "target": [12, 0, -40], "yawDegrees": 33, "pitchDegrees": 41, "distance": 180 },
  "designer": {
    "berthWidth": 5, "berthLength": 12, "berthSeparators": "PairedFingerPiers", "berthGap": 0.5, "berthServices": "PowerAndWater",
    "pierNamePattern": "Pier {pier}",
    "berthNaming": { "pattern": "{pier}-{side}{number}", "startNumber": 1, "increment": 1, "numberDigits": 2, "leftSide": "L", "rightSide": "R" }
  },
  "referenceImage": { "data": "iVBORw0KGgoAAAANSUhEUgAA…", "contentType": "image/png", "pixelWidth": 2400, "pixelHeight": 1600,
    "center": [20, -10], "metersPerPixel": 0.125, "opacity": 0.6, "visible": true, "aboveScene": true },
  "cameraPresets": [ { "name": "Fuel pier", "target": [40, 0, 10], "yawDegrees": 150, "pitchDegrees": 35, "distance": 60 } ],
  "disabledCameraPresets": [ "West" ]
}
```

The whole format is described by a JSON Schema (draft 2020-12), [`schema/marina.schema.json`](schema/marina.schema.json). A unit test compares it with the classes that write the file, property by property and enum by enum, so it cannot fall behind. The schema describes what is *written*; the reader accepts more (see below).

Every element — land area, pier, divider, berth and multi-berth — can carry a `"metadata"` object of host-owned strings (`Berth.Metadata` and friends). The library never reads it; it is there so an integration can keep its own keys, contract numbers or asset references inside the design instead of in a parallel table. An empty one is left out of the file.

Conventions: points are `[x, y]` in plan coordinates (meters, x east, y south — north is −y), headings count from +Z (south) toward +X (east) and are not compass bearings, colors are `#RRGGBB` (or `#RRGGBBAA`), light colors are `[r, g, b]` in 0–1, enums are written by name, and lengths are meters and angles degrees throughout — the same units as the API ([coordinates and conventions](02-coordinates-and-conventions.md), and every direction spelled out in [coordinate conventions, exactly](20-coordinate-conventions.md)).

### What a design carries, and what it leaves to the host

A design file describes the marina, not today's occupancy. A single berth's **status, boat and interaction flags** (`isVisible`, `isDisabled`, `isReadOnly`) are runtime state that the host sets from its own records every session, so they are not written; they are still read from files written before that was so. A **multi-berth** is the exception: it cannot exist without a boat and a status that is not Free, so both are written with it. A Free berth that carries a boat is not an error — the visualizer drops the boat when the berth is loaded.

A boat's `lengthMeters` and `beamMeters` are written only when the boat was given a size of its own; without them the boat takes the nominal size of its `type`, and follows the type if it changes. A pier's `deckHeight` works the same way. (Older files wrote both always; a value that equals the type's default reads as the default.)

### The berth label font

When a design was given a real font for its labels, the file carries the **outlines**, not just the name: `fontName`,
`fontBold` and `fontGlyphs`, one line per character holding its advance and its closed contours. That is what lets
the marina read the same on a machine where the font was never installed.

It is the one part of a design that is measurably large — around 40 to 55 KB for the printable ASCII of a typical
font, against a few hundred KB for a marina of several hundred berths. A design with no captured font carries none
of it and falls back to the built-in lettering.

### The shoreline and the passing traffic

Both are optional, and a file without them is a marina in open water — which is how every file written before they
existed reads back. What each setting *means* is in [The sea and the shore](17-sea-and-shore.md); what matters here
is how little of it reaches the file.

`shoreline` stores **only the drawn line**: `line` is the coast the designer clicked and `landOnLeft` says which half
of the plan is land. The first and last stretches of that line run on without end, so the shape covering the land is
worked out on load and its far edge is never written down. `scenery` and `scenerySeed` say what covers the land and
fix the arrangement; the scenery itself is drawn from the seed, so a wooded coast costs no more than a bare one.

A shoreline that does not split the plan cleanly in two leaves neither side of the line as "the land": the drawn
line crossing or touching itself (or turning straight back along itself), the two endless stretches crossing each
other, or an endless stretch running back across the drawn line. `Shoreline.Validate` reports each of these. Such a
file still *opens* — the line is read as it is, so nothing is lost — but it fails `ApplyTo` with a
`MarinaLayoutException`, exactly as a land area whose outline crosses itself does. (Up to this version a line crossing
itself was accepted and drew land in the wrong places; such a design now has to have its coast redrawn.)

`marineTraffic` stores **only the settings** — `clearance`, `edgeClearance`, `laneCount`, `laneSpacing`,
`speedPercent`, `maximumVessels`, `spawnDelaySeconds`, `reach`, `seed` and the vessel mix. The lanes are worked out
from the shoreline and the traffic from the seed, so a busy sea costs no more to store than an empty one, and moving
the coast moves the shipping with it.

A setting a file does not carry falls back to its default on load, so a design written by an earlier version opens
with sensible traffic rather than none. Whether the lanes are *drawn* is not stored: that is a working aid, and a
reloaded design always has it off.

The vessels are decoration: they are not berths, they cannot be clicked, and they never appear in `berths`.

## Staying compatible between versions

`formatVersion` is `major.minor`. The rules are built into the reader, so designs keep working when either side is updated:

| Situation | What happens |
|---|---|
| A property is missing (written by an older version) | It falls back to the current default — the domain's own default, so a default is only ever written down in one place |
| A value is `null` where it should not be | A `null` entry in a list is dropped; a `null` or blank id reads as a missing id (the element's default id); other `null`s read as missing |
| A drawn line repeats a point (a double-click, a hand edit) | Points repeated next to each other, and a last point repeating the first of an outline, are dropped on load. Validation refuses them, so without this such a file would not open |
| A property is unknown (written by a newer version) | It is kept and written back out unchanged — an older application does not silently drop it |
| An enum name is unknown (`"services": "PowerAndWaterAndFuel"`) | It falls back to that enum's default instead of refusing the file |
| A newer **minor** version (`2.7` here) | Loads; `IsFromNewerVersion` tells you settings may have been ignored |
| A newer **major** version (`3.0` here) | Refused with a clear message, unless you pass `allowNewerVersion: true` |
| The file is not JSON, or is another kind of document | `MarinaFormatException` |

Format **1.x** is read as well, even though it is an older major version: back then piers were `"docks"`, berths were `"slips"` and `"berths"` was the list of multi-berth groups (`"slipIds"`, `"dockId"`, `"slipLabels"`, `"slipWidth"`, and the rest). Those files load unchanged, and saving one writes it in the current words.

**How older files are brought up to date.** Reading happens in two stages. A file is first read into classes that know only the current format. If it is older than the current version — by the `formatVersion` it states, *or* by its shape (it has `docks`, `slips`, groups with `slipIds`, or any other name only 1.x wrote), whatever version it claims or whether it states one at all — it is read again as raw JSON and passed through an ordered list of migration steps, each taking the file from one version to the next (today one step, 1.x → 2.0), before being read into the current classes. A current file, which is nearly every file, is read once. The document keeps the version the file stated in `Version`; saving writes the current version.

Unknown properties are preserved per element, matched by id, and per section — so a round trip through an older build keeps a future `"tideSimulation"` section, a future field on a pier and a future field on a berth.

**Adding to the format** (for contributors): add optional properties with a default that means "as before", bump the **minor** version, and never change what an existing property means. Bump the **major** version only when old readers would get it wrong. New enum values are safe: older readers fall back to the default. A setting with a domain default is nullable in the wire classes and read as `value ?? default`, so the default lives in the domain alone. When a change needs old files rewritten (a rename, a move), add a step to `MarinaMigrations` rather than special cases in the wire classes, and add the property to [`schema/marina.schema.json`](schema/marina.schema.json) — the schema test fails until you do.

**Storing your own data**: use `Extensions`, under a key that won't clash. The names the format itself uses at the top of the file (`layout`, `presentation`, `camera`, `designer`, ... compared ignoring case) are reserved: `SetExtension` and `Extensions[key] = ...` throw `ArgumentException` for them. Start your keys with your product name. In a trimmed or ahead-of-time compiled application (Blazor WebAssembly with trimming, native AOT) pass the metadata from your own source-generated context; the overloads without it use reflection and are marked `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.

```csharp
[JsonSerializable(typeof(ErpInfo))]
partial class AcmeJsonContext : JsonSerializerContext { }

var context = new AcmeJsonContext(MarinaJson.CreateOptions());      // the marina file's JSON style
document.SetExtension("acme.erp", erpInfo, context.ErpInfo);
var erp = document.GetExtension("acme.erp", context.ErpInfo);

// Reflection, when trimming is not a concern:
document.SetExtension("acme.erp", new { site = 42, tariff = "summer" });
```

The library never touches those keys, and they survive being opened and saved in the designer.

## Reading the file yourself

The JSON is plain and stable enough to read from another language or a database job; [`schema/marina.schema.json`](schema/marina.schema.json) describes it. `MarinaJson.Options` (and `CompactOptions`) are the exact, read-only `System.Text.Json` settings the file is written with. They are bound to the format's own source-generated metadata, so they cannot serialize types of your own (that throws `NotSupportedException`). For your own types call `MarinaJson.CreateOptions(resolver)`, which returns a fresh, writable options instance with the same conventions — camelCase names, `[x, y]` points, hex colors, forgiving enums — and the type metadata you pass (your source-generated context, or `new DefaultJsonTypeInfoResolver()` for reflection). The format's own serialization is source-generated, so it works in trimmed and WebAssembly builds.

For a flat list of elements instead of a file — for an ERP that keeps its own tables — `ExportObjects()` and `MarinaLayout.FromObjects(...)` are still there; see [Designer](12-designer.md#exporting-the-marina).
