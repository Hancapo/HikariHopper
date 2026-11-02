# Changelog

All notable changes to HikariHopper are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

### Fixed

- The folder tree shows an expand arrow only on RPFs that have folders or other RPFs inside. ([#19](https://github.com/Hancapo/HikariHopper/pull/19))
- Release builds no longer contain the folder paths of the machine that built them. ([#18](https://github.com/Hancapo/HikariHopper/pull/18))

### Removed

## [0.0.2] - 2026-11-02

### Changed

- Text uses Avalonia's own rendering, keeping each font's letter widths and line heights instead of rounding them the way Qt does. ([#13](https://github.com/Hancapo/HikariHopper/pull/13))
- Releases are native builds and no longer need the .NET 10 Runtime. ([#14](https://github.com/Hancapo/HikariHopper/pull/14))

## [0.0.1] - 2026-11-02

### Added

- RPF explorer for GTA V Legacy and Enhanced, with nested archives, a folder tree, list and grid views, search and tabs.
- Texture editor for YTD dictionaries and textures embedded in YDR drawables.

[Unreleased]: https://github.com/Hancapo/HikariHopper/compare/v0.0.2...HEAD
[0.0.2]: https://github.com/Hancapo/HikariHopper/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/Hancapo/HikariHopper/releases/tag/v0.0.1
