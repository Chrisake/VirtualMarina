# Localization

Every piece of text the libraries and the Designer application display comes from a resource file, one per assembly, so a host can ship the marina view in any language without touching the code.

## Choosing the language

Text is looked up through `CultureInfo.CurrentUICulture`, so an application that already sets the UI culture needs nothing. `MarinaLocalization` sets it for the whole application in one line, including threads started later:

```csharp
using System.Globalization;
using VirtualMarina.Core.Api;

MarinaLocalization.Culture = new CultureInfo("el-GR"); // before the first window is shown
```

| Member | What it does |
|---|---|
| `MarinaLocalization.Culture` | The culture text is looked up in. Null (the default) follows `CultureInfo.CurrentUICulture` |
| `MarinaLocalization.AvailableCultures()` | The neutral language plus every culture a translation was found for — ready for a language picker |

A culture without a translation falls back to the neutral English text, and a single missing entry falls back on its own, so a partial translation is safe: nothing ever comes out blank.

## Where the text lives

| Assembly | Resource file | What it holds |
|---|---|---|
| `VirtualMarina.Core` | `Resources/Strings.resx` | Status, boat, pier and separator names, the default tooltip, the designer tool hints, the undo step descriptions |
| `VirtualMarina.WinForms` | `Resources/Strings.resx` | `MarinaDesignerPanel`, the selection popup, the file dialog filter |
| `VirtualMarina.Blazor` | `Resources/Strings.resx` | `<MarinaDesignerPanel>` |
| `VirtualMarina.Designer` | `Resources/Strings.resx` | The Designer application: menus, toolbar, inspector, dialogs and log |

The names of the settings themselves are in the core library, reached through `DisplayNames`, so the WinForms panel, the Blazor panel and the Designer application all offer the same words:

```csharp
using VirtualMarina.Core.Api;

comboBox.Items.Add(BerthSeparator.PairedFingerPiers.GetDisplayName()); // "Pier every other berth"
comboBox.Items.Add(PierSides.Left.GetDisplayName());                  // "Boats on the left only"
label.Text = berth.Status.GetDisplayName();                           // "Temporarily Free"
```

`GetDisplayName()` exists for `BerthStatus`, `BerthLabelMode`, `BoatType`, `PierType`, `PierServices`, `PierSides`, `LandKind`, `DividerType`, `MooringStyle`, `BerthSeparator` and `DesignTool`.

## Adding a language

1. Copy `Strings.resx` next to itself as `Strings.<culture>.resx` — for example `Resources/Strings.el.resx` for Greek or `Strings.el-GR.resx` for one country only. Do this in every assembly whose text you want translated; they are independent.
2. Translate the `<value>` elements. **Leave the `name` attributes untouched** — they are the keys the code asks for.
3. Keep the placeholders (`{0}`, `{1:0.0}`) and their meaning; each entry's `<comment>` says what they stand for. They may be reordered when the target language needs it.
4. Build. The SDK compiles each file into a satellite assembly under `<culture>/VirtualMarina.*.resources.dll` next to the application, and `AvailableCultures()` picks it up.

Nothing else is needed: no code generation step, no project file edit, and entries that are not translated fall back to English.

### What not to translate

- Ids (`A-L01`, `Pier A`) — they are data, and the file format stores them as written.
- The application name `VirtualMarina Designer`, unless the product is renamed too.
- The file extension and the JSON keys of a `.marina.json` design; those are the same in every language ([marina files](13-marina-file-format.md)).

## Numbers and dates

Sizes, coordinates and times are formatted with `CultureInfo.CurrentCulture` (the *formatting* culture), not the UI culture, so a Greek UI on a machine set to Greek shows `12,5 m`. Set both when you want them to agree:

```csharp
var culture = new CultureInfo("el-GR");
MarinaLocalization.Culture = culture;                      // the words
CultureInfo.DefaultThreadCurrentCulture = culture;         // the numbers and dates
```

The `.marina.json` format is unaffected: it always writes invariant numbers, so a design drawn on one machine reads the same on any other.
