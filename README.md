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

`Open Hikari Hopper.bat` builds the app (incrementally) and starts it.

## Tests

```sh
dotnet test --solution HikariHopper.slnx
```

Real-asset checks run when `HIKARI_TEST_LEGACY` points to a GTA V Legacy installation; they
only read from it.

## Game keys

No encryption keys are included. HikariHopper finds the AES key in your own `GTA5.exe` or
`GTA5_Enhanced.exe` (cached in `%LOCALAPPDATA%\fivefury\keys.json`) and uses it to unlock the
bundled, encrypted NG tables in `src/HikariHopper/Assets/Keys/keys.bin`, which are useless
without the game. `tools/MakeKeyBlob.cs` regenerates that blob.
