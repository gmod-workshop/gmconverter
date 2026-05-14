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
<summary>CLI Options</summary>

| Option | Description | Example | Default |
| --- | --- | --- | --- |
| `--input-format <format>` | Input format: `opt`, `mdl`, `psk`, or `mow`. | `--input-format psk` | Required |
| `--output-format <format>` | Output format: `info`, `obj`, `glb`, `gltf`, `source`, or `mdl`. | `--output-format mdl` | Required |
| `--input-path <path>` | Input model path. | `--input-path "SkeletalMesh\model.psk"` | Required |
| `--output-path <path>` | Output directory. Required except for `info`. | `--output-path "out\model"` | Required except `info` |
| `--name <base-name>` | Override generated file names. | `--name bacta_dispenser` | Input file name |
| `--model-path <path/name.mdl>` | MDL path under the game `models` directory. | `--model-path "gmconverter/model.mdl"` | `gmconverter/<name>.mdl` |
| `--studiomdl-path <path>` | Optional `cestudiomdl.exe` override. | `--studiomdl-path "E:\Tools\cestudiomdl.exe"` | Auto-downloaded to `tools` |
| `--vtfcmd-path <path>` | Optional `VTFCmd.exe` override. | `--vtfcmd-path "E:\Tools\VTFCmd.exe"` | Auto-downloaded to `tools` when materials are built |
| `--material-dir <path>` | Recursive search directory for sidecar materials and textures. | `--material-dir "E:\Tools\umodel\UmodelExport"` | None |
| `--animation-path <path.psa>` | PSA animation file to import alongside PSK/PSKX. | `--animation-path "MeshAnimation\model.psa"` | None |
| `--scale <factor>` | Scale exported geometry. | `--scale 0.5` | `1` |
| `--no-scale` | Compatibility alias for scale `1`. | `--no-scale` | Off |
| `--axis-mode <mode>` | Input axis convention: `auto`, `z-up`, or `y-up`. | `--axis-mode y-up` | `auto` |
| `--no-materials` | Skip VTF/VMT compilation. | `--no-materials` | Off |
| `--physics` | Generate bounds collision for Source output. | `--physics` | Off |
| `--physics-mode <mode>` | Collision generation mode: `bounds` or `coacd`. | `--physics-mode coacd` | `bounds` |
| `--physics-mass <value>` | Physics mass for Source collision. | `--physics-mass 250` | `100` |
| `--coacd-threshold <value>` | CoACD termination threshold. | `--coacd-threshold 0.05` | `0.05` |
| `--max-convex-pieces <count>` | Maximum CoACD convex hull count. Use `-1` for no limit. | `--max-convex-pieces 16` | `16` |
| `--coacd-max-hull-vertices <count>` | Maximum vertices per CoACD hull. | `--coacd-max-hull-vertices 16` | `16` |

</details>

## GUI

`GMConverter.UI` provides a frontend for the same conversion library, with some extra features.

```powershell
./GMConverter.UI
```

<details>
<summary>GUI Preview</summary>

![GUI Preview](https://i.imgur.com/X0HBj7g.png)

</details>

The GUI auto-loads the first `gmconverter.ini` it finds in the current directory.

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
physics-mode = bounds
physics-mass = 100
```

</details>

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

Use `--material-dir` to resolve UModel-style `.mat` sidecars and texture files. Diffuse, normal, specular, opacity, and emissive references are supported. If a material has no explicit normal map reference, nearby diffuse-name `_normal`, `_norm`, or `_bump` textures are used as normal-map fallbacks.

PSA files are supported as animation sidecars for PSK/PSKX. Pass a matching PSA with `--animation-path` to export animation clips to glTF/GLB or Source `$sequence` SMDs.

```powershell
./GMConverter.CLI --input-format psk --output-format glb `
  --input-path "SkeletalMesh\BactaDispenserRAS.psk" `
  --animation-path "MeshAnimation\BactaDispenserRASSet.psa" `
  --output-path "out\bacta-glb" `
  --material-dir "E:\Tools\umodel\UmodelExport"
```

</details>

<details>
<summary>MDL</summary>

Source MDL files are supported as input and output. MDL read support decompiles reference SMD meshes through MdlCrowbar and currently imports static reference mesh geometry and material references, not full compiled animation data.

MDL write support generates SMD, QC, material files, optional animation SMDs, and compiles the final MDL with `cestudiomdl`. Material builds use `VTFCmd` to write VTF textures. If `--studiomdl-path` or `--vtfcmd-path` is omitted, GMConverter downloads portable defaults into a `tools` folder next to the executable.

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
  --physics-mode coacd `
  --max-convex-pieces 16
```

</details>

## Credits

- [Ab4d.SharpEngine](https://www.ab4d.com/SharpEngine.aspx) - Avalonia 3D preview rendering
- [DarklightGames/io_scene_psk_psa](https://github.com/DarklightGames/io_scene_psk_psa) - PSK/PSA support
- [colin97/CoACD](https://github.com/colin97/CoACD) - Convex decomposition
