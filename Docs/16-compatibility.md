# Compatibility and versioning

This library is meant to be integrated and left alone. An application that builds against 1.0 should keep building, and keep running, against every later 1.x release without edits. This page says exactly what that promise covers, how it is enforced, and how to add things without breaking it.

## The promise

| Version change | What may happen |
|---|---|
| **Patch** (1.0.0 → 1.0.1) | Fixes only. Nothing is added to or removed from the API |
| **Minor** (1.0 → 1.1) | Things are **added**: new types, new members, new optional parameters, new enum members. Nothing published is removed, renamed or given a different meaning |
| **Major** (1.x → 2.0) | The only release allowed to remove or change what is already published |

`AssemblyVersion` stays at `<major>.0.0.0` for a whole major line, so a newer 1.x assembly is a drop-in replacement: no rebuild, no binding redirects. `FileVersion` and `InformationalVersion` carry the exact version for support.

### What is covered

Everything in the four shipped assemblies that a host application can bind to: `VirtualMarina.Core`, `VirtualMarina.WinForms`, `VirtualMarina.Blazor` and `VirtualMarina.Rendering.OpenGL` — type and member names, signatures, default parameter values, the numbers behind enum members, the values of `const` fields, and the `.marina.json` [file format](13-marina-file-format.md).

### What is not covered

These are free to change in any release, so don't build on them:

- **Displayed text.** Tooltips, hints and labels are translatable resources ([localization](15-localization.md)); their wording changes. Match on ids and enum values, never on text.
- **What a frame looks like.** Geometry detail, colors and shading are presentation, tuned over time.
- **Ordering** that isn't documented as ordered. Where order matters it is stated (`GetBerths()` returns berths in the order they were added).
- **`[JSInvokable]` members of the Blazor components** (`OnPointerMove`, `OnAnimationFrame`, …). They are public only because the JavaScript interop layer requires it; they are plumbing between the component and its own script.
- **Anything `internal`**, and the exact text of exception messages.

### Deprecation

A member that has to go is marked `[Obsolete]` with a message naming the replacement, and keeps working for the rest of the major line. It is removed only in the next major version. Nothing is deleted without a release that warned about it first.

## How the promise is enforced

`PublicApiTests` writes out the full public surface of each shipped assembly and compares it against a baseline checked in beside it:

```
tests/VirtualMarina.Core.Tests/ApiBaselines/VirtualMarina.Core.approved.txt
tests/VirtualMarina.Core.Tests/ApiBaselines/VirtualMarina.Blazor.approved.txt
tests/VirtualMarina.Core.Tests/ApiBaselines/VirtualMarina.Rendering.OpenGL.approved.txt
tests/VirtualMarina.WinForms.Tests/ApiBaselines/VirtualMarina.WinForms.approved.txt
```

Any change to the API fails the build with a diff of the lines added and removed:

```
The public API of VirtualMarina.Core has changed.

- void AssignBoat(string berthId, Boat boat)
+ void AssignBoat(string berthId, Boat boat, bool notify)
```

That failure is the point at which somebody decides. If the change only **adds**, approve it by replacing the `.approved.txt` file with the `.received.txt` the test wrote next to it, and commit both the code and the new baseline. If it removes or changes something, it does not belong in this major version.

The baselines also record every enum number and `const` value, because those are compiled into the consumer's own assembly: an application built against `MaxDimension = 8192` keeps using 8192 until it is rebuilt, and a berth status stored in a host database as `2` must still mean `Reserved` next year.

## Adding things without breaking anything

| To add | Do this | Not this |
|---|---|---|
| Data on a domain record | A new `init` property with a default | A new positional parameter on the record |
| An argument to a method | A new **optional** parameter at the end, or an overload | A new required parameter |
| A value to an enum | Append it with the next free number | Insert it in the middle, or renumber |
| A member to `ISceneRenderer` | Give it a default implementation | A plain abstract member |
| A member to `IMarinaVisualizer` | Allowed in a minor release — see below | — |
| A section or field in a marina file | An optional field, and bump the format's minor version | Change what an existing field means |

A few rules behind the table:

- **Records are extended, never re-shaped.** `Berth`, `Pier`, `Divider`, `LandArea` and `MultiBerth` are `sealed record`s whose data is `init`-only. Adding a property is invisible to existing code; adding a constructor parameter is not. Every one of them carries a `Metadata` dictionary for host-owned strings (saved with the design) and berths additionally carry `ExternalData` for live host objects (never saved), so an integration usually has somewhere to put its own data without the library changing at all.
- **Optional parameters are baked into the caller.** Adding one is source-compatible and, for practical purposes, binary-compatible; *changing a default value* is not — the old default stays compiled into applications until they rebuild, so two versions would disagree. Treat a default as published.
- **`ISceneRenderer` is implemented outside the library**, by whoever writes a graphics backend. Everything added to it therefore has a default implementation, as `DeviceDescription` does, so an existing backend keeps compiling untouched.
- **`IMarinaVisualizer` is not meant to be implemented outside the library.** It exists so host code can be written and tested against an abstraction; `MarinaVisualizer` is the implementation. Members are added to it in minor releases. A mocking library handles that by itself; a hand-written implementation does not, which is why it isn't supported.
- **Renaming is removing.** Even a typo fix in a public name is a break; it waits for a major version, with the old name kept and `[Obsolete]` in between.

## Integrating with confidence

For a host application, the shortest path to an integration that survives updates:

1. **Depend on `MarinaVisualizer` (or `IMarinaVisualizer`) and the domain records.** They are the stable core.
2. **Key everything by id.** Berth, pier and land-area ids are yours to choose and are never reinterpreted. `Berth.Metadata` and `Berth.ExternalData` let you attach your own references instead of keeping a parallel map.
3. **Switch on enum values with a default case.** New members are appended in minor releases; `_ =>` keeps that from throwing.
4. **Load designs through `MarinaDocument`**, which already reads older and newer files ([file format](13-marina-file-format.md)), instead of parsing the JSON yourself.
5. **Don't implement `IMarinaVisualizer` or parse displayed text.** Those are the two habits that make updates painful.
