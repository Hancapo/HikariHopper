<#
.SYNOPSIS
Builds a HikariHopper release: the Native AOT win-x64 build, checked on the native binary, zipped.

.DESCRIPTION
Publishes with Native AOT and fails on any AOT compiler warning (these cover FiveFury.NET too, which the build's
analyzers don't see). Then runs the app's --self-test against a GTA V installation, starts the app until its window
appears, and zips the build into artifacts\HikariHopper-<version>-win-x64.zip.

.EXAMPLE
pwsh tools/release.ps1 -GameRoot "D:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
#>
param(
    # A GTA V installation for the self-test; defaults to HIKARI_TEST_LEGACY, then to the folder configured in the app.
    [string]$GameRoot = $env:HIKARI_TEST_LEGACY,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\HikariHopper\HikariHopper.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "HikariHopper-$version-win-x64"
$build = Join-Path $Output $name
New-Item -ItemType Directory -Force $Output | Out-Null
if (Test-Path $build) { Remove-Item $build -Recurse -Force }

# The AOT linker step finds MSVC through vswhere.exe, which the Visual Studio installer doesn't put on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }

Write-Host "Publishing $name (Native AOT)..."
$log = & dotnet publish $project -c Release -r win-x64 -p:PublishReferencesDocumentationFiles=false -o $build 2>&1
if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw 'Publish failed' }
$warnings = $log | Select-String 'warning IL\d+' | Sort-Object -Unique
if ($warnings) { $warnings | Write-Host; throw 'The AOT compiler reported warnings; any of them can fail at runtime' }
# Third-party notices are shown in the app instead; debug symbols aren't shipped.
Remove-Item (Join-Path $build 'licenses') -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $build -Filter *.pdb | Remove-Item

# Nothing shipped may name this machine's folders, such as a PDB path in a binary's debug directory.
$private = @($env:USERPROFILE, (Resolve-Path (Join-Path $PSScriptRoot '..')).Path) | Where-Object { $_ }
$leaks = foreach ($file in Get-ChildItem $build -Recurse -File) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    # Latin-1 for 8-bit strings; UTF-16 read from both even and odd offsets.
    $texts = [Text.Encoding]::Latin1.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes),
        [Text.Encoding]::Unicode.GetString($bytes, 1, [Math]::Max(0, $bytes.Length - 1))
    foreach ($path in $private) {
        if ($texts | Where-Object { $_.IndexOf($path, [StringComparison]::OrdinalIgnoreCase) -ge 0 }) { "$($file.Name) contains $path" }
    }
}
if ($leaks) { $leaks | Write-Host; throw 'The build contains private paths' }

$exe = Join-Path $build 'HikariHopper.exe'
Write-Host 'Running the self-test on the native build...'
$report = Join-Path $Output 'self-test.txt'
$arguments = @('--self-test')
# Backslashes before the closing quote are doubled, or a path ending in \ would escape it.
if ($GameRoot) { $arguments += '"' + ($GameRoot -replace '(\\+)$', '$1$1') + '"' }
# Start-Process -Wait, because PowerShell doesn't wait for a GUI exe started with & and would read a stale exit code.
$test = Start-Process $exe -ArgumentList $arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $report
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

$zip = Join-Path $Output "$name.zip"
Compress-Archive -Path $build -DestinationPath $zip -Force
Write-Host "Built $((Resolve-Path $zip).Path)"
