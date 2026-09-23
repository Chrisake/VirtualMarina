# Static analysis

Two analysers read this code, and they are set up to agree with each other. Both run on every ordinary build, in Visual Studio and on the command line alike, so a finding looks the same wherever you meet it: in the error list while typing, in `dotnet build` output, and in the SonarQube Cloud report on a pull request.

| Analyser | Rules | Where it runs |
|---|---|---|
| **.NET analyzers** (the SDK's, what Visual Studio calls *code analysis*) | `CAxxxx` | Every build. Turned on by `Directory.Build.props` |
| **SonarAnalyzer.CSharp** | `Sxxxx` | Every build, as a NuGet analyzer package. The same engine SonarQube Cloud runs server-side |
| **SonarQube Cloud** | The above, plus server-side rules, duplication, coverage and the quality gate | CI, and `tools/sonar-scan.ps1` on demand |

Referencing SonarAnalyzer locally is the point of the arrangement. Without it, Sonar findings only appear after a push, which is the most expensive moment to learn about them. With it, the CI report holds no surprises.

## Reading a finding

Findings are **warnings, not errors**. The build stays green while the backlog is worked down; nobody is blocked by a rule they disagree with. The exception is the documentation warnings the shipped libraries already treat as errors (`CS1591` and friends, see `src/Directory.Build.props`) — an undocumented public member is still a build failure.

Each rule number links to its explanation: `CA` rules at [learn.microsoft.com](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/), `S` rules at [rules.sonarsource.com/csharp](https://rules.sonarsource.com/csharp/).

## Which rules are on

The .NET analyzers run at `latest-all`, the whole catalogue, rather than the small default set. The default set finds nothing here — the code was already clean against it — so every interesting rule is in the part that is off by default.

The whole catalogue also contains rules that are about a different kind of program than this one. Those are switched off **by name** in `.editorconfig`, each with its reason next to it. The four that mattered:

| Rule | Findings | Why it is off |
|---|---|---|
| `CA1707` identifiers should not contain underscores | 258 | Test names are `Method_Scenario_Expectation`. The underscores are the convention, not an accident. Off in `tests/` only |
| `CA2213` disposable field is never disposed | 121 | A WinForms control added to a `Controls` collection is disposed by the form that owns it. The analyzer cannot see that ownership transfer |
| `CA5394` `Random` is insecure | 85 | Berths, hulls and fleets are generated procedurally and deliberately seedable so a layout reproduces. No secret is guarded by it |
| `CA1303` pass literals as localized strings | 7 | VirtualMarina localizes through its own resource layer ([localization](15-localization.md)), not the `LocalizableAttribute` convention this rule looks for |

One suppression could not go in `.editorconfig`: Roslyn resolves analyzer configuration for a `.razor` file through the Razor source generator rather than through the file's own path, so no `.editorconfig` section reaches it. The single `CA5394` in `App.razor` is switched off with `<NoWarn>` in `samples/VirtualMarina.TestHost.Blazor/VirtualMarina.TestHost.Blazor.csproj`, with the same reason written there.

Switching a rule off is a decision that has to be written down. If you add one, add the reason with it: a bare suppression is worse than the warning it silences, because the next person cannot tell whether it was reasoned about or just noisy that day.

That tuning takes the catalogue from 662 findings to 216. Those 216 were then worked through: 86
were fixed, and the rest were rules this codebase deliberately does not follow, switched off by name
with the reason beside each one. **One warning is left**, and it is a question rather than a defect:
`CA1812` says `AppearanceForm` is never instantiated, which is true — it is a whole form nothing
opens. Deleting it or wiring it up is a product decision, so the warning stays until it is made.

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

Two findings did not belong in `.editorconfig` because they are one site each, so they carry a
`#pragma` with the reason next to the code instead: the deliberate `b`/`c` swap that reverses a
triangle's winding in `MeshBuilder`, and the `FontFamily` in `FontCapture` that a `using` takes over
on the following line.

### Tightening later

The backlog is one warning away from zero. Once `CA1812` is settled, set
`CodeAnalysisTreatWarningsAsErrors` to `true` in `Directory.Build.props` and it cannot come back:
a new finding then fails the build rather than scrolling past in the log.

## Coverage

`dotnet test` collects line coverage with [coverlet](https://github.com/coverlet-coverage/coverlet) in **OpenCover** format, because that is a format SonarQube's C# plugin reads — the Cobertura it produces by default is not:

```
dotnet test VirtualMarina.sln --collect:"XPlat Code Coverage;Format=opencover"
```

Each test project writes `tests/<project>/TestResults/<guid>/coverage.opencover.xml`, and the scanner is pointed at all of them by wildcard. The two reports overlap (both exercise `VirtualMarina.Core`); Sonar merges them, so its figure is higher than either alone. The current baseline across 302 tests is roughly half the sequence points in the shipped assemblies.

Note the `<guid>` in that path: a run adds a directory rather than replacing one. `tools/sonar-scan.ps1` clears `TestResults` first, so the report carries this run's numbers and not an accumulation.

## Running the SonarQube analysis

### In CI

`.github/workflows/static-analysis.yml` builds, tests and analyses every push to `main` and every pull request. It runs on `windows-latest` because `VirtualMarina.WinForms` and `VirtualMarina.Designer` target `net8.0-windows`; on Linux they would not build and the analysis would quietly cover less than it appears to.

It needs one repository secret:

| Secret | Where it comes from |
|---|---|
| `SONAR_TOKEN` | sonarcloud.io, My Account, Security, Generate Token |

The checkout uses `fetch-depth: 0` on purpose. Sonar dates each issue from the commit that introduced it, which is how *new code* is separated from the existing backlog in the quality gate. A shallow clone gives it one commit, and every issue looks new.

### Locally

```powershell
$env:SONAR_TOKEN = "your token"
./tools/sonar-scan.ps1
```

The same three steps as CI, so a finding can be fixed before it is pushed. Two things it needs:

- **Java 17 or newer.** The scanner is a Java program. A Java 8 runtime — still common on Windows — fails with a class-file version error that says nothing about the real cause, so the script checks first and says so plainly. `winget install EclipseAdoptium.Temurin.17.JDK`, then point `JAVA_HOME` at it.
- **`begin`, build, `end` bracketing one real build.** The scanner reads what the compiler wrote while it was watching. A `--no-build`, or an incremental build that turns out to be up to date, hands it nothing and produces an empty report that looks like a clean one.

The scanner itself is pinned in `.config/dotnet-tools.json`, so `dotnet tool restore` gets the same version everywhere. It is not installed globally.

### First-time setup

The project key and organization default to what SonarQube Cloud assigns a project imported from the GitHub repository: `Chrisake_VirtualMarina` in the `chrisake` organization. If the project is created by hand and given different ones, change them in the workflow and in the defaults of `tools/sonar-scan.ps1` — or pass them: `./tools/sonar-scan.ps1 -ProjectKey ... -Organization ...`.

## Where the settings live

| File | What it decides |
|---|---|
| `Directory.Build.props` | Which analysers run, at what level, and that findings are warnings |
| `.editorconfig` | The severity of individual rules, and the code style the analyzers enforce |
| `.config/dotnet-tools.json` | The pinned scanner version |
| `.github/workflows/static-analysis.yml` | The CI build, test and analysis |
| `tools/sonar-scan.ps1` | The same, run by hand |
