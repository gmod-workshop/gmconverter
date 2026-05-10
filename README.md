# GMConverter

Tools for converting model assets into Source Engine compile inputs for Garry's Mod.

## Supported Formats

| Format | Read | Write | Mesh       | Materials / Textures | Bones / Weights | Animations |
| --- | --- | --- |------------|----------------------|-----------------| --- |
| `OPT` | Yes | No | Read       | Read                 | -               | - |
| `MDL` | Yes* | Yes* | Read/Write | Read*/Write          | Write           | Write |
| `PSK` / `PSKX` | Yes* | No | Read       | Read*                | Read            | Read* |
| `OBJ` / `MTL` | No | Yes | Write      | Write                | -               | - |
| `glTF` / `GLB` | No | Yes | Write      | Write                | Write           | Write |

`*` See [Format Details](#format-details) for caveats.

`-` Unsupported by format.

## CLI

```powershell
./GMConverter.CLI --input-format psk --output-format mdl `
  --input-path "SkeletalMesh\BactaDispenserRAS.psk" `
  --animation-path "MeshAnimation\BactaDispenserRASSet.psa" `
  --material-dir "E:\Tools\umodel\UmodelExport" `
  --output-path "out\bacta-source" `
  --game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod" `
  --model-path "gmconverter/bactadispenserras.mdl"
```

| Option | Description | Example | Default |
| --- | --- | --- | --- |
| `--input-format <format>` | Input format: `opt`, `mdl`, or `psk`. | `--input-format psk` | Required |
| `--output-format <format>` | Output format: `info`, `obj`, `glb`, `gltf`, `source`, or `mdl`. | `--output-format mdl` | Required |
| `--input-path <path>` | Input model path. | `--input-path "SkeletalMesh\model.psk"` | Required |
| `--output-path <path>` | Output directory. Required except for `info`. | `--output-path "out\model"` | Required except `info` |
| `--name <base-name>` | Override generated file names. | `--name bacta_dispenser` | Input file name |
| `--model-path <path/name.mdl>` | MDL path under the game `models` directory. | `--model-path "gmconverter/model.mdl"` | `gmconverter/<name>.mdl` |
| `--game-dir <path>` | Source game directory containing `gameinfo.txt`. | `--game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod"` | Required for `source` / `mdl` |
| `--engine-dir <path>` | Optional Source engine root override. | `--engine-dir "E:\Games\Steam\steamapps\common\GarrysMod"` | Inferred from `gameinfo.txt` |
| `--material-dir <path>` | Recursive search directory for sidecar materials and textures. | `--material-dir "E:\Tools\umodel\UmodelExport"` | None |
| `--animation-path <path.psa>` | PSA animation file to import alongside PSK/PSKX. | `--animation-path "MeshAnimation\model.psa"` | None |
| `--scale <factor>` | Scale exported geometry. | `--scale 0.5` | `1` |
| `--no-scale` | Compatibility alias for scale `1`. | `--no-scale` | Off |
| `--axis-mode <mode>` | Input axis convention: `auto`, `z-up`, or `y-up`. | `--axis-mode y-up` | `auto` |
| `--no-materials` | Skip VTF/VMT compilation. | `--no-materials` | Off |
| `--physics` | Generate bounds collision for Source output. | `--physics` | Off |
| `--physics-mode <mode>` | Collision generation mode: `bounds` or `coacd`. | `--physics-mode coacd` | `bounds` |
| `--physics-mass <value>` | Physics mass for Source collision. | `--physics-mass 250` | `100` |
| `--coacd-threshold <value>` | CoACD termination threshold. | `--coacd-threshold 0.05` | `0.01` |
| `--max-convex-pieces <count>` | Maximum CoACD convex hull count. Use `-1` for no limit. | `--max-convex-pieces 16` | `32` |
| `--coacd-max-hull-vertices <count>` | Maximum vertices per CoACD hull. | `--coacd-max-hull-vertices 32` | `32` |

## GUI

`GMConverter.GUI` provides a simple Windows frontend for the same conversion library. Build the solution and run:

```powershell
./GMConverter.GUI
```

The GUI calls the converter library directly and streams library logs into the output panel.

## Format Details

### OPT

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

### PSK / PSKX

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

### MDL

Source MDL files are supported as input and output. MDL read support decompiles reference SMD meshes through MdlCrowbar and currently imports static reference mesh geometry and material references, not full compiled animation data.

MDL write support generates SMD, QC, material files, optional animation SMDs, and optionally compiles the final MDL with `studiomdl`.

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod" `
  --model-path "gmconverter/buoyc.mdl"
```

`--game-dir` is required and must point to a Source game directory containing `gameinfo.txt`. The tool reads `gameinfo.txt` to identify the game and infer the engine root from `gamebin`.

### OBJ / MTL

OBJ export is intended for diagnostics and static interchange. It writes OBJ, MTL, and PNG texture files. Materials include diffuse maps, alpha maps, specular maps, normal maps via `map_Bump`, and emissive maps where available. OBJ does not support bones, skin weights, or animations.

### glTF / GLB

glTF/GLB export writes portable mesh assets with materials, normal maps, skeletons, skin weights, and animations. `glb` writes a single binary file with embedded buffers and images. `gltf` writes a JSON glTF file with satellite resources.

## Physics

### Bounds

Generate a simple convex collision box:

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod" `
  --physics `
  --physics-mass 250
```

### CoACD

Generate a [CoACD](https://colin97.github.io/CoACD/) based collision mesh:

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod" `
  --physics-mode coacd `
  --max-convex-pieces 16
```

## Credits

- [DarklightGames/io_scene_psk_psa](https://github.com/DarklightGames/io_scene_psk_psa) - PSK/PSA support
- [colin97/CoACD](https://github.com/colin97/CoACD) - Convex decomposition
