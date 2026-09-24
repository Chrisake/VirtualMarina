# Samples and use cases

The repository ships a sample marina, a sample ERP integration and two test hosts that exercise the whole API. They
are the fastest way to see what the library does, and the sample integration is a reasonable starting point for a
real one.

```bash
dotnet run --project samples/VirtualMarina.TestHost.WinForms   # desktop (Windows)
dotnet run --project samples/VirtualMarina.TestHost.Blazor     # then open http://localhost:5280
dotnet run --project apps/VirtualMarina.Designer               # the stand-alone designer (Windows)
dotnet run --project apps/VirtualMarina.Designer.Blazor        # the designer in the browser, http://localhost:5290
dotnet run --project apps/VirtualMarina.Designer.Desktop       # the browser designer in a window of its own
```

The WinForms projects target `net8.0-windows` and only run on Windows; the Blazor ones run anywhere the .NET 8 SDK
does. The desktop launcher serves the Blazor designer and opens it in an application window of a Chromium-family
browser; see [the designer application](14-designer-app.md).

## The sample marina

`MockMarinaFactory.CreateSampleMarina(seed, clock)` builds a complete marina, the same one every time for a given
seed, so screenshots and tests stay comparable. The optional `TimeProvider` only dates expected arrivals; pass a fixed
one to pin those too, or leave it out for the system clock. Seen from the quay looking out to sea, piers A to D run left to right:

| | |
|---|---|
| **A** | Fixed concrete, pile dividers between berths |
| **B** | Floating wooden, with a motor yacht moored alongside three berths (`B-L10` to `B-L12`) as one [multi-berth](05-multi-berths.md) |
| **C** | Floating concrete, sized for multihulls |
| **D** | Floating wooden, superyacht berths on one side and booms between the jet ski berths on the other |
| **E** | A single-sided floating concrete pontoon along the east mole, single piles between its berths |
| **W** | A single-sided fixed quay wall, pile dividers |

The land is polygons rather than rectangles: the main quay, a trapezoid boatyard with two rows of berths ashore, a
tapered east mole with a maintenance row, an irregular lawn, and two curved rubble-mound breakwaters drawn as rock.
A few berths are disabled, read-only or hidden, so the [status and flag](04-status-and-flags.md) handling has
something to show.

Other helpers on the same class are useful when writing a host or a test:

| | |
|---|---|
| `FindFreeWaterBerth(marina, boat)` | The first free, enabled berth the boat actually fits |
| `FindFreeLandBerth(marina, boat)` | The same for a spot ashore |
| `CreateBoatForBerth(berth, rng, types)` | A plausible boat that fits a given berth |
| `CreateRandomActivity(berths, rng, count, clock)` | A batch of `BerthUpdate`s, for simulating traffic through the marina |
| `CreateGuestBerth(marina, pierId)` | Adds a berth to a pier at runtime |

## The sample ERP integration

`SampleErpIntegration(marina, rng, log, clock)` is what a host application typically does with the interaction
events, and both test hosts share it. Seed `rng` and pass a fixed `TimeProvider` for repeatable runs. It subscribes to `BerthSelected`, `MultiBerthSelected` and `BerthActionInvoked`, and shows the three things
a real integration has to get right:

- **Filling the tooltip** with its own data rather than only the berth's;
- **Offering actions** that depend on the berth's current state, and running them when invoked;
- **Keeping its own objects** in `Berth.ExternalData`, which is shared by every snapshot of a berth, so a record
  written while handling one event is still there in the next.

See [Selection, tooltips and actions](06-selection-tooltips-actions.md) for the API it is built on.

## What the library is for

**A berth plan inside an ERP or a booking system.** The common case: the ERP owns the data, the visualizer owns the
picture. Load the layout once, push status and boats in as they change, and answer `BerthSelected` with whatever the
ERP knows about that berth. Berth ids are the ERP's own keys, so nothing needs mapping. See
[Berth status, boats and flags](04-status-and-flags.md).

**A front-desk screen.** Set `BerthLabelMode` so free berths name themselves, filter to one status, and use
`SetSelection(..., focusCamera: true)` to fly to a berth someone has just looked up.
See [Camera and focus](07-camera-and-focus.md).

**Drawing a marina to scale.** The [designer application](14-designer-app.md) traces a scaled aerial photograph and
writes a [`.marina.json`](13-marina-file-format.md) file the host then loads. The same tools are available in-process
through [`marina.Designer`](12-designer.md) if the host wants to offer editing itself.

**The same marina on the desktop and the web.** One core assembly drives both views, so a WinForms client and a
Blazor one show the same marina from the same layout and the same integration code.
See [Hosting and custom views](10-hosting-and-custom-views.md).

**Something other than WinForms or Blazor.** `ISceneRenderer` and the input controller are public, so the visualizer
can be driven from WPF, Avalonia, MAUI or a game engine by forwarding a frame and some input.

## The test hosts

Both hosts exercise every API rather than showing a tidy demo: status changes, multi-berths, filters, camera
presets, appearance settings, the designer, saving and loading. They are the place to look for a working call to
something the guides describe.

| | |
|---|---|
| `VirtualMarina.TestHost.WinForms` | `MarinaViewControl` plus panels for every part of the API, under a File / Edit / View menu with the usual shortcuts. The menu is there to show the [keyboard contract](07-camera-and-focus.md#which-keys-the-view-takes): Ctrl+O, Ctrl+S, Ctrl+Z (the designer's Undo), Ctrl+Y and Ctrl+R reach the form whether or not the 3D view has the focus |
| `VirtualMarina.TestHost.Blazor` | `<MarinaView>` in a WebAssembly page, with the same panels |
| `apps/VirtualMarina.Designer`, `apps/VirtualMarina.Designer.Blazor` | The stand-alone drawing tool on the desktop and in the browser, which are real applications rather than harnesses |

Both test hosts keep the design file they last opened and save it back with `MarinaDocument.UpdateFrom` rather than
writing a fresh one, so sections and properties they do not understand survive the round trip — the pattern a host
that edits designs should follow (see [marina files](13-marina-file-format.md)).
