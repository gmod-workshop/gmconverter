# Changelog

All notable changes to this project will be documented in this file.

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

[1.1.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/gmod-workshop/gmconverter/releases/tag/v1.0.0
