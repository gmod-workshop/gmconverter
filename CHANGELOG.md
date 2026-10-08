# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Added a plugin system. The SDK now defines `IPlugin`, `IPluginContext`, `PluginManifest`, and a `[Plugin(typeof(MyPlugin))]` assembly attribute under `GMConverter.SDK.Plugins`. The core library hosts a `PluginLoader` that discovers `plugin.json` manifests under a `plugins/` directory next to the executable, loads each into its own collectible `AssemblyLoadContext` with SDK type unification (so `IImporter`/`IExporter`/`IExplorer` references match across the plugin boundary), and instantiates the plugin's entry type. Plugin-contributed importers and explorers are merged into the host's resolution path; CLI and UI both bootstrap the loader at startup.
- Extracted Unreal Engine 4/5 support into a standalone `GMConverter.UnrealEngine` plugin assembly that references only `GMConverter.SDK` plus CUE4Parse and MessagePack. The plugin contributes the PSK importer (with optional scene-manifest support) and the UE4 and UE2 archive explorers via `IPluginContext`. Host bundling is opt-in: `GMConverter.CLI` and `GMConverter.UI` list it via `<BundledPlugin>` items in their csprojs, which trigger a loose `ProjectReference` (for build order) and a `CopyBundledPlugins` target that stages the plugin's `bin/` into the host's `bin/plugins/unrealengine/`. The host's CUE4Parse and MessagePack package references are gone — those move with the plugin.
- Extracted Source Engine support into a standalone `GMConverter.SourceEngine` plugin assembly that references only `GMConverter.SDK` plus MdlCrowbar. The plugin contributes the MDL importer (reads Garry's Mod / Source MDL via MdlCrowbar) and the MDL exporter (writes the full SMD / QC / VTF / VMT compile workspace via studiomdl + VTFCmd) via `IPluginContext` using DI activation. `MDLExporter` declares the full ~12-option schema (Tools / Materials / Physics groups) via the new `ExporterOptionSchema` so future generic UI rendering can drive the panel. Host's `MdlCrowbar` package ref and the entire `Source/` directory are gone from Core — they live in the plugin. The CoACD native and its MSBuild download target also moved with the plugin since CoACD is Source-physics-specific. Source compile binaries (`studiomdl.exe`, `VTFCmd.exe`) still auto-download into `./tools/` on first run.
- Added `ITextureFactory` to the SDK. Plugins construct `Texture` instances by name/stream/raw RGBA bytes without taking a dependency on the host's image library. The default implementation (`DefaultTextureFactory` in Core) wraps SixLabors.ImageSharp and is registered in the host's service provider.
- Wired Microsoft.Extensions.DependencyInjection into the plugin system. `IPluginContext` now exposes an `IServiceProvider Services` populated by the host with `ITextureFactory`, `ILoggerFactory`, and any future host-side services. Plugins consume these via constructor injection — `context.RegisterImporter<PSKImporter>()` activates `PSKImporter` against the provider, resolving its ctor params automatically. Named context properties for individual services (e.g. `TextureFactory`, `LoggerFactory`) were dropped in favor of a single `Services` source of truth; adding a new host capability is now a one-line registration in `PluginLoader` with zero impact on existing plugins.
- Moved the format-agnostic helpers `NameHelpers`, `PathHelpers`, `PerfTimer`, and `ExplorerFileSystem` from `GMConverter.Common`/`GMConverter.Explorer` (Core) to `GMConverter.SDK.Common`/`GMConverter.SDK.Explorer` so plugins (and any future engine plugin) can share them without taking a Core reference.
- Added scrolling-texture support for Unreal Engine 2 materials. `TexPanner` rates and directions on a material's diffuse chain are recorded in UE2 Explorer material sidecars (`UvScroll=<u>,<v>`), carried on the new SDK `Material.UvScrollRate`, and written to Source VMTs as `TextureScroll` proxies, so animated liquids such as Republic Commando's bacta dispenser scroll in game. Republic Commando's `TexPanner2D` (per-axis `SpeedU`/`SpeedV`) is supported as well.
- Added independently scrolling glow layers for Unreal Engine 2 shaders whose `SelfIlluminationMask` is panned separately from the rest of the material, such as the bubbles in Republic Commando's bacta liquid. The mask is baked with the self-illumination colour, carried on the new SDK `Material.EmissiveUvScrollRate`, and written to Source as an unlit additive `$detail` layer with its own `TextureScroll`.
- Added material blend modes. The SDK `Material` now carries `BlendMode` (alpha blend, alpha test, or additive) and `AlphaCutoff`. Unreal Engine 2 Shader and FinalBlend blending (including masked cut-outs and additive glows) is recorded in UE2 Explorer sidecars and written to Source as `$translucent`, `$alphatest`/`$alphatestreference`, or `$additive`; glTF exports alpha-tested materials with `MASK`. Materials without a blend mode keep the previous alpha-based behaviour.
- Added material skins. Unreal Engine 2 materials that the game swaps in at run time are exported as alternate skins: sibling materials in the same package group that share a name stem and a diffuse texture, such as Republic Commando's ammo dispenser `_On`, `_Warm`, `_Off` and `_Dest` states. They are listed in UE2 Explorer sidecars (`Variants=`), carried on the new SDK `Model.Skins` (`MaterialSkin`), and written to Source as a `$texturegroup` so each state is a selectable skin.
- Added glows that scroll under a fixed mask, such as the flashing state lights on Republic Commando's ammo dispensers. UE2 Explorer sidecars record a panner on the `SelfIllumination` chain and a `ConstantColor` added to or multiplied into it. The importer carries them on the new SDK `Material.EmissiveLayer`, and Source gets VertexLitGeneric's emissive blend pass with an identity flow map. Exporters that can't animate the glow use its first frame.
- Added Unreal Engine 2 texture modifiers beyond scrolling.
  - `TexScaler` tiling, `TexRotator` rotation and `TexOscillator` pan, stretch and jitter are recorded in UE2 Explorer sidecars (`UvTransform=`).
  - The importer carries them on the new SDK `Material.UvTransform`. Source gets a static `$basetexturetransform`, or a `TextureTransform` driven by `Sine`, `LinearRamp` and `UniformNoise` proxies when animated.
- Added two-texture `Combiner` materials (add or multiply). The second texture becomes the new SDK `Material.DetailLayer`, with its own transform, and is written to Source as `$detail`. Examples are Republic Commando's tiled burlap over a lightmap, a drifting scope line, and a rotating light swirl.
- Added colour-only glows. A `ConstantColor` or `FadeColor` used as a material's self-illumination glows in that colour, and a `FadeColor` pulses between its two colours (the new SDK `MaterialEmissiveLayer.Pulse`). An example is Republic Commando's demolition charge.
- Added masked-blend `Combiner` materials to UE2 Explorer exports, such as Republic Commando's destroyed-dispenser scorch overlay. The overlay and its mask are now composited into the diffuse instead of the overlay texture replacing the whole material.

### Changed

- The GUI and CLI no longer have Source-specific code; every Source option comes from the Source plugin.
  - The legacy CLI flags (`--model-path`, `--studiomdl-path`, `--vtfcmd-path`, `--no-materials`, `--physics`, `--physics-mode`, `--physics-mass`, `--coacd-threshold`, `--max-convex-pieces`, `--coacd-max-hull-vertices`) still work, as aliases of the matching `--mdl-` options. Any boolean option flag can now be given bare, and enum values are matched case-insensitively.
  - `--physics-mode` no longer turns physics on by itself; add `--physics`.
  - The CLI no longer offers `--glb-binary`, which `--output-format` always overrode.
  - The collision overlay in the GUI preview now comes from the exporter, so it shows the real CoACD hulls again instead of only the bounding box.
  - Saved GUI settings keep only options you changed. Settings from older versions, including StudioMDL/VTFCmd paths and physics and texture options, are carried over once. The saved model path is not: it was tied to the last model, and a blank model path now becomes `gmconverter/<name>.mdl` at export.
  - On a fresh install the GUI starts from the Source plugin's defaults: no texture resize and no texture deduplication, the same as the CLI. It previously defaulted to a 1024 resize with deduplication.
  - Config files no longer have their own Source keys; keys such as `studiomdl`, `physics` and `nomaterials` match the plugin's option aliases instead.
- Plugin SDK: added `ICollisionPreview`, which an exporter implements to supply the collision meshes hosts draw in the preview. A Bool option alias starting with `no-` sets the opposite value.
- Importers now declare their own options and file extensions, like exporters already did.
  - The PSK animation file is a PSK importer option. The CLI flag is `--psk-import-animationPath`, and `--animation-path` keeps working as an alias. In the GUI it moved into a new **Import options** panel on the Convert page, which shows whatever options the selected importer declares.
  - Input file checks and the GUI file picker use each importer's declared extensions instead of a built-in list.
  - Config files accept any importer or exporter option key or alias. Keys that match nothing are reported in the console instead of failing the whole load.
  - The last-used animation path is not carried over from older settings files; pick it again once.
- Plugin SDK: option types moved to `GMConverter.SDK.Options`, with `ExporterOptionSchema` renamed to `OptionSchema` and `ExportOptions` to `OptionValues`. `OptionDescriptor` gained `Aliases`. `IImporter` gained `FileExtensions` and `OptionSchema`, and `ModelParseOptions` carries importer values in `Options` instead of `AnimationPath`. `ExplorerResolvedEntry` passes importer values through `ImporterOptions` instead of `AnimationPath`. `MaterialSpecularTexturePacking.UnrealSpecularMasks` is now `SpecularMetallicRoughness`, named for its channel layout.
- Moved Men of War and X-Wing Alliance support out of the core library into their own bundled plugins, `GMConverter.MenOfWar` (MOW importer and explorer) and `GMConverter.XWingAlliance` (OPT importer). Every importer now comes from a plugin, so the input format list is sorted by format. Warnings from plugin importers, such as missing Men of War textures, now appear in the UI console.
- Extracted the plugin-facing contract into a new `GMConverter.SDK` project under the `GMConverter.SDK.*` namespace, split by domain: `GMConverter.SDK.Geometry` (mesh/skeleton bits), `GMConverter.SDK.Animation` (clips and tracks), `GMConverter.SDK.Materials`, `GMConverter.SDK.Textures`, plus `GMConverter.SDK.Importers` / `Exporters` / `Explorer` / `Common`. The core `GMConverter` library now references the SDK and all moved types are `public` so engine plugins can compile against the contract without a dependency on the core library. CLI and UI behavior is unchanged.
- Kept the SDK free of implementation dependencies. `Texture` is now an abstract base class with no image-library dependency; the concrete `ImageSharpTexture` implementation lives in the core library, and pipeline-specific transforms (`ToGltfMetallicRoughness`, `ToSourcePhongExponent`, `WithOpenGlNormalMap`, etc.) are exposed as extension methods on the SDK `Texture` that delegate to the concrete type. The MessagePack attribute on `ExplorerFileEntry` was removed; the UE4 scan cache continues to serialize it via the contractless resolver.
- Split `IExporter<TOptions>` to inherit from a new non-generic `IExporterDescriptor` so the plugin registry can hold exporters in a homogeneous collection and look them up by `OutputFormat` without committing to a specific options type at the call site. *(Superseded later in the same `[Unreleased]` window — see the schema-driven `IExporter` entry below.)*
- Replaced `IExporter<TOptions>` (and the transitional `IExporterDescriptor`) with a single non-generic `IExporter` that exposes an `ExporterOptionSchema` and accepts an `ExportOptions` bag at invocation time. The SDK adds the schema types (`OptionType`, `OptionDescriptor`, `OptionGroup`, `ExporterOptionSchema`) and the bag value type (`ExportOptions`). Each exporter declares the options it accepts as data; the host builds an `ExportOptions` from UI/CLI inputs keyed by `OptionDescriptor.Key` and hands it to the plugin, which reads what it needs via typed accessors and constructs its own internal options record. Source Engine declares the full schema; GLTF and OBJ declare minimal schemas (or empty for OBJ).
- Added a schema-driven exporter options panel to Convert and Settings, including text, path, boolean, numeric, and enum controls. All groups from the selected exporter are shown; numeric limits and increments come from the schema. Existing Source settings remain compatible, and the `source` and `mdl` aliases share option state.
- Populated UI input/output formats from loaded plugins and routed conversion through the selected exporter with its option bag. Plugin options persist per format in `ui-settings.json`, including values for temporarily unavailable plugins. glTF binary/text output remains controlled by the selected format.
- Added schema-generated CLI flags named `--<format>-<option-key>` (colons in keys become hyphens), with type, enum, and numeric-range validation. Newly installed plugin exporters can run through the CLI without host changes; existing Source flags remain supported.
- Included bundled plugin manifests, assemblies, and dependencies in CLI/UI publish output so release packages retain plugin functionality, including single-file hosts.
- Updated the Unreal plugin's MessagePack dependency to 3.1.10 to resolve vulnerability warnings that blocked builds.
- Removed UI-side CoACD physics preview generation. The Source plugin owns CoACD now (`CoacdNative` moved with it), and the UI no longer has direct access to the native; physics preview falls back to a bounds visualisation for both `bounds` and `coacd` modes. The actual export still produces CoACD physics when the user selects that mode. A follow-up could surface a plugin-contributed "preview physics" hook so the UI can render the real shape pre-export.
- Hardened the plugin native-library loader. `PluginLoadContext.LoadUnmanagedDll` now falls back to a sibling-folder probe (with platform-conventional shared-library names) when `AssemblyDependencyResolver` returns no path. CUE4Parse-Natives ships as a `Content`/`CopyToOutputDirectory` item rather than a `runtimes/<rid>/native/` layout and is not listed in deps.json as a native asset, so the resolver alone would not find it; the fallback closes that gap.

### Fixed

- Fixed builds failing on new SixLabors.ImageSharp 3.1.12 security advisories. The patched 4.x release requires a Six Labors license key to compile, so the advisories are suppressed with documented reasons until ImageSharp is upgraded or replaced.
- Normalized standalone Unreal PSK/PSKX geometry, skeletons, and PSA animation translations from centimeters to meters, matching scene imports. Source exports at scale 1 now retain their physical size instead of being 100 times too large. Bounds collision and its preview use a one-inch minimum thickness instead of one meter.
- Preserved opacity from 32-bit UModel TGA textures whose headers declare no alpha bits. Translucent PSK materials such as the bacta dispenser liquid previously compiled to fully opaque VTFs despite `$translucent`.
- Fixed UE2 Explorer mesh exports failing to convert when materials use DXT textures. The exporter wrote raw DDS sidecars, which the PSK importer cannot decode; DXT1/DXT3/DXT5 textures are now decoded and written as PNG.
- Fixed UE2 Explorer exports silently dropping uncompressed RGBA8, 16-bit grayscale (G16), and Republic Commando format-12 signed normal-map textures, about 7% of Republic Commando's textures. They are now decoded to PNG; paletted P8 textures, which Republic Commando only uses for fonts and editor labels, remain unsupported.
- Fixed Republic Commando shader bump maps being dropped from UE2 Explorer exports. Their two-byte height format is now decoded and converted to a tangent-space normal map, so Source and glTF exports receive normal detail.
- Fixed Source animations distorting bones that are pitched straight up or down. The SMD writer degenerated at ±90° pitch and wrote arbitrary roll and yaw, so rigs with vertical bones (such as Republic Commando's Dwarf Spider Droid and Wookiee) stretched or detached limbs in every animation except the bind pose. Angles are now derived the way Source's `MatrixAngles` does, folding the spin into yaw at gimbal lock.
- Fixed static Unreal Engine 2 self-illumination being ignored. Shaders that gate `SelfIllumination` with a `SelfIlluminationMask` alpha (lit panels, indicator eyes) now export a per-pixel glow texture.
- Fixed PSK materials with an explicit `Normal` sidecar entry also using the normal map as their specular texture, which produced bogus phong and envmap settings in Source. The packed-texture term `orm` matched the `Normal` key.
- Fixed self-illuminated Source materials rendering fully lit in Garry's Mod. Static glow was written as a separate `$selfillummask`, but the game reads self-illumination from an alpha channel that was solid white. Opaque materials now store glow coverage in the base texture's alpha with plain `$selfillum`; translucent ones keep a mask with coverage in every channel.
- Fixed inside-out parts rendering nearly black in PSK imports. Closed shells whose winding is inverted relative to a clear majority of the mesh's closed shells (such as one of the bacta dispenser's eyelid doors in Republic Commando) are now rewound on import. Open surfaces and PSKX files with authored normals are left unchanged.
- Fixed the UE2 property reader discarding bool properties, so flags such as FinalBlend `AlphaTest` and `TwoSided` were always read as unset. FinalBlend's additive "Translucent" mode is also no longer treated as alpha blending.
- Fixed UE2 materials whose glow comes through a `Combiner` (such as Republic Commando's droid dispensers) losing their self-illumination and having the glow's scroll applied to the diffuse texture. Nested combiners now keep their channel, a glow colour texture smaller than its mask is resampled instead of dropped, and a `SelfIlluminationMask` that cannot be baked no longer falls back to lighting the whole surface.
- Fixed UE2 Explorer listing a material's own building blocks as alternate skins: a `Combiner` used inside a FinalBlend or Shader in the same package group is no longer exported as a variant of it.

## [1.7.0] - 2026-05-21

### Added

- Added Source MDL texture optimization options in the Source Engine settings card under a new Materials subsection: a max-edge texture-size cap (default 1024, with Original/512/1024/2048/4096 presets) that resizes textures before VTF compile, and a deduplicate-identical-textures toggle (default on) that content-hashes resized PNGs so materials sharing the same map only emit one VTF. Together these cut typical Fortnite character MDL output from roughly 200–300 MB of textures down to ~50 MB without quality loss on representative assets.
- Added auto-fill for the StudioMDL and VTFCmd path fields in the Source Engine settings card. On startup the UI scans the existing `tools/` directory and populates empty fields with whatever portable tools are already extracted. The discovered paths are not persisted to `ui-settings.json`, so moving the app folder cleanly re-resolves against the new `tools/` location instead of carrying a stale absolute path forward.

### Changed

- Extracted the Source Engine settings card into a dedicated `Controls/Settings/SourceEngineSettings` UserControl and split its contents into labelled `TOOLS` and `MATERIALS` subsections to keep `SettingsView` readable as the section grew.
- Passed `-resize` to VTFCmd so non-power-of-two inputs (common from the multi-layer baker, whose output dimensions are `baseWidth × tileX` by `baseHeight × tileY` and land non-POT whenever the tile counts are not powers of two) get snapped to POT instead of silently exiting 0 with no `.vtf`. Added a post-compile existence check that turns those silent failures into loud build errors with the source PNG path.
- Gated the `MDLExporter.ExportSourceMaterials` PNG fallback path so per-material PNGs are no longer emitted alongside compiled VTFs in the output `materials/` directory. The fallback now runs only when VTF compilation is not going to (no `VtfCmdPath`, or `BuildMaterials` off).
- Routed `studiomdl` invocations through `ProcessRunner` and set `CreateNoWindow = true` on every shelled-out tool. The vtfcmd, studiomdl, and coacd subprocesses no longer briefly flash console windows during a conversion.

### Fixed

- Fixed UE4/5 preview rendering by switching the preview pane to `PhysicallyBasedMaterial` (`UsePbrMaterial = true`). UE4/5/Fortnite assets export with `metallic = 1.0`, and the previous PBR→Phong conversion drained the base color into specular, leaving metallic surfaces near-black under analytic lights.
- Fixed UV transforms in the in-app preview by adding `GLTFExportOptions.BakeUvTransforms`, which folds each material's `BakedUv0Scale` into mesh UVs and skips the `KHR_texture_transform` write. The Ab4d.SharpEngine glTF importer used for the preview does not honor that extension, which made multi-layer Fortnite parts sample the wrong tile of the `MultiLayerBaker` output. The main `.glb` export keeps the extension-based representation so Blender and other consumers that honor `KHR_texture_transform` see the same data they do today.
- Fixed glTF/GLB orientation when exporting Z-up internal data. Importers normalize source data to Z-up, but `GLTFExporter` was writing vertex positions, joint bind poses, and animation keyframes as-is while still declaring Y-up, so the in-app preview and Blender both showed the model tipped onto its face. Every top-level scene node is now wrapped under a single `NodeBuilder` that applies a -90° rotation around the X axis at view time, so Auto / Z-up input produces correct preview, Blender output, and MDL output without having to manually flip axis mode.
- Fixed Avalonia preview pane disposal when the main window closes via the `[X]` chrome button. Avalonia does not raise `Unloaded` on a top-level `Window` on that path, so `PreviewPane.Dispose()` (and therefore `SharpEngineSceneView.Dispose()`) never ran and SharpEngine's Vulkan render thread kept the .NET host alive — holding the build output DLLs open and blocking the next `dotnet build` until the host was killed manually. The shell now hooks `Closed`, walks the visual tree for `PreviewPane` descendants, disposes each, then disposes the view model.
- Fixed the Publish workflow by passing `submodules: recursive` to `actions/checkout`, matching the Build workflow. The v1.6.0 and v1.6.1 publishes had failed at release time with CS0246 errors across every CUE4Parse-derived type because the `Dependencies/CUE4Parse` submodule was checked out as an empty directory.

## [1.6.1] - 2026-05-19

### Added

- Added a persistent on-disk scan cache for Unreal Engine 4/5 archives keyed by a fingerprint of `*.pak`, `*.utoc`, `*.ucas`, `*.sig`, and `Manifest_*.txt` sizes and modification times. Subsequent scans of an unchanged install skip the CUE4Parse mount and `AssetRegistry.bin` parse and return in milliseconds; the cache is invalidated automatically when the archive set changes and is wiped by the existing Refresh action.
- Added a persistent on-disk asset-export cache for UE4/5 resolves. Re-previewing or re-converting the same asset against an unchanged archive now reuses the previously extracted PSK, decoded textures, and material sidecars instead of re-running CUE4Parse and the multi-layer baker each time. The cache is bounded by an LRU sweeper with a 5 GB default per archive (override with `GMCONVERTER_EXPORT_CACHE_BYTES`).
- Added Stopwatch-based instrumentation across the UE4/5 scan, resolve, and export pipeline (`%TEMP%\GMConverter.Perf.log`) for diagnosing per-phase cost.

### Changed

- Gated the per-material PNG re-encode debug dump in the glTF exporter behind `GMCONVERTER_GLTF_DEBUG_DUMP=1` so production exports no longer encode every texture twice. Multi-part Fortnite scenes that previously took ~30 s in glTF export should fall to roughly half that time.
- Memoized per-`Texture` PNG encoding inside a single glTF export so textures shared across multiple materials are encoded once instead of once per material reference.
- Memoized derived textures inside a single glTF export so `WithOpenGlNormalMap`, `ToGltfMetallicRoughness`, and `ToSpecularFactorMask` each run once per unique source texture instead of once per material reference.
- Lowered the PNG compression level used for in-memory texture encoding (`Texture.ToPngBytes`) from the default (DEFLATE level 6) to level 1. This trades roughly 10-15% larger PNG payloads in `.glb` outputs for a 3-4x faster encode pass, which was the dominant cost left in `GLTFExporter` after the prior dedup work.
- Lifted the per-material texture decode cache in `WriteResolvedMaterialOverrides` to per-part scope so two materials in the same UE mesh part that share a texture only pay the decode/encode/write cost once.
- Gated the per-material multi-layer bake diagnostic logs (`*.uvdiag.log`, `*.bakediag.log`) behind `GMCONVERTER_BAKE_DIAGNOSTICS=1` to drop fixed-cost StringBuilder + disk writes per multi-layer material.
- Reduced the post-export file-settle polling cadence in the UE4/5 explorer from 100 ms to 10 ms so resolving a multi-part scene saves several seconds of idle wait without lowering the overall five-second safety ceiling.

### Removed

- Removed the `GMCONVERTER_KEEP_EXPORT_CACHE` opt-in environment variable. UE4/5 export reuse is now always active and governed by the new sentinel-based asset-export cache, which validates archive fingerprint and tool version before reusing a previously extracted asset tree.

### Performance

- Added a thread-safe per-operation artifact cache in `GMConverter.Common` that memoizes expensive byte-producing work (currently used for UE texture decode but format-agnostic for future importers/exporters). UE4/5 scenes with shared textures across multiple parts now decode each unique texture once per resolve instead of once per reference, cutting the dominant cold-path cost in `WriteResolvedMaterialOverrides` and the multi-layer baker's channel loading.
- Added a generic `DecodedImage` type in `GMConverter.Common` for cache payloads that need raw RGBA pixels plus lazy PNG memoization. The UE multi-layer baker now reads raw pixels directly into ImageSharp via `Image.LoadPixelData<Rgba32>` instead of going through an encode-then-decode PNG roundtrip, eliminating ~1-4 s of per-cold-preview work on a multi-part Fortnite scene.
- Parallelized the UE4/5 per-part export loop in `ExportResolvedScene`. Parts on distinct mesh UObjects run concurrently while parts that share an underlying UObject serialize against a per-UObject lock (necessary because `MaterialOverrideScope` and CUE4Parse's `Exporter` both mutate or read state on the shared UObject). Parallelism degree defaults to half the logical core count capped at 4, override via `GMCONVERTER_PARTS_PARALLELISM=N` (set to 1 to disable). Expected cold-path win on a 22-part Fortnite scene: ~105 s sequential → ~30-45 s parallel.
- Added a generic `BarycentricRasterizer` helper in `GMConverter.Common` with a `Vector<float>` SIMD inner loop. Walks the pixels inside a 2D triangle and invokes a struct-generic `IBarycentricPixelHandler` per inside pixel; the JIT specializes per concrete handler type so the per-pixel call is inlined. The UE multi-layer baker's `RasterizeTriangle` is now a thin caller that delegates to this helper, and any future format that needs UV-space rasterization can reuse the same primitive.
- Parallelized glTF material construction so all materials in a multi-part scene now build concurrently with a thread-safe derived-texture cache.
- Parallelized `PSKImporter.ParseScene` so the per-entry mesh parse of an Unreal scene runs across cores while preserving manifest order.
- Switched the persistent UE4/5 scan cache from JSON to LZ4-compressed MessagePack (`*.msgpack` files in `%LOCALAPPDATA%\GMConverter\cache\ue4-scan`). Reading a 138K-entry Fortnite scan cache now takes a fraction of the prior JSON deserialize cost and the on-disk file is roughly half the size. Adds a `MessagePack` package reference.
- Parallelized the multi-layer baker: the three channel bakes (diffuse, normals, specular) run concurrently per material, and the background-fill phase inside `BakeChannel` walks rows with `Parallel.For`. Triangle rasterization stays sequential to preserve last-write-wins semantics on overlapping triangles.
- Rewrote the in-place texture transforms (`WithOpenGlNormalMap`, `ToGltfMetallicRoughness`, `ToSpecularFactorMask`) to operate on direct byte spans instead of per-pixel `Rgba32` struct reads/writes, with a `Vector<byte>` SIMD path for green-channel inversion (the most common transform — every DirectX normal map hits it).

## [1.6.0] - 2026-05-19

### Added

- Added an Unreal Engine 4/5 Explorer profile backed by CUE4Parse for browsing archive meshes and resolving them into the existing conversion workflow.
- Added Fortnite archive bootstrap support that fetches current AES keys and mappings metadata from UEDB when scanning an installed Fortnite content folder.
- Added a multi-layer texture bake for Fortnite materials (`Use 2/3/4 Layers`) that rasterizes each part's UV1 mask into flat composite Diffuse, Normals, and SpecularMasks textures, with per-part output hashes so sibling mesh variants that share a material name do not collide on a single bake.
- Added Source MDL real-world scale conversion (meters to Source units) at SMD write time so UE-derived models import at their authored physical size instead of inch-tall miniatures.
- Added Source MDL environment-map reflection emission for materials with specular data, with the per-pixel mask packed into the normal-map alpha (`$normalmapalphaenvmapmask`) when a bump map is present so opaque glass-like surfaces are reflective without needing `$translucent`.

### Changed

- Extended PSK material resolution to read CUE4Parse material JSON sidecars and their exported texture references.
- Changed Explorer auto-detect to choose a single matching profile so Unreal archive roots do not also trigger slower legacy directory scans.
- Changed Fortnite UEDB selection to prefer the highest available change list and report locked archives when the published key set is incomplete.
- Changed Fortnite UEDB integration to use the official AES and mappings API endpoints instead of parsing the web page.
- Changed UE4/UE5 archive scans to use readable mounted containers even when other encrypted containers are still locked.
- Split Unreal archive setup into game profiles so Fortnite can use a dedicated CUE4Parse version/key/mapping path while generic UE4/UE5 archives remain supported separately.
- Switched CUE4Parse to a pinned source submodule so Fortnite can use newer UE 5.8, usmap, and on-demand archive support that is not available in the published NuGet package.
- Added Fortnite item-definition registry browsing and initial mesh resolution from common cosmetic and weapon mesh properties.
- Added Fortnite playset prop and prefab browsing with preview resolution through level save records, actor blueprints, component templates, component material overrides, and multi-mesh scene manifests.
- Initialized UE texture decoders and added a mesh-only fallback so texture decompression failures do not block Fortnite PPID previews.
- Added UE archive preview diagnostics that report resolved mesh parts, material files, texture files, and mesh-only fallbacks.
- Improved UE4/UE5 scan errors when mounted containers expose no readable asset registry or package files.
- Updated Fortnite archive setup to use the UE 5.8 package version value and load CUE4Parse virtual paths before scanning.
- Improved Fortnite preview scene cleanup by deduplicating equivalent UE mesh parts, filtering graybox/blockout placeholders when real meshes are available, using UE material color parameters when no diffuse texture is exported, preserving SFX emissive textures in GLTF previews, rendering preview meshes double-sided, normalizing PSK normals, preferring Fortnite layer-specific texture channels, and importing preview GLBs with more stable non-cached materials.
- Improved Fortnite material export by marking CUE4Parse material textures as UE-style packed data, converting packed specular masks into glTF metallic/roughness and Source phong inputs, and flipping DirectX normal maps for GLB/OBJ exports.
- Added Unreal animation asset listings for `AnimSequence` and `AnimMontage` entries in the Explorer so candidate Fortnite animation assets are easier to identify before conversion support is wired.
- Added Explorer animation filters, including a related-animation search that derives candidate animation terms from the selected Unreal/Fortnite asset.
- Broadened Unreal animation browsing to include additional animation-like asset classes and registry entries whose names or paths indicate animation content.
- Improved Fortnite level-save scene reconstruction by applying actor instance transforms and matching texture-data overrides by actor-data order instead of template map key.
- Corrected UE packed specular mask channel mapping for glTF and Source exports so roughness and metallic data are not swapped.
- Limited displayed Explorer filter results so broad Fortnite animation searches do not lock up the UI while building the tree.
- Improved Fortnite scene diagnostics with per-part transform and texture slot details in the resolve log.
- Cleared stale UE4/UE5 per-asset export cache folders before resolving a selection so old material override sidecars cannot bleed into refreshed previews.
- Improved CUE4Parse material texture selection by scoring texture candidates against the material name, which avoids choosing unrelated Fortnite layer, decal, snow, or global fallback textures from large material JSON sidecars.
- Reworked the glTF specular texture extension for UE packed masks to carry the per-pixel mask value in both RGB and alpha channels so glTF (which reads alpha for KHR_materials_specular) and Source MDL (which reads red for `$phong*`) both see the same value, and added a `Material.SpecularFactor` scalar that the PSK importer dampens for Fortnite materials so the dielectric specular response matches the subtle in-game look.
- Migrated the texture pipeline from Magick.NET to SixLabors.ImageSharp to fix a Magick.NET encoder bug that produced 282-byte stub PNGs for baked textures, which SharpGLTF was then deduplicating across every Fortnite material.
- Switched the CUE4Parse-Natives build to compile from the submodule's CMake target rather than vendoring a NuGet-packaged DLL, matching FortnitePorting's setup. Builds now require CMake and a C++ compiler on PATH; CI images already include both.
- Resolved Unreal simple construction script mesh parts through the component hierarchy so child mesh transforms inherit parent scene component transforms.
- Avoided duplicate Unreal blueprint scene parts by treating resolved simple construction script meshes as authoritative before falling back to CDO or superclass scraping.
- Filtered origin-only duplicate Unreal scene parts when the same mesh/material/scale also resolves with a more specific component transform.
- Improved Fortnite blueprint scene transforms and material matching by composing nested component transforms with Unreal's transform math, scoring material textures against the mesh name, and ignoring broad SFX emissive textures on non-emissive mesh parts.
- Allowed the CLI `psk` input format to accept generated `.ue4scene` manifests for Unreal multi-part exports.
- Tightened Unreal scene part deduplication for negative-scale Blueprint components and weighted Fortnite material layer selection toward the actual mesh name, following FortnitePorting's parameter-mapping approach.
- Changed Fortnite material texture selection to prefer deterministic shader-parameter slots such as `Diffuse_Texture_2`, `Normals_Texture_2`, and `SpecularMasks_2` before falling back to texture-name scoring.
- Changed Unreal Blueprint SCS transform resolution to prefer component `AttachParent` absolute transforms and only compose SCS parent transforms when cooked components do not expose attachment metadata.
- Improved UE4/UE5 material import reliability by writing per-mesh resolved material sidecars with exact texture slots and preferring local sidecars during PSK import.
- Added UE animation PSA export for `AnimSequence`, `AnimMontage`, and `AnimComposite` assets via a new Set Animation action in the Explorer that exports the animation to a PSA sidecar and sets it as the active animation on the Convert page.
- Resolved `UBuildingTextureData.OverrideMaterial` when writing Fortnite PPID texture data sidecars so material slots that replace their base material via `OverrideMaterial` use the correct override material textures instead of the original mesh material textures.
- Added per-part Y-axis mirror correction for Fortnite SCS root-level components (negate translation Y plus the rotation's X/Z imaginary parts so children inherit through normal composition) so wing-side, side-door, and rear-door variants land on the side of the actor pivot that matches the in-game LAAT.
- Added PSK-level mirror handling for negative-determinant scene transforms: reverse the triangle winding so post-mirror front faces are still front, and apply the inverse-transpose of the scale to normals so mirrored meshes are lit from the correct side instead of inverted.
- Bake the per-material `BakedUv0Scale` into SMD wedge UVs at MDL export time so tile-extended multi-layer textures sample within `[0,1]` of their baked PNG instead of running off the edge in Source (which has no equivalent of glTF's `KHR_texture_transform`).

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

[Unreleased]: https://github.com/gmod-workshop/gmconverter/compare/v1.7.0...HEAD
[1.7.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.6.1...v1.7.0
[1.6.1]: https://github.com/gmod-workshop/gmconverter/compare/v1.6.0...v1.6.1
[1.6.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.3.1...v1.4.0
[1.3.1]: https://github.com/gmod-workshop/gmconverter/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/gmod-workshop/gmconverter/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/gmod-workshop/gmconverter/releases/tag/v1.0.0
