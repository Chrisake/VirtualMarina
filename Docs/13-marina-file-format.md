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
| `Version`, `Generator`, `SavedUtc` | Format version the file was written in, what wrote it, and when |
| `Extensions` | Anything else in the file: your own sections, and sections written by newer versions |
| `FromVisualizer(marina, generator, includeCamera, includeDesignerSettings)` | Captures a live visualizer |
| `ApplyTo(marina, applyStyle, applyCamera, applyDesignerSettings)` | Loads it into a visualizer |
| `Parse(json, allowNewerVersion)` / `ToJson(indented)` | Read and write the text |
| `Load(path, allowNewerVersion)` / `Save(path, indented)` | Read and write a file |
| `Validate()` | Errors in the stored layout, empty when it is sound |
| `SetExtension(key, value)` / `GetExtension<T>(key)` | Your own data, stored next to the marina |

`MarinaDocument.FileExtension` is `.marina.json` and `MarinaDocument.FileDialogFilter` is ready for an open/save dialog. A file that cannot be read raises `MarinaFormatException`; a file whose layout is unsound raises `MarinaLayoutException` from `ApplyTo`.

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
    "marineTraffic": { "enabled": true, "intensity": 0.45, "clearance": 260, "speedKnots": 7, "reach": 600, "seed": 12 },
    "landAreas": [
      { "id": "quay", "name": "Main quay", "kind": "Quay", "height": 1, "outline": [[-130, -32], [150, -32], [150, -6], [-130, -6]] },
      { "id": "lawn", "kind": "Grass", "height": 1.15, "outline": [[68, -30], [100, -31], [122, -26]],
        "trees": [ { "position": [97.6, -18], "height": 8.87, "crownRadius": 3.36, "shape": "Broadleaf" } ] }
    ],
    "piers": [
      { "id": "E", "name": "Pier E", "start": [110.75, -6], "headingDegrees": 0, "length": 50, "width": 2.5,
        "type": "FloatingConcrete", "deckHeight": 0.55, "pilingSpacing": 6, "berthingSides": "Right", "services": "PowerAndWater",
        "metadata": { "erpId": "PONT-07" } }
    ],
    "dividers": [ { "id": "E-R-D01", "pierId": "E", "start": [109.5, -6], "headingDegrees": -90, "length": 10, "width": 0.4, "type": "SinglePile", "spacing": 1.6 } ],
    "berths": [
      { "id": "E-R01", "pierId": "E", "label": "E-1", "center": [104.5, -3.5], "headingDegrees": 90, "length": 10, "width": 5,
        "maxDraft": 3, "status": "Occupied", "hasFingerPiers": false,
        "boat": { "id": "BT-1", "name": "Aurora", "type": "MotorYacht", "lengthMeters": 9.4, "beamMeters": 3.1, "ownerName": "P. Dubois" },
        "metadata": { "contract": "2026-114" } }
    ],
    "multiBerths": [ { "id": "B-VIP", "berthIds": ["B-L10", "B-L11"], "status": "Occupied", "style": "Alongside", "boat": { "id": "BT-70001", "name": "Meltemi Star", "type": "MotorYacht" } } ]
  },
  "presentation": {
    "water": { "waveAmplitude": 0.08, "waveSpeed": 1, "boatMotion": 1, "deepColor": [0.03, 0.2, 0.3] },
    "lighting": { "sunDirection": [0.45, 0.8, 0.35], "fogDensity": 0.0022 },
    "status": { "free": "#33C751", "occupied": "#E6332E", "padOpacity": 0.45 },
    "structures": { "pedestal": "#D1D4D9", "power": "#F2C21C", "water": "#2985D9" },
    "berthLabels": "None"
  },
  "camera": { "target": [12, 0, -40], "yawDegrees": 33, "pitchDegrees": 41, "distance": 180 },
  "designer": {
    "berthWidth": 5, "berthLength": 12, "berthSeparators": "PairedFingerPiers", "berthGap": 0.5, "berthServices": "PowerAndWater",
    "pierNamePattern": "Pier {pier}",
    "berthNaming": { "pattern": "{pier}-{side}{number}", "startNumber": 1, "increment": 1, "numberDigits": 2, "leftSide": "L", "rightSide": "R" }
  }
}
```

Every element — land area, pier, divider, berth and multi-berth — can carry a `"metadata"` object of host-owned strings (`Berth.Metadata` and friends). The library never reads it; it is there so an integration can keep its own keys, contract numbers or asset references inside the design instead of in a parallel table. An empty one is left out of the file.

Conventions: points are `[x, y]` in plan coordinates (meters, north is −y), colors are `#RRGGBB` (or `#RRGGBBAA`), light colors are `[r, g, b]` in 0–1, enums are written by name, and lengths are meters and angles degrees throughout — the same units as the API ([coordinates and conventions](02-coordinates-and-conventions.md)).

