<#
.SYNOPSIS
Builds a HikariHopper release: the Native AOT win-x64 build, checked on the native binary, packed to update itself.

.DESCRIPTION
Publishes with Native AOT and fails on any AOT compiler warning (these cover FiveFury.NET too, which the build's
analyzers don't see). Then runs the app's --self-test against a GTA V installation and starts the app until its window
appears. Velopack (vpk, pinned in dotnet-tools.json) then packs it: artifacts\HikariHopper-<version>-win-x64.zip, the
zip people download, which updates itself, and in artifacts\HikariHopper-<version>-win-x64-updates the files the app's
updater reads from the GitHub release (the feed and the packages). The script ends with the command that publishes them.

.EXAMPLE
pwsh tools/release.ps1 -GameRoot "D:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
#>
param(
    # A GTA V installation for the self-test; defaults to HIKARI_TEST_LEGACY, then to the folder configured in the app.
    [string]$GameRoot = $env:HIKARI_TEST_LEGACY,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\HikariHopper.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "HikariHopper-$version-win-x64"
$build = Join-Path $Output $name
New-Item -ItemType Directory -Force $Output | Out-Null
if (Test-Path $build) { Remove-Item $build -Recurse -Force }

# The AOT linker step finds MSVC through vswhere.exe, which the Visual Studio installer doesn't put on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }

Write-Host "Publishing $name (Native AOT)..."
# Marked as a release build, which hides the changelog's Unreleased notes; any other build is a dev build that shows them.
$log = & dotnet publish $project -c Release -r win-x64 -p:PublishReferencesDocumentationFiles=false -p:HikariHopperRelease=true -o $build 2>&1
if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw 'Publish failed' }
$warnings = $log | Select-String 'warning IL\d+' | Sort-Object -Unique
if ($warnings) { $warnings | Write-Host; throw 'The AOT compiler reported warnings; any of them can fail at runtime' }
# Third-party notices are shown in the app instead; debug symbols aren't shipped.
Remove-Item (Join-Path $build 'licenses') -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $build -Filter *.pdb | Remove-Item

# Nothing shipped may name this machine's folders, such as a PDB path in a binary's debug directory.
$private = @($env:USERPROFILE, (Resolve-Path (Join-Path $PSScriptRoot '..')).Path) | Where-Object { $_ }
function Assert-NoPrivatePaths([string]$folder) {
    # Empty files (the zip's .portable marker) hold nothing to find.
    $leaks = foreach ($file in Get-ChildItem $folder -Recurse -File | Where-Object Length -gt 0) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        # Latin-1 for 8-bit strings; UTF-16 read from both even and odd offsets.
        $texts = [Text.Encoding]::Latin1.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes),
            [Text.Encoding]::Unicode.GetString($bytes, 1, [Math]::Max(0, $bytes.Length - 1))
        foreach ($path in $private) {
            if ($texts | Where-Object { $_.IndexOf($path, [StringComparison]::OrdinalIgnoreCase) -ge 0 }) { "$($file.Name) contains $path" }
        }
    }
    if ($leaks) { $leaks | Write-Host; throw 'The build contains private paths' }
}
Assert-NoPrivatePaths $build
# No Markdown ships with a release.
$markdown = Get-ChildItem $build -Recurse -File -Filter *.md
if ($markdown) { $markdown | ForEach-Object { Write-Host $_.FullName }; throw 'The build contains Markdown files' }

$exe = Join-Path $build 'HikariHopper.exe'
Write-Host 'Running the self-test on the native build...'
$report = Join-Path $Output 'self-test.txt'
$arguments = @('--self-test')
# Backslashes before the closing quote are doubled, or a path ending in \ would escape it.
if ($GameRoot) { $arguments += '"' + ($GameRoot -replace '(\\+)$', '$1$1') + '"' }
# Start-Process and WaitForExit, because PowerShell doesn't wait for a GUI exe started with & and would read a stale exit code.
$test = Start-Process $exe -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $report
if (-not $test.WaitForExit(120000)) { $test.Kill(); $test.WaitForExit(); Get-Content $report | Write-Host; throw 'The self-test did not finish within two minutes' }
Get-Content $report | Write-Host
if ($test.ExitCode -ne 0) { throw 'Self-test failed' }
if (-not (Select-String -Path $report -Pattern '^Self-test passed$' -Quiet)) { throw 'The self-test reported nothing' }

