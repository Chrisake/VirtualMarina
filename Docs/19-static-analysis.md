# Static analysis

Two analysers read this code, and they are set up to agree with each other. Both run on every ordinary build, in Visual Studio and on the command line alike, so a finding looks the same wherever you meet it: in the error list while typing, in `dotnet build` output, and in the SonarQube Cloud report on a pull request.

| Analyser | Rules | Where it runs |
|---|---|---|
| **.NET analyzers** (the SDK's, what Visual Studio calls *code analysis*) | `CAxxxx` | Every build. Turned on by `Directory.Build.props` |
| **SonarAnalyzer.CSharp** | `Sxxxx` | Every build, as a NuGet analyzer package referenced by every project (a `GlobalPackageReference` in `Directory.Packages.props`). The same engine SonarQube Cloud runs server-side |
| **SonarQube Cloud** | The above, plus server-side rules, duplication, coverage and the quality gate | CI, and `tools/sonar-scan.ps1` on demand |

Referencing SonarAnalyzer locally is the point of the arrangement. Without it, Sonar findings only appear after a push, which is the most expensive moment to learn about them. With it, the CI report holds no surprises.

## Reading a finding

Findings are **errors**. Every compiler warning, every `CA` and every `S` fails the build, at the
highest warning level the compiler offers (`WarningLevel` 9999, so warnings added by a future SDK
arrive switched on rather than silently off). The backlog is zero and this is what keeps it there.

That is a strong setting, so it comes with an obligation: when a rule does not fit this codebase,
switch it off **by name in `.editorconfig` with its reason**, and never reach for a blanket
suppression or a `<NoWarn>` of convenience. The list of what is off, and why, is below.

Each rule number links to its explanation: `CA` rules at [learn.microsoft.com](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/), `S` rules at [rules.sonarsource.com/csharp](https://rules.sonarsource.com/csharp/).

## Which rules are on

The .NET analyzers run at `8.0-all`, the whole catalogue, rather than the small default set. The level is pinned to the .NET 8 catalogue rather than `latest`: `global.json` lets the build roll forward to any 8.0.x SDK, and with every finding an error, a rule added by a newer SDK band would otherwise fail a build whose code had not changed. Raising the level is a deliberate change, made together with the fixes it asks for. The default set finds nothing here — the code was already clean against it — so every interesting rule is in the part that is off by default.

The whole catalogue also contains rules that are about a different kind of program than this one. Those are switched off **by name** in `.editorconfig`, each with its reason next to it. The four that mattered:

| Rule | Findings | Why it is off |
|---|---|---|
| `CA1707` identifiers should not contain underscores | 258 | Test names are `Method_Scenario_Expectation`. The underscores are the convention, not an accident. Off in `tests/` only |
| `CA2213` disposable field is never disposed | 121 | A WinForms control added to a `Controls` collection is disposed by the form that owns it. The analyzer cannot see that ownership transfer |
| `CA5394` `Random` is insecure | 85 | Berths, hulls and fleets are generated procedurally and deliberately seedable so a layout reproduces. No secret is guarded by it |
| `CA1303` pass literals as localized strings | 7 | VirtualMarina localizes through its own resource layer ([localization](15-localization.md)), not the `LocalizableAttribute` convention this rule looks for |

Some suppressions cannot go in `.editorconfig`: Roslyn resolves analyzer configuration for a `.razor` file through the Razor source generator rather than through the file's own path, so no `.editorconfig` section reaches it. The Blazor apps that hit rules this way (`CA5394` for decorative randomness, `S5693` for HTTP request limits that do not apply in a browser) switch them off with `<NoWarn>` in their own project file, with the same reason written there: `samples/VirtualMarina.TestHost.Blazor` and `apps/VirtualMarina.Designer.Blazor`.

Switching a rule off is a decision that has to be written down. If you add one, add the reason with it: a bare suppression is worse than the warning it silences, because the next person cannot tell whether it was reasoned about or just noisy that day.

That tuning takes the catalogue from 662 findings to 216, and those 216 were then worked through:
86 were fixed and the rest were rules this codebase deliberately does not follow, switched off by
name with the reason beside each one. The build now carries **no warnings at all**.

What the fixes changed:

| Area | What was done |
|---|---|
| **Correctness** | `LayoutChangeKind.PierRenamed` and `ShorelineChanged` both had the value 19, so a host could not tell a pier rename from a shoreline change. `PierRenamed` moved to 21 |
| **Stability** | `ArgumentNullException.ThrowIfNull` at six public entry points that dereferenced an argument without checking it; the standard constructors added to `MarinaLayoutException` and `MarinaFormatException`; the designer and test host now dispose their main form |
| **Performance** | 15 private helpers returning or taking an interface where the concrete type avoids the dispatch; `BerthIds` and `ActionableBerths` build once and cache instead of allocating an array per read; three `FirstOrDefault` calls on indexable lists replaced by indexing |
| **Maintainability** | 19 null-forgiving `!` operators the compiler did not need; seven unused locals, two write-only fields and an unused helper removed; a duplicate `RequestPopupRefresh` body; parameter names aligned with the interface and base class they implement; a token scanner rewritten as the `while` loop it always was |

### What the remaining rules are, and why they are off

| Rule | Hits | Why |
|---|---|---|
| `S3358` nested ternary | 47 | A chained ternary, one condition per line, is how this codebase writes a multi-way expression |
| `S3218` shadowing | 19 | `SceneBuilder` keeps 19 shorthand properties over the thread's `Palette` so geometry code stays terse |
| `CA1305` culture | 11 | Test hosts print numbers for a person to read, so they should follow that person's locale |
| `CA2007` / `CA1849` | 9 | Blazor WebAssembly is single-threaded and its components need their synchronisation context |
| `CA1508` dead condition | 7 | The dataflow cannot follow a variable assigned inside an event-handler lambda |
| `S1244` float equality | 6 | Every hit is a "did this value change?" guard, where a tolerance would drop real changes |
| `S4136`, `S2365`, `S3267`, `CA1819`, `CA1700`, `CA1710`, `CA1720`, `CA1721` | 21 | Public naming this domain owns, arrays handed to the GPU, and LINQ that would allocate in per-frame loops |

`CA1812` was the last finding standing, and it was right: `AppearanceForm` was a whole dialog
nothing opened, superseded by `AppearancePanel`. It is deleted rather than suppressed.
`TextInputForm`, which shared the file and is used, moved to `TextInputForm.cs`.

Two findings did not belong in `.editorconfig` because they are one site each, so they carry a
`#pragma` with the reason next to the code instead: the deliberate `b`/`c` swap that reverses a
triangle's winding in `MeshBuilder`, and the `FontFamily` in `FontCapture` that a `using` takes over
on the following line.

### Code style

The style rules are enforced too, and they state what the code already does rather than what a
default thinks it should. The brace rule is the clearest case: `csharp_prefer_braces` is
`when_multiline`, because that is the convention -- 739 single-line guards against 248 multi-line
bodies -- so the 912 findings it used to report were the rule disagreeing with the house style, not
912 inconsistencies. Set correctly it reports 2, and both were real.

`.editorconfig` deliberately sets no `charset`. The repository is mixed about the UTF-8 byte-order
mark, roughly two thirds of the .cs files carrying one, and declaring either answer would make
`dotnet format` rewrite the encoding of every file that disagrees. That one is left for a
deliberate decision rather than settled as a side effect.

### Kept at zero

Already done. `TreatWarningsAsErrors` and `CodeAnalysisTreatWarningsAsErrors` are both true in
`Directory.Build.props`, so a new finding fails the build rather than scrolling past in the log.

To check the wiring is live, add an unused private field to any file and build: it should come back
as `CS0414`, `CA1823` and `S1144`, all three as errors.

## Coverage

`dotnet test` collects line coverage with [coverlet](https://github.com/coverlet-coverage/coverlet) in **OpenCover** format, because that is a format SonarQube's C# plugin reads — the Cobertura it produces by default is not:

```
dotnet test VirtualMarina.sln --collect:"XPlat Code Coverage;Format=opencover"
```

(Windows. Elsewhere, run it per test project, leaving out `VirtualMarina.WinForms.Tests`, as the script does.)

Each test project writes `tests/<project>/TestResults/<guid>/coverage.opencover.xml`, and the scanner is pointed at all of them by wildcard. The reports overlap (every test project exercises `VirtualMarina.Core`); Sonar merges them, so its figure is higher than any one alone. The figure deliberately leaves out what unit tests cannot reach — the WinForms and OpenGL assemblies, the apps, the test hosts, browser JavaScript and generated resource code — as listed in `tools/sonar-scan.ps1`. The current figure is on the SonarQube Cloud project page; it is not repeated here, where it would only go stale.

Note the `<guid>` in that path: a run adds a directory rather than replacing one. `tools/sonar-scan.ps1` clears `TestResults` first, so the report carries this run's numbers and not an accumulation.

## Running the SonarQube analysis

### In CI

Two workflows share the work, so that a change can always be built and tested, whoever made it:

| Workflow | What it runs | Needs a secret |
|---|---|---|
| `.github/workflows/ci.yml` | Build and tests on Linux, Windows and macOS (the WinForms tests on Windows), GLSL compilation of every shader, the trimmed Blazor publish, the API reference check, and ESLint/TypeScript over the shipped JavaScript. See [continuous integration](#continuous-integration-and-releases) | No |
| `.github/workflows/static-analysis.yml` | The SonarQube Cloud analysis, by running `tools/sonar-scan.ps1` | `SONAR_TOKEN` |

The analysis runs on `windows-latest` because `VirtualMarina.WinForms` and `VirtualMarina.Designer` target `net8.0-windows` and their tests only run there. It is skipped, not failed, when the token is not available to the run, which is always the case for a pull request from a fork; and a SonarQube outage fails only that workflow, never the build and tests in `ci.yml`.

| Secret | Where it comes from |
|---|---|
| `SONAR_TOKEN` | sonarcloud.io, My Account, Security, Generate Token |

The checkout uses `fetch-depth: 0` on purpose. Sonar dates each issue from the commit that introduced it, which is how *new code* is separated from the existing backlog in the quality gate. A shallow clone gives it one commit, and every issue looks new.

### Locally

```powershell
$env:SONAR_TOKEN = "your token"
./tools/sonar-scan.ps1
```

The very script CI runs, so a finding can be fixed before it is pushed, and the scanner settings (project key, coverage exclusions) exist in one place only. It runs under PowerShell 7 on Windows, Linux or macOS, and under Windows PowerShell 5.1; off Windows the `net8.0-windows` projects are still compiled and analysed, but only the cross-platform tests contribute coverage. Two things it needs:

- **Java 17 or newer.** The scanner is a Java program. A Java 8 runtime — still common on Windows — fails with a class-file version error that says nothing about the real cause, so the script checks first and says so plainly. `winget install EclipseAdoptium.Temurin.17.JDK`, then point `JAVA_HOME` at it.
- **`begin`, build, `end` bracketing one real build.** The scanner reads what the compiler wrote while it was watching. A `--no-build`, or an incremental build that turns out to be up to date, hands it nothing and produces an empty report that looks like a clean one.

The scanner itself is pinned in `.config/dotnet-tools.json`, so `dotnet tool restore` gets the same version everywhere. It is not installed globally.

### First-time setup

The project key and organization default to what SonarQube Cloud assigns a project imported from the GitHub repository: `Chrisake_VirtualMarina` in the `chrisake` organization. If the project is created by hand and given different ones, change the defaults of `tools/sonar-scan.ps1` (CI uses them too), or pass them: `./tools/sonar-scan.ps1 -ProjectKey ... -Organization ...`.

## Tests

| Project | Target | What it covers |
|---|---|---|
| `tests/VirtualMarina.Core.Tests` | net8.0 | The visualizer API, events, picking, camera, geometry, the designer, the file format, shaders; the API baselines of `VirtualMarina.Core` and `VirtualMarina.Rendering.OpenGL` |
| `tests/VirtualMarina.Blazor.Tests` | net8.0 | `<MarinaView>`, `<MarinaDesignerPanel>` and `WebGlSceneRenderer`, against a fake JavaScript runtime; the `VirtualMarina.Blazor` API baseline |
| `tests/VirtualMarina.Designer.Common.Tests` | net8.0 | What both designer apps share: the session, the command table, renaming, camera names, the launcher's liveness tracking |
| `tests/VirtualMarina.WinForms.Tests` | net8.0-windows | `MarinaViewControl` (events, keys) and `MarinaDesignerPanel`; the `VirtualMarina.WinForms` API baseline. Windows only |
| `tests/VirtualMarina.TestSupport` | net8.0 | Not a test project: the API baseline comparer, a fixed clock and the repository-root lookup the others share |

**Adding a test project.** `tests/Directory.Build.props` treats every project under `tests/` whose name ends in `.Tests` as a test project and gives it xUnit, the runner, the test SDK and coverlet, with `Xunit` imported globally, so a new one needs only its `TargetFramework` and its references. Any other project there (like `TestSupport`) gets none of it. Package versions come from `Directory.Packages.props` (below): a `PackageReference` never carries a `Version`. CI finds test projects by the `tests/*.Tests/*.Tests.csproj` pattern, so a new one runs on every OS without a workflow change; one that can only run on Windows needs the same exclusion `VirtualMarina.WinForms.Tests` has in `ci.yml` and `tools/sonar-scan.ps1`.

**API baselines.** Each `PublicApiTests` writes the public surface of a shipped assembly and compares it with the `ApiBaselines/<assembly>.approved.txt` checked in beside the test. On a difference it writes `<assembly>.received.txt` next to it and fails with the lines added and removed. To approve an intended change, review the `.received.txt` file, rename it over the `.approved.txt`, and commit it with the code; what may change at all is set out in [compatibility](16-compatibility.md). The WinForms baseline can only be regenerated on Windows.

**Property-based tests.** `PropertyTests` uses [CsCheck](https://github.com/AnthonyLloyd/CsCheck): each property is checked against a few hundred random inputs (the file format round trip, polygon math, naming), and a failure is shrunk to the smallest input that still fails and printed with its seed. Pass that seed to `Sample(seed: ...)` to replay it.

**Golden files.** `tests/VirtualMarina.Core.Tests/Fixtures/format-<version>/` holds saved marina files of every format version, and `GoldenFileTests` checks that each still loads to the same marina. They are never regenerated to make a test pass: a failure means a change broke reading an existing file. When the format moves to a new version, add a folder for it; `VM_WRITE_FIXTURES=1` makes `CurrentVersionFixtures_CanBeWritten` write the current-version files into the source tree:

```bash
VM_WRITE_FIXTURES=1 dotnet test tests/VirtualMarina.Core.Tests --filter CurrentVersionFixtures_CanBeWritten
```

**Shaders.** The unit tests only look for substrings in the GLSL, which a shader that does not compile would pass. `ShaderDumpTests` therefore writes every shader `ShaderSources` can produce, in both dialects, to `.vert`/`.frag` files when `VM_SHADER_DUMP_DIR` names a directory, and CI compiles each one with `glslangValidator`. To do the same locally: `VM_SHADER_DUMP_DIR=/tmp/shaders dotnet test tests/VirtualMarina.Core.Tests --filter ShaderDumpTests`, then `glslangValidator` on each file.

## Continuous integration and releases

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, forks included: nothing in it needs a secret.

| Job or step | Where | What it checks |
|---|---|---|
| Build and test | Ubuntu, Windows, macOS | The whole solution in Release (`-p:EnableWindowsTargeting=true`, so Linux and macOS compile the `net8.0-windows` projects too), then every test project that can run there; the WinForms tests on Windows. Results are uploaded as `.trx` files |
| Shader compilation | Ubuntu | Every shader dumped by the tests (`VM_SHADER_DUMP_DIR`), compiled by `glslangValidator` |
| Trimmed publish | Ubuntu | The Blazor test host published trimmed with trim analysis on; the trim warnings in VirtualMarina's own code are listed in the job summary. They fail the job once `TRIM_WARNINGS_AS_ERRORS` in the workflow is switched to `'true'` |
| API reference | Windows | `Docs/11-api-reference.md` regenerated by `Docs/tools/ApiDocGen` and compared with the committed copy. After an API change, run `dotnet run --project Docs/tools/ApiDocGen` on Windows (it loads the WinForms assembly) and commit the result |
| JavaScript | Ubuntu, Node 22 | ESLint and the TypeScript checker (`checkJs`) over the JavaScript shipped to the browser: the WebGL renderer and the Blazor designer's helpers |

The JavaScript checks run locally with Node 20 or newer, from the repository root:

```bash
npm ci && npx eslint . && npx tsc -p jsconfig.json      # or: npm ci && npm run check
```

`.github/workflows/release.yml` runs when a `v*` tag is pushed (or by hand). On Windows it builds and tests the solution, then packs the four libraries — `VirtualMarina.Core`, `VirtualMarina.Rendering.OpenGL`, `VirtualMarina.WinForms` and `VirtualMarina.Blazor` — with their symbol packages, and uploads them as a workflow artifact. They are pushed to nuget.org only when the repository has a `NUGET_API_KEY` secret and the run is for a tag, so a fork or a dry run still produces packages to inspect.

**Versions** come from git tags through [MinVer](https://github.com/adamralph/minver): tagging a commit `v1.2.3` makes every assembly and package built from it 1.2.3; commits after the tag build as a pre-release such as `1.2.4-alpha.0.5`. `AssemblyVersion` stays `<major>.0.0.0`, which is the binary-compatibility policy of [compatibility](16-compatibility.md). A checkout without tags builds as `1.0.0-alpha.0.N`; a build outside a git working tree can pass `-p:MinVerVersionOverride=1.2.3`. Only the libraries are packable (`src/Directory.Build.props`), licensed `GPL-3.0-only`.

**Packages** are versioned centrally: `Directory.Packages.props` sets every NuGet version once (Central Package Management), and a project's `PackageReference` names the package without a version — a `Version` attribute there is an error (NU1008). SonarAnalyzer.CSharp is a `GlobalPackageReference`, so every project gets it without asking. Dependabot (`.github/dependabot.yml`) proposes updates weekly for NuGet, GitHub Actions and npm, grouped by ecosystem; the ASP.NET Core packages move together and stay on the 8.0 band.

## Where the settings live

| File | What it decides |
|---|---|
| `Directory.Build.props` | Which analysers run, at what level, and that every finding is an error |
| `Directory.Packages.props` | The version of every package, the SonarAnalyzer one included |
| `tests/Directory.Build.props` | What makes a project under `tests/` a test project, and what it gets |
| `.editorconfig` | The severity of individual rules, and the code style the analyzers enforce |
| `.config/dotnet-tools.json` | The pinned scanner version |
| `.github/workflows/ci.yml` | The build and tests on every OS, shader compilation, trim analysis, API reference and JavaScript checks |
| `.github/workflows/static-analysis.yml` | The SonarQube Cloud analysis in CI |
| `.github/workflows/release.yml` | Packing the libraries on a `v*` tag, and publishing them to NuGet |
| `tools/sonar-scan.ps1` | The scanner settings and the analysis itself, run by CI and by hand |
| `eslint.config.js`, `jsconfig.json`, `package.json` | Lint and type-check rules for the browser JavaScript, and the pinned tool versions |
