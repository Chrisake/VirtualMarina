# Localization

Every piece of text the libraries and the Designer applications display comes from a resource file, one per assembly, so a host can ship the marina view in any language without touching the code.

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

`MarinaLocalization.Culture` is one setting for the whole process (it also sets `CultureInfo.DefaultThreadCurrentUICulture`), which suits a desktop application. A Blazor Server application serving users in different languages should leave it null and set `CultureInfo.CurrentUICulture` per request or circuit, as ASP.NET Core's request localization does.

**Blazor WebAssembly.** The UI culture starts as the browser's language, so the view and the Blazor Designer follow it with no code. To choose another, set `MarinaLocalization.Culture` in `Program.cs` before `RunAsync`: the runtime loads the satellite assemblies for the culture that is current when the app starts, so changing it later takes a reload of the page.

## Where the text lives

| Assembly | Resource file | What it holds |
|---|---|---|
| `VirtualMarina.Core` | `src/VirtualMarina.Core/Resources/Strings.resx` | Status, boat, pier, separator and scenery names, the default tooltip, the camera preset names and descriptions, the designer tool hints and the measurements written in the designer's preview, the undo step descriptions, and the messages of the exceptions and validation results the API raises (layout, multi-berths, marina files) |
| `VirtualMarina.WinForms` | `src/VirtualMarina.WinForms/Resources/Strings.resx` | `MarinaDesignerPanel`, the selection popup, the image file dialog filter, and the placeholder the view shows when OpenGL cannot start or in the Visual Studio designer |
| `VirtualMarina.Blazor` | `src/VirtualMarina.Blazor/Resources/Strings.resx` | `<MarinaDesignerPanel>`, the view's accessible name (its `aria-label`, unless the host passes `AriaLabel`), and the message shown over the view when drawing keeps failing |
| `VirtualMarina.Designer.Common` | `apps/VirtualMarina.Designer.Common/Resources/Strings.resx` | Both Designer applications, WinForms and Blazor: menus, toolbar, inspector, Look and Cameras panels, dialogs, log, the Shortcuts help and the window title |

The two Designer applications have no resource file of their own: everything they display is in `VirtualMarina.Designer.Common`, so one translation covers both.

The names of the settings themselves are in the core library, reached through `DisplayNames`, so the WinForms panel, the Blazor panel and the Designer application all offer the same words:

```csharp
using VirtualMarina.Core.Api;

comboBox.Items.Add(BerthSeparator.PairedFingerPiers.GetDisplayName()); // "Pier every other berth"
comboBox.Items.Add(PierSides.Left.GetDisplayName());                  // "Boats on the left only"
label.Text = berth.Status.GetDisplayName();                           // "Temporarily Free"
```

`GetDisplayName()` is an extension method for `BerthStatus`, `BerthLabelMode`, `PierServices`, `PierSides`, `LandKind`, `HinterlandScenery`, `DividerType`, `MooringStyle`, `BerthSeparator` and `DesignTool`. Boat and pier types have static ones: `BoatTypeCatalog.GetDisplayName(type)` and `Pier.GetDisplayName(type)`.

## Adding a language

1. Copy `Strings.resx` next to itself as `Strings.<culture>.resx` — for example `Resources/Strings.el.resx` for Greek or `Strings.el-GR.resx` for one country only. Do this in every assembly whose text you want translated; they are independent.
2. Translate the `<value>` elements. **Leave the `name` attributes untouched** — they are the keys the code asks for.
3. Keep the placeholders (`{0}`, `{1:0.0}`) and their meaning; each entry's `<comment>` says what they stand for. They may be reordered when the target language needs it.
   - **Plurals.** A text that depends on a count comes in two entries, `<name>_One` and `<name>_Other` (for example `UndoAddBerths_One`, "Add {0} berth", and `UndoAddBerths_Other`, "Add {0} berths"). Translate both. The singular is used for 1, and for 0 as well in French and Portuguese; a language with more forms (Polish, Russian, Arabic) gets its nearest two until more keys are added.
   - **The designer's preview text** (the `Overlay…` entries, such as `{0} M` and `LAND THIS SIDE`) is drawn in the built-in stroke font, which has only `A`–`Z`, `0`–`9` and `- _ + . , : / ( ) # ?`. Lowercase is drawn as capitals, but any other character — an accented or a Greek letter — shows as `?`, so these few entries have to stay within that set (a unit abbreviation such as `M` usually does).
4. Build. The SDK compiles each file into a satellite assembly under `<culture>/VirtualMarina.*.resources.dll` next to the application, and `AvailableCultures()` picks it up.

Nothing else is needed: no code generation step, no project file edit, and entries that are not translated fall back to English.

### What not to translate

- Ids (`A-L01`, `Pier A`) — they are data, and the file format stores them as written.
- The application name `VirtualMarina Designer`, unless the product is renamed too.
- The keyboard shortcuts. They are in the command table (`DesignerCommands`), not in the resources, and are the same in every language; the menus and the Shortcuts help show them from there.
- The file extension and the JSON keys of a `.marina.json` design; those are the same in every language ([marina files](13-marina-file-format.md)).

## Numbers and dates

Sizes, coordinates and times are formatted with `CultureInfo.CurrentCulture` (the *formatting* culture), not the UI culture, so a Greek UI on a machine set to Greek shows `12,5 m`. Set both when you want them to agree:

```csharp
var culture = new CultureInfo("el-GR");
MarinaLocalization.Culture = culture;                      // the words
CultureInfo.DefaultThreadCurrentCulture = culture;         // the numbers and dates
```

The `.marina.json` format is unaffected: it always writes invariant numbers, so a design drawn on one machine reads the same on any other.
