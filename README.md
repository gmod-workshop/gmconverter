# GMConverter

Tools for converting model assets into Source Engine compile inputs for Garry's Mod.

**Features**

* Browse game archives and assets in the GUI Explorer.
* Preview models with animation and basic Source shading.
* Convert models to Source MDL with animation, materials, and optional physics.
* Convert models to glTF with animation for import into Blender.

## Supported Formats

| Format                | Read | Write | Mesh       | Materials / Textures | Bones / Weights | Animations |
|-----------------------| --- | --- |------------|----------------------|-----------------| --- |
| `OPT`                 | Yes | No | Read       | Read                 | -               | - |
| `MDL`                 | Yes* | Yes* | Read/Write | Read*/Write          | Write           | Write |
| `PSK` / `PSKX`        | Yes* | No | Read       | Read*                | Read            | Read* |
| `UE4` / `UE5` archives | Yes* | No | Read       | Read*                | Read            | - |
| `MOW` (`DEF` / `MDL`) | Yes* | No | Read | Read* | Read* | Read* |
| `OBJ` / `MTL`         | No | Yes | Write      | Write                | -               | - |
| `glTF` / `GLB`        | No | Yes | Write      | Write                | Write           | Write |

`*` See [Format Details](#format-details) for caveats.

`-` Unsupported by format.

## CLI

```powershell
./GMConverter.CLI --input-format psk --output-format mdl `
  --input-path "SkeletalMesh\BactaDispenserRAS.psk" `
  --animation-path "MeshAnimation\BactaDispenserRASSet.psa" `
  --material-dir "E:\Tools\umodel\UmodelExport" `
  --output-path "out\bacta-source" `
  --model-path "gmconverter/bactadispenserras.mdl"
```

<details>
<summary>Common Options</summary>

These apply to every input and output format.

| Option | Description | Example | Default |
| --- | --- | --- | --- |
| `--input-format <format>` | Input format: `opt`, `mdl`, `psk`, or `mow`. | `--input-format psk` | Required |
| `--output-format <format>` | Output format: `info`, `obj`, `glb`, `gltf`, `mdl`, or `source` (same as `mdl`). | `--output-format mdl` | Required |
| `--input-path <path>` | Input model path. | `--input-path "SkeletalMesh\model.psk"` | Required |
| `--output-path <path>` | Output directory. Required except for `info`. | `--output-path "out\model"` | Required except `info` |
| `--name <base-name>` | Override generated file names. | `--name bacta_dispenser` | Input file name |
| `--material-dir <path>` | Recursive search directory for sidecar materials and textures. | `--material-dir "E:\Tools\umodel\UmodelExport"` | None |
| `--scale <factor>` | Scale exported geometry. | `--scale 0.5` | `1` |
| `--no-scale` | Compatibility alias for scale `1`. | `--no-scale` | Off |
| `--axis-mode <mode>` | Input axis convention: `auto`, `z-up`, or `y-up`. | `--axis-mode y-up` | `auto` |

</details>

### Format Options

Each importer and exporter adds its own options, which only apply when that format is selected:

- Exporter options are `--<format>-<option>`, for example `--mdl-physics-mode`.
- Importer options are `--<format>-import-<option>`, for example `--psk-import-animationPath`. The extra `import` keeps them apart from exporter options, since a format such as `mdl` can be both read and written.
- Many options also have a shorter alias, such as `--physics-mode`. Either spelling works.
- Boolean options can be given bare (`--physics`) or with `true`/`false`. An alias starting with `--no-` sets the opposite value.

Passing an option for a format that isn't selected is an error. The same keys and aliases work in [config files](#gui), and in the GUI they appear under **Import options** and **Export options** on the Convert page.

<details>
<summary>PSK / PSKX Import Options</summary>

| Option | Alias | Description | Default |
| --- | --- | --- | --- |
| `--psk-import-animationPath <path.psa>` | `--animation-path` | PSA animation file to import alongside the mesh. | None |

</details>

<details>
<summary>MDL (Source) Export Options</summary>

| Option | Alias | Description | Default |
| --- | --- | --- | --- |
| `--mdl-modelPath <path/name.mdl>` | `--model-path` | MDL path under the game `models` directory. | `gmconverter/<name>.mdl` |
| `--mdl-studioMdlPath <path>` | `--studiomdl-path`, `--studiomdl` | `cestudiomdl.exe` override. | Auto-downloaded to `tools` |
| `--mdl-vtfCmdPath <path>` | `--vtfcmd-path`, `--vtfcmd` | `VTFCmd.exe` override. | Auto-downloaded to `tools` when materials are built |
| `--mdl-buildMaterials <true\|false>` | `--no-materials` (sets `false`) | Compile VTFs and VMTs alongside the MDL. | `true` |
| `--mdl-material-maxTextureSize <size>` | `--max-texture-size` | Cap the longest texture edge before VTF compile: `0`, `512`, `1024`, `2048`, or `4096`. `0` disables resizing. | `0` |
| `--mdl-material-deduplicateTextures [true\|false]` | `--deduplicate-textures` | Reuse one VTF for byte-identical textures. | `false` |
| `--mdl-physics-enabled [true\|false]` | `--physics` | Generate a collision model. | `false` |
| `--mdl-physics-mode <mode>` | `--physics-mode` | Collision mode: `bounds` or `coacd`. Takes effect with `--physics`. See [Physics](#physics). | `bounds` |
| `--mdl-physics-mass <value>` | `--physics-mass` | Physics mass in kilograms. | `100` |
| `--mdl-physics-coacdThreshold <value>` | `--coacd-threshold` | CoACD termination threshold. | `0.05` |
| `--mdl-physics-maxConvexPieces <count>` | `--max-convex-pieces` | Maximum CoACD convex hull count. Use `-1` for no limit. | `16` |
| `--mdl-physics-maxHullVertices <count>` | `--coacd-max-hull-vertices`, `--max-hull-vertices` | Maximum vertices per CoACD hull. | `16` |

</details>

<details>
<summary>glTF / GLB Export Options</summary>

These use the `--glb-` prefix for both `glb` and `gltf` output. Binary or text output follows `--output-format`.

| Option | Description | Default |
| --- | --- | --- |
| `--glb-bakeUvTransforms [true\|false]` | Fold per-material UV scale and offset into the mesh UVs, for viewers that ignore `KHR_texture_transform`. | `false` |

</details>

OPT, MOW and MDL import, and OBJ export, have no format options.

## GUI

`GMConverter.UI` provides a frontend for the same conversion library, with some extra features.

```powershell
git submodule update --init --recursive
./GMConverter.UI
```

<details>
<summary>GUI Preview</summary>

![GUI Preview](https://i.imgur.com/X0HBj7g.png)

</details>

The GUI auto-loads the first `gmconverter.ini` it finds in the current directory. Config keys are the common option names plus any [format option](#format-options) key or alias. Case, `-` and `_` are ignored, and keys that match nothing are reported in the console.

<details>
<summary>Example Config</summary>

```ini
# gmconverter.ini
output-format = mdl
# Optional compiler overrides. Leave unset to use portable tools downloaded to ./tools.
# studiomdl-path = E:\Tools\cestudiomdl.exe
# vtfcmd-path = E:\Tools\VTFCmd.exe
material-dir = E:\Tools\umodel\UmodelExport
model-path = gmconverter/bactadispenserras.mdl
axis-mode = auto
no-materials = false
physics = true
physics-mode = bounds
physics-mass = 100
```

</details>

## Plugins

Unreal Engine, Source Engine, Men of War and X-Wing Alliance support ship as plugins. Each plugin lives in its own directory under `plugins/` beside the executable, with a `plugin.json` manifest, its entry assembly, and runtime dependencies. Both the GUI and CLI load plugins at startup; restart the application after installing a plugin.

Each plugin declares its own options; see [Format Options](#format-options).

## Format Details

<details>
<summary>OPT</summary>

X-Wing Alliance OPT files are supported as input. The importer reads mesh geometry, material slots, textures, and model statistics. OPT does not carry skeletal animation data in the current converter.

Print model statistics and size checks:

```powershell
./GMConverter.CLI --input-format opt --output-format info --input-path "FlightModels\buoyc.opt"
```

Export diagnostic OBJ, MTL, and PNG assets:

```powershell
./GMConverter.CLI --input-format opt --output-format obj `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-obj"
```

Outputs mesh, LOD, texture, face, and vertex counts, plus bounding-box sizes at Source scale and OPT library display scale.

</details>

<details>
<summary>PSK / PSKX</summary>

Unreal ActorX PSK/PSKX files are supported as input. The importer reads mesh geometry, UVs, material slots, skeleton bind data, skin weights, and PSKX vertex normals when present.

ActorX coordinates use centimeters and are normalized to meters on import, including bone positions and PSA animation translations. Keep `--scale 1` for the original physical size; Source export converts meters to inches automatically. Scene manifests use the same normalization.

Use `--material-dir` to resolve UModel-style `.mat` sidecars and texture files. Diffuse, normal, specular, opacity, and emissive references are supported. If a material has no explicit normal map reference, nearby diffuse-name `_normal`, `_norm`, or `_bump` textures are used as normal-map fallbacks.

PSA files are supported as animation sidecars for PSK/PSKX. Pass a matching PSA with `--animation-path` (`--psk-import-animationPath`) to export animation clips to glTF/GLB or Source `$sequence` SMDs.

```powershell
./GMConverter.CLI --input-format psk --output-format glb `
  --input-path "SkeletalMesh\BactaDispenserRAS.psk" `
  --animation-path "MeshAnimation\BactaDispenserRASSet.psa" `
  --output-path "out\bacta-glb" `
  --material-dir "E:\Tools\umodel\UmodelExport"
```

</details>

<details>
<summary>Unreal Engine 4 / 5 Archives</summary>

The Explorer can scan Unreal Engine 4 and 5 `.pak` / `.utoc` archives through CUE4Parse and resolve `StaticMesh` and `SkeletalMesh` assets into the normal conversion workflow. Select either the archive directory or a game `Content` directory that contains a `Paks` folder.

Fortnite installations use a dedicated Unreal profile. When detected, GMConverter uses the Fortnite package version, fetches current AES keys and mappings from UEDB, downloads the mapping file to the local app-data cache, and submits those keys to CUE4Parse before scanning. Other UE4/UE5 archives use the generic profile and may require future game-specific key or version support.

</details>

<details>
<summary>MDL</summary>

Source MDL files are supported as input and output. MDL read support decompiles reference SMD meshes through MdlCrowbar and currently imports static reference mesh geometry and material references, not full compiled animation data.

MDL write support generates SMD, QC, material files, optional animation SMDs, and compiles the final MDL with `cestudiomdl`. Material builds use `VTFCmd` to write VTF textures. If `--mdl-studioMdlPath` or `--mdl-vtfCmdPath` (or their `--studiomdl-path` / `--vtfcmd-path` aliases) is omitted, GMConverter downloads portable defaults into a `tools` folder next to the executable.

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --model-path "gmconverter/buoyc.mdl"
```

Copy the generated `models` and `materials` folders from the output directory into your Garry's Mod add-on or game content folder.

</details>

<details>
<summary>Men of War (MOW)</summary>

Men of War / Assault Squad 2 extracted assets are supported with `--input-format mow`. The importer accepts either the entity `.def` file or the referenced `.mdl` file. `.def` input resolves the first `Extension` node to find the model.

The current importer reads the `.mdl` skeleton tree, loads each bone `VolumeView` binary `EPLYBNDS` `.ply`, parses the referenced `.mtl`, resolves local `.dds` diffuse/specular textures, and imports local `.anm` animation files. Meshes are rigidly weighted to their owning bones.

```powershell
./GMConverter.CLI --input-format mow --output-format glb `
  --input-path "bddispenser\bddispenser.def" `
  --output-path "out\bddispenser-glb"
```

</details>

<details>
<summary>OBJ / MTL</summary>

OBJ export is intended for diagnostics and static interchange. It writes OBJ, MTL, and PNG texture files. Materials include diffuse maps, alpha maps, specular maps, normal maps via `map_Bump`, and emissive maps where available. OBJ does not support bones, skin weights, or animations.

</details>

<details>
<summary>glTF / GLB</summary>

glTF/GLB export writes portable mesh assets with materials, normal maps, skeletons, skin weights, and animations. `glb` writes a single binary file with embedded buffers and images. `gltf` writes a JSON glTF file with satellite resources.

</details>

## Physics

<details>
<summary>Bounds</summary>

Generate a simple convex collision box:

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --physics `
  --physics-mass 250
```

</details>

<details>
<summary>CoACD</summary>

Generate a [CoACD](https://colin97.github.io/CoACD/) based collision mesh:

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --physics `
  --physics-mode coacd `
  --max-convex-pieces 16
```

</details>

## Credits

- [Ab4d.SharpEngine](https://www.ab4d.com/SharpEngine.aspx) - Avalonia 3D preview rendering
- [DarklightGames/io_scene_psk_psa](https://github.com/DarklightGames/io_scene_psk_psa) - PSK/PSA support
- [colin97/CoACD](https://github.com/colin97/CoACD) - Convex decomposition
