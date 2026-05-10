# GMConverter

Tools for converting model assets into Source Engine compile inputs for Garry's Mod.

## Supported Formats

- `MDL` - Source Engine
- `OPT` - X-Wing Alliance

## Commands

```powershell
./GMConverter.CLI --input-format opt --output-format info --input-path <input.opt>
./GMConverter.CLI --input-format opt --output-format obj --input-path <input.opt> --output-path <output-dir>
./GMConverter.CLI --input-format opt --output-format mdl --input-path <input.opt> --output-path <output-dir> --game-dir <path-to-game-dir>
```

## GUI

`GMConverter.GUI` provides a simple Windows frontend for the same conversion library. Build the solution and run:

```powershell
./GMConverter.GUI
```

The GUI calls the converter library directly and streams library logs into the output panel.

## OPT

### Info

Print model statistics and size checks.

```powershell
./GMConverter.CLI --input-format opt --output-format info --input-path "FlightModels\buoyc.opt"
```

Outputs mesh, LOD, texture, face, and vertex counts, plus bounding-box sizes at Source scale and OPT library display scale.

### OBJ

Export diagnostic OBJ, MTL, and PNG assets.

```powershell
./GMConverter.CLI --input-format opt --output-format obj `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-obj"
```

Useful for checking geometry before Source compilation.

### Source

Generate SMD, QC, material files, and optionally compile the MDL.

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod" `
  --model-path "gmconverter/buoyc.mdl"
```

`--game-dir` is required and must point to a Source game directory containing `gameinfo.txt`. The tool reads `gameinfo.txt` to identify the game and infer the engine root from `gamebin`.

## Common Options

- `--input-format <format>`: Input format. Currently `opt`.
- `--output-format <format>`: Output format. `info`, `obj`, `source`, or `mdl`.
- `--input-path <path>`: Input model path.
- `--output-path <path>`: Output directory. Required except for `--output-format info`.
- `--name <base-name>`: Override generated file names.
- `--model-path <path/name.mdl>`: Output path under the game `models` directory.
- `--game-dir <path>`: Required for `source` and `mdl` output.
- `--engine-dir <path>`: Optional override when tools cannot be inferred from `gameinfo.txt`.
- `--scale <factor>`: Scale exported geometry. Default is `1`.
- `--no-scale`: Equivalent to the default scale behavior; kept for command compatibility.
- `--axis-mode <mode>`: Input model axis convention. `auto` uses Z-up defaults, `z-up` applies no axis rotation, `y-up` converts Y-up input to Source Z-up.
- `--no-materials`: Skip VTF/VMT compilation.

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

Generate a CoACD-based collision mesh:

```powershell
./GMConverter.CLI --input-format opt --output-format mdl `
  --input-path "FlightModels\buoyc.opt" `
  --output-path "out\buoyc-source" `
  --game-dir "E:\Games\Steam\steamapps\common\GarrysMod\garrysmod" `
  --physics-mode coacd `
  --max-convex-pieces 16
```

CoACD is downloaded at build time as a native library. The converter calls it directly; Python is not required.

## Materials

When compiler tools are found, textures are converted automatically:

- TGA sources: `materialsrc/<material-path>/`
- VTF output: `materials/<material-path>/`
- VMT output: beside the compiled VTF files

Use `--no-materials` to skip this.

## Notes

- Source MDLs are produced by `studiomdl`; this tool writes SMD/QC inputs and invokes the compiler when available.
- OPT coordinates export as Source inches by default.
