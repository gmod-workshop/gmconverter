# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [1.5.0] - 2026-05-14

### Added

- Added a Console workspace in the Avalonia UI for viewing, copying, and clearing session log entries.

### Changed

- Aligned Avalonia workspace card widths with the shared page headers.
- Moved config loading and Source Engine defaults from the Convert page into a dedicated Settings workspace.
- Combined animation and material lookup fields into a Supporting Files card on the Convert page.
- Updated the Avalonia UI window and executable icon to use the GMConverter icon asset.
- Replaced Source game and engine directory settings with StudioMDL and VTFCmd path overrides that auto-download portable tool defaults.
- Updated the README examples and option reference for the portable Source tool workflow.

## [1.4.0] - 2026-05-14

### Added

- Added a UE2 Explorer profile that finds `SkeletalMesh` and `StaticMesh` exports in Unreal packages and resolves them natively into PSK/PSKX inputs with UE2 material sidecars, PSA animation sidecars, DXT texture extraction, wrapped material support for modifiers and combiners, and custom material fallback traversal.

### Changed

- Reworked the Avalonia UI around ShadUI with a new shell layout, modular Convert, Explorer, and Preview views, and a card-based visual structure.
- Split the Avalonia shell state into dedicated Convert, Explorer, and Preview view models to match the modular views.
- Added a collapsible sidebar that remains accessible as a compact icon rail.
- Moved the model preview into a collapsible right-side panel.
- Made the right-side preview panel width adjustable with ratio-based sizing so it scales with the window.
- Replaced the visible convert-page scrollbar with a bottom vertical scroll progress indicator and refined title bar spacing.
- Moved workspace titles into docked per-page headers that match the ShadUI demo layout.
- Simplified the expanded sidebar header by removing the redundant app title and logo.
- Split shared Avalonia shell and page chrome into reusable sidebar, page header, and scroll-progress controls.
- Hid the preview width resize handle while keeping the resize hit area available.
- Simplified the preview panel by removing the status badge, metric strip, and bottom model summary text.
- Reworked the preview toolbar into a minimal Blender-style icon strip with tooltip labels and grouped projection/shading controls.
- Fixed the custom preview toolbar icons so transformed SVG paths stay anchored inside their buttons.
- Updated preview shading modes so Solid removes textures and Wireframe hides textured faces while showing only model edges.
- Switched Solid preview shading to a lit neutral material so model depth remains visible without textures.
- Replaced the custom title-bar logo badge with the app SVG from `Assets/icon.svg`.
- Updated the 3D preview render background to follow the active light or dark theme.
- Updated preview wireframe lines to use a theme-aware color for better contrast in light mode.
- Added maximized-window chrome insets so title-bar controls and bottom content are not clipped at the screen edge.
- Aligned Explorer page cards and actions to the same page width.
- Matched Explorer card spacing to the Convert page spacing rhythm.
- Normalized Convert page two-column card gutters so wide and split rows share the same outer width.
- Updated the Avalonia UI app to use the SharpEngine open source license.
- Added repository-wide `.editorconfig` and analyzer enforcement, and updated the codebase to satisfy the enabled style and quality checks.
- Added agent workflow guidance for branch hygiene, scoped changes, changelog updates, and Conventional Commits.

## [1.3.1] - 2026-05-12

### Fixed

- Fixed glTF/GLB texture sampler defaults so UI preview texture wrapping matches exported models in viewers such as Blender.

## [1.3.0] - 2026-05-12

### Changed

- Updated the publish workflow to package the Avalonia `GMConverter.UI` app instead of the removed WinForms GUI project.
- Documented the published release archive names in the README.
- Added SharpEngine credit to the README.

## [1.2.0] - 2026-05-12

### Added

- Added importer logging support through `ILoggerFactory`, with CLI console logging and GUI log-box routing.
- Added Men of War warnings for missing materials, textures, and animation files.
- Added support for recursive MOW texture lookup through `--material-dir`.
- Added Source Engine Phong mask output from imported specular textures such as MOW `_sp` maps.
- Added a new Avalonia UI project with an improved conversion workflow and 3D preview.
- Added an Explorer tab for scanning loose asset folders and previewing/exporting supported model files.
- Added Explorer support for zip-backed Men of War `.pak` archives.
- Added cross-PAK texture extraction for Men of War models selected in the Explorer.
- Added Explorer refresh support that clears archive caches before rescanning.

### Changed

- Hardened MOW bone name handling for joint-typed bone declarations such as `revolute`.
- Improved MOW texture resolution for shared texture references by matching indexed texture basenames.
- Use MOW `metal`, `wood`, and `concrete` props to emit Source Engine material surface props.
- Use MOW `metal` props to choose a stronger Source Engine Phong material profile.
- Replaced the old WinForms GUI project with the Avalonia UI project.
- Renamed the Game Explorer implementation and UI to Explorer so it can cover general asset folders, not only game directories.
- Improved Explorer archive preview performance with archive-entry and texture-index caching.
- Improved the Avalonia layout so the console remains visible, dense controls scroll only when needed, and preview toolbar controls wrap at narrow widths.
- Persisted preview toolbar options such as orthographic mode, wireframe, and physics overlay.
- Made default CoACD physics generation coarser to reduce preview/export time.

### Fixed

- Fixed MOW imports for models with duplicate original bone names.
- Fixed MOW PLY parsing for multi-material mesh sections.
- Fixed additional MOW PLY material and vertex format variants found in real assets.
- Fixed MOW ANM vertex-animation chunk skipping for animations with variable trailer sizes.
- Fixed unreadable MOW texture files aborting model preview or export.
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

[Unreleased]: https://github.com/gmod-workshop/gmconverter/compare/v1.5.0...HEAD
[1.5.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.3.1...v1.4.0
[1.3.1]: https://github.com/gmod-workshop/gmconverter/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/gmod-workshop/gmconverter/releases/tag/v1.0.0
