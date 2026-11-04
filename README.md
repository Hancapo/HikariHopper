<h1 align="center"><img src="src/Assets/Branding/hikarihopper-banner.png" alt="HikariHopper" width="640"></h1>

A GTA V (Legacy and Enhanced) RPF Explorer and texture dictionary editor for Windows, built with .NET 10,
Avalonia and FiveFury.NET.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a checkout of
FiveFury.NET (with its submodules) next to this repository (`..\FiveFury.NET`), or point
`FIVEFURY_ROOT` at it.

```sh
dotnet build HikariHopper.slnx -c Release
dotnet run --project src -c Release
```

## Tests

```sh
dotnet test --solution HikariHopper.slnx
```

Real-asset checks run when `HIKARI_TEST_LEGACY` points to a GTA V Legacy installation; they
only read from it.

## Releasing

Releases are Native AOT builds, so users don't need .NET installed. Publishing needs the
Visual Studio C++ build tools.

```sh
pwsh tools/release.ps1 -GameRoot "D:\Path\To\Grand Theft Auto V"
```

The script publishes the AOT build and fails on any AOT compiler warning. It then runs
`HikariHopper.exe --self-test` on the native binary against the game, checks that the window
opens, and zips the result into `artifacts\`.