### The shoreline

`shoreline` is the mainland behind the marina, and it is optional: a file without it is a marina in open water, which
is how every file written before it existed reads back.

Only the drawn line is saved. `line` is the coast the designer clicked, and `landOnLeft` says which half of the plan is
land — the left of the line walked from its first point to its last. The first and last stretches run on without end,
so the shape covering that half is worked out on load and its far edge is never written down. Two points are enough:
that is a straight coast.

The one rule is that those two endless stretches must not cross each other, or neither side of the line is "the land".
`Shoreline.Validate` reports that, and a file whose line breaks it fails `ApplyTo` like any other unsound layout.

`scenery` is what is scattered over the land — `None`, `Countryside`, `Fields` or `Town` — and `scenerySeed` keeps that
scattering the same between sessions. The scenery itself is never written out: it is drawn from the seed, so the file
stays small however much of it there is.

### The passing traffic

`marineTraffic` is the shipping out at sea, and it is optional in the same way: a file without it is an empty sea, and
it is only written once the traffic has been switched on.

Only the settings are stored. The lanes, the vessels on them and where each one started are worked out from `seed`
when the file is loaded, so a busy sea costs no more to store than an empty one and looks the same every time it is
opened.

`clearance` is the distance in meters a lane has to keep from the marina, from every land area and from the mainland.
It is the setting that guarantees nothing ever appears to sail over a quay. Asking for more clearance than the open
water allows leaves fewer lanes, or none — never a lane that cuts a corner.

`reach` is how far out a lane runs before its vessels fade away. It is capped on load by however much water the grid
actually covers, since a vessel past the edge of the water would be sailing on nothing.

The vessels are decoration: they are not berths, they cannot be clicked, and they never appear in `berths`.

## Staying compatible between versions

`formatVersion` is `major.minor`. The rules are built into the reader, so designs keep working when either side is updated:

| Situation | What happens |
|---|---|
| A property is missing (written by an older version) | It falls back to the current default |
| A property is unknown (written by a newer version) | It is kept and written back out unchanged — an older application does not silently drop it |
| An enum name is unknown (`"services": "PowerAndWaterAndFuel"`) | It falls back to that enum's default instead of refusing the file |
| A newer **minor** version (`2.7` here) | Loads; `IsFromNewerVersion` tells you settings may have been ignored |
| A newer **major** version (`3.0` here) | Refused with a clear message, unless you pass `allowNewerVersion: true` |
| The file is not JSON, or is another kind of document | `MarinaFormatException` |

Format **1.x** is read as well, even though it is an older major version: back then piers were `"docks"`, berths were `"slips"` and `"berths"` was the list of multi-berth groups (`"slipIds"`, `"dockId"`, `"slipLabels"`, `"slipWidth"`, and the rest). Those files load unchanged, and saving one writes it in the current words.

Unknown properties are preserved per element, matched by id, and per section — so a round trip through an older build keeps a future `"tideSimulation"` section, a future field on a pier and a future field on a berth.

**Adding to the format** (for contributors): add optional properties with a default that means "as before", bump the **minor** version, and never change what an existing property means. Bump the **major** version only when old readers would get it wrong. New enum values are safe: older readers fall back to the default.

**Storing your own data**: use `Extensions`, under a key that won't clash.

```csharp
document.SetExtension("acme.erp", new { site = 42, tariff = "summer" });
var erp = document.GetExtension<ErpInfo>("acme.erp");
```

The library never touches those keys, and they survive being opened and saved in the designer.

## Reading the file yourself

The JSON is plain and stable enough to read from another language or a database job. `MarinaJson.Options` (and `CompactOptions`) expose the exact `System.Text.Json` settings used, including the point, color and enum converters, so you can serialize marina types of your own the same way. Serialization is source-generated, so it works in trimmed and WebAssembly builds.

For a flat list of elements instead of a file — for an ERP that keeps its own tables — `ExportObjects()` and `MarinaLayout.FromObjects(...)` are still there; see [Designer](12-designer.md#exporting-the-marina).