Write-Host 'Starting the app...'
$app = Start-Process $exe -PassThru
$deadline = (Get-Date).AddSeconds(20)
do {
    Start-Sleep -Milliseconds 200
    $app.Refresh()
} until ($app.HasExited -or $app.MainWindowTitle -like 'HikariHopper*' -or (Get-Date) -gt $deadline)
if ($app.HasExited) { throw "The app exited at startup with code $($app.ExitCode)" }
if ($app.MainWindowTitle -notlike 'HikariHopper*') { $app.Kill(); throw 'The app showed no window within 20 seconds' }
Write-Host "Window: $($app.MainWindowTitle)"
[void]$app.CloseMainWindow()
if (-not $app.WaitForExit(5000)) { $app.Kill(); $app.WaitForExit() }

# The version's changelog section. The GitHub release shows its groups; the package carries it with its heading too,
# for the app's update sheet, which shows the date and the changes.
$changelog = (Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Raw) -replace "`r`n", "`n"
$section = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($version))\].*?(?=^## \[|^\[[^\]]+\]: |\z)").Value.Trim()
if (-not $section) { throw "CHANGELOG.md has no section for $version" }
$notes = Join-Path $Output "$name-notes.md"
Set-Content $notes ($section -replace '^[^\n]*\n+', '') -NoNewline
$packageNotes = Join-Path $Output "$name-package-notes.md"
Set-Content $packageNotes $section -NoNewline

$repository = 'https://github.com/Hancapo/HikariHopper'
$updates = Join-Path $Output "$name-updates"
if (Test-Path $updates) { Remove-Item $updates -Recurse -Force }
New-Item -ItemType Directory $updates | Out-Null
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }
    # The last release's package, so this one also gets a delta: copies that have updated before download only what changed.
    Write-Host 'Fetching the last release for its package...'
    # A repository with no release in Velopack's feed succeeds and downloads nothing; failing is a network or GitHub
    # error, and packing on would ship this version without its delta.
    & dotnet vpk download github --repoUrl $repository --outputDir $updates 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'Fetching the last release failed' }
    Write-Host 'Packing with Velopack...'
    # --noInst: no installer, only the zip. --runtime, or vpk takes the build for x86.
    & dotnet vpk pack --packId HikariHopper --packVersion $version --packTitle HikariHopper --packAuthors Hancapo `
        --packDir $build --mainExe HikariHopper.exe --runtime win-x64 --noInst --releaseNotes $packageNotes --outputDir $updates 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
}
finally { Pop-Location }

# The zip people download: Velopack's portable zip, under the release's own name.
$zip = Join-Path $Output "$name.zip"
Move-Item (Join-Path $updates 'HikariHopper-win-Portable.zip') $zip -Force
$unpacked = Join-Path $Output "$name-unpacked"
if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
Expand-Archive $zip $unpacked
Assert-NoPrivatePaths $unpacked
Remove-Item $unpacked -Recurse -Force

# What the updater reads from the release: the feed and this version's packages (the last release's stays where it is).
$assets = @($zip, (Join-Path $updates 'releases.win.json')) +
    @(Get-ChildItem $updates -Filter "HikariHopper-$version-*.nupkg" | ForEach-Object FullName)
Write-Host "Built $((Resolve-Path $zip).Path)"
Write-Host 'Publish it with:'
$quoted = ($assets | ForEach-Object { '"' + $_ + '"' }) -join ' '
Write-Host "  gh release create v$version --title $version --notes-file `"$notes`" $quoted"
