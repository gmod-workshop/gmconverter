# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Added importer logging support through `ILoggerFactory`, with CLI console logging and GUI log-box routing.
- Added Men of War warnings for missing materials, textures, and animation files.
- Added support for recursive MOW texture lookup through `--material-dir`.
- Added Source Engine Phong mask output from imported specular textures such as MOW `_sp` maps.
- Added a GUI Game Explorer tab for scanning loose game folders and previewing/exporting supported model files.
- Added Game Explorer support for zip-backed Men of War `.pak` archives.

### Changed

- Hardened MOW bone name handling for joint-typed bone declarations such as `revolute`.
- Improved MOW texture resolution for shared texture references by matching indexed texture basenames.
- Use MOW `metal`, `wood`, and `concrete` props to emit Source Engine material surface props.
- Use MOW `metal` props to choose a stronger Source Engine Phong material profile.

### Fixed

- Fixed MOW imports for models with duplicate original bone names.
- Fixed MOW PLY parsing for multi-material mesh sections.
- Fixed additional MOW PLY material and vertex format variants found in real assets.
- Fixed MOW ANM vertex-animation chunk skipping for animations with variable trailer sizes.
- Improved MOW parser errors for truncated or unsupported PLY and ANM data.

## [1.1.0] - 2026-05-11

### Added

- Added Men of War / Assault Squad 2 import support with `--input-format mow`.
- Added support for MOW `.def` entry files and referenced `.mdl` model files.
- Added MOW mesh, material, skeleton, rigid bone-weight, and `.anm` animation import.
- Added MOW diffuse/specular DDS texture resolution from local material files.
- Added MOW support to the CLI and GUI file picker.
- Added importer/exporter source names for GUI dropdowns, such as `MDL (Source Engine)` and `PSK (Unreal Engine)`.

### Changed

- Moved manually parsed PSK and PSA format records into `GMConverter.Formats.PSK` and `GMConverter.Formats.PSA`.
- Kept PSK material resolution self-contained inside `PSKImporter`.
- Updated README format support and MOW usage documentation.

### Fixed

- Fixed glTF/GLB texture coordinate export so textures line up correctly in Blender.
- Fixed GUI preview texture sampling so previewed textures match exported models.
- Fixed Source/MDL export triangle winding and material handling for MOW-derived models.
- Fixed Source material export for specular maps by using Source phong parameters instead of treating specular textures as alpha.
- Added `$nocull` to exported Source materials for models that need two-sided rendering.
- Fixed Source animation export to preserve keyframe timing and held poses.
- Fixed Source animation transforms for scaled or mirrored MOW bones so animated parts move in the expected direction.

## [1.0.0]

- Initial public release.

[Unreleased]: https://github.com/gmod-workshop/gmconverter/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/gmod-workshop/gmconverter/releases/tag/v1.0.0
