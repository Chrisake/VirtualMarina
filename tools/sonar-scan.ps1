<#
.SYNOPSIS
    Builds, tests and analyses the solution, then uploads the result to SonarQube Cloud.

.DESCRIPTION
    The same three steps CI runs (.github/workflows/static-analysis.yml), so a finding can be seen
    and fixed before it is pushed. The .NET and SonarAnalyzer rules compiled in by
    Directory.Build.props already run on every ordinary build; this script adds the server-side
    rules, the coverage report and the quality gate.

    Needs a SonarQube Cloud token in $env:SONAR_TOKEN, from sonarcloud.io -> My Account -> Security,
    and a Java 17 or newer runtime for the scanner itself.

.PARAMETER ProjectKey
    The SonarQube Cloud project key. Defaults to the key SonarQube Cloud gives a project imported
    from the GitHub repository.

.PARAMETER Organization
    The SonarQube Cloud organization key.

.PARAMETER Configuration
    Debug or Release. Release matches CI; Debug is faster for a quick look.

.EXAMPLE
    $env:SONAR_TOKEN = "..."
    ./tools/sonar-scan.ps1
#>
[CmdletBinding()]
param(
    [string]$ProjectKey = 'Chrisake_VirtualMarina',
    [string]$Organization = 'chrisake',
    [string]$HostUrl = 'https://sonarcloud.io',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    if (-not $env:SONAR_TOKEN) {
        throw "SONAR_TOKEN is not set. Create a token at $HostUrl (My Account -> Security), then: `$env:SONAR_TOKEN = '<token>'"
    }

    # The scanner is a Java program. Java 8 is still a common thing to have installed on Windows and
    # it fails here with a class-file version error that says nothing about the real cause, so check
    # the version first and say what is wrong.
    $javaHome = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { 'java' }
    $javaVersion = (& $javaHome -version 2>&1 | Select-Object -First 1) -replace '.*"([^"]+)".*', '$1'
    $javaMajor = if ($javaVersion -match '^1\.(\d+)') { [int]$Matches[1] } else { [int]($javaVersion -split '\.')[0] }
    if ($javaMajor -lt 17) {
        throw "The SonarQube scanner needs Java 17 or newer; found $javaVersion. Install a JDK 17+ (winget install EclipseAdoptium.Temurin.17.JDK) and point JAVA_HOME at it."
    }

    # TestResults accumulates a new GUID-named directory per run, and the scanner is pointed at all
    # of them by wildcard. Clearing them keeps the coverage report to this run's numbers.
    Get-ChildItem -Path 'tests' -Directory -Filter 'TestResults' -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    # begin, build, end have to bracket one build: the scanner reads what the compiler wrote during
    # it, so a --no-build or an up-to-date incremental build would hand it nothing to analyse.
    dotnet tool run dotnet-sonarscanner begin `
        /k:"$ProjectKey" `
        /o:"$Organization" `
        /d:sonar.token="$env:SONAR_TOKEN" `
        /d:sonar.host.url="$HostUrl" `
        /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml" `
        /d:sonar.coverage.exclusions="apps/VirtualMarina.Designer/**/*,samples/VirtualMarina.TestHost.*/**/*,src/VirtualMarina.WinForms/**/*,src/VirtualMarina.Rendering.OpenGL/**/*,src/VirtualMarina.Blazor/**/*,**/wwwroot/**/*,**/Resources/Strings.*,**/*.Designer.cs" `
        /d:sonar.scanner.scanAll=false
    if ($LASTEXITCODE -ne 0) { throw 'sonarscanner begin failed.' }

    dotnet build VirtualMarina.sln --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; the analysis would be incomplete.' }

    dotnet test VirtualMarina.sln --configuration $Configuration --no-build `
        --collect:"XPlat Code Coverage;Format=opencover"
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Tests failed. Continuing so the analysis still reports, but the coverage figure is not trustworthy.' }

    dotnet tool run dotnet-sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
    if ($LASTEXITCODE -ne 0) { throw 'sonarscanner end failed.' }

    Write-Host ''
    Write-Host "Analysis uploaded. Results: $HostUrl/project/overview?id=$ProjectKey" -ForegroundColor Green
}
finally {
    Pop-Location
}
