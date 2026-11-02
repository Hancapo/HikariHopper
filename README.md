# HikariHopper

A GTA V RPF Explorer and texture dictionary editor for Windows, built with .NET 10,
Avalonia and FiveFury.NET.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a checkout of
FiveFury.NET (with its submodules) next to this repository (`..\FiveFury.NET`), or point
`FIVEFURY_ROOT` at it.

```sh
dotnet build HikariHopper.slnx -c Release
dotnet run --project src/HikariHopper -c Release
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

## Game keys

No encryption keys are included. HikariHopper finds the AES key in your own `GTA5.exe` or
`GTA5_Enhanced.exe` (cached in `%LOCALAPPDATA%\fivefury\keys.json`) and uses it to unlock the
bundled, encrypted NG tables in `src/HikariHopper/Assets/Keys/keys.bin`, which are useless
without the game. `tools/MakeKeyBlob.cs` regenerates that blob.
