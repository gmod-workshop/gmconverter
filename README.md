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
| `--game <game>` | Game the input comes from. Its naming conventions fill in what each animation is for, which the Garry's Mod and Source targets turn into activities. Unreal games: `swzc`, `fortnite`, or `unreal` for any other. The Explorer sets this automatically in the GUI. | `--game swzc` | None |
| `--role <clip=role>` | Set what an animation is for, replacing the guess from its name. A role is an action (`idle`, `combat-idle`, `walk`, `jog`, `run`, `crouch-idle`, `crouch-walk`, `turn`, `jump`, `fall`, `land`, `attack`, `melee-attack`, `reload`, `flinch`, `stagger`, `death`), optionally followed by a stance and direction (`run,rifle,forward`), or `none` for no role. Repeat for several animations. | `--role A_B1_2HRifle_Jog_F=run` | Guessed |
| `--target <game>` | Game to export for, for output formats that have several. MDL: `garrysmod` or `source` (any other Source game). | `--target source` | `garrysmod` for MDL |

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
| `--psk-import-animationPath <path.psa>` | `--animation-path` | PSA animation files to import alongside the mesh. Separate several files with `;`. | None |
| `--psk-import-blendSpacePath <path.blendspace.json>` | `--blend-space-path` | Blend space files the Explorer writes when it resolves a blend space or aim offset, describing how the imported animations blend. Separate several files with `;`. | None |

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
| `--mdl-physics-mode <mode>` | `--physics-mode` | Collision mode: `auto`, `bounds` or `coacd`. Takes effect with `--physics`. See [Physics](#physics). | `auto` |
| `--mdl-physics-mass <value>` | `--physics-mass` | Physics mass in kilograms. | `100` |
| `--mdl-physics-coacdThreshold <value>` | `--coacd-threshold` | CoACD termination threshold. | `0.05` |
| `--mdl-physics-maxConvexPieces <count>` | `--max-convex-pieces` | Maximum CoACD convex hull count. Use `-1` for no limit. | `16` |
| `--mdl-physics-maxHullVertices <count>` | `--coacd-max-hull-vertices`, `--max-hull-vertices` | Maximum vertices per CoACD hull. | `16` |

</details>

<details>
<summary>Garry's Mod Target Options</summary>

These apply when MDL output targets Garry's Mod (the default).

| Option | Description | Default |
|---|---|---|
| `--garrysmod-characterUse <use>` | Which activities animations get: `npc` (`ACT_IDLE`, `ACT_RUN`, …), `player` (`ACT_HL2MP_*` for player models), or `both`, which writes a `<name>_player` copy of each sequence for the player set. | `npc` |
| `--garrysmod-handAttachments <true\|false>` | Add `anim_attachment_RH` and `anim_attachment_LH` on the hands, copied from the model's hand sockets (or placed on the hand bones), which Garry's Mod uses to place held items. | `true` |

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

PSA files are supported as animation sidecars for PSK/PSKX. Pass a matching PSA with `--animation-path` (`--psk-import-animationPath`) to export animation clips to glTF/GLB or Source `$sequence` SMDs. To bundle several animations, separate the PSA paths with `;`. For long lists, put the arguments in a response file (one per line) and pass `@args.rsp`, because Windows limits command lines to about 32,000 characters.

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

The Explorer can scan Unreal Engine 4 and 5 `.pak` / `.utoc` archives through CUE4Parse and resolve `StaticMesh` and `SkeletalMesh` assets into the normal conversion workflow. Select the game's install folder, the archive directory, or a `Content` directory that contains a `Paks` folder.

The **Game** box chooses which game's settings are used. On **Auto-detect**, the Explorer recognises the game from the folder and shows it in the scan status; pick a game yourself if detection gets it wrong. Recognised games:

* **Fortnite** uses the Fortnite package version, fetches current AES keys and mappings from UEDB, downloads the mapping file to the local app-data cache, and submits those keys to CUE4Parse before scanning.
* **Star Wars Zero Company** is recognised from `SWZeroCompany.exe`.
* **Other Unreal Engine game** covers every other UE4/UE5 archive with generic settings, and may need future game-specific key or version support.

Most cooked UE5 games (for example Star Wars Zero Company) store "unversioned" properties. The Explorer can still list their assets, but meshes, materials and textures can't be read without a `.usmap` mappings file that matches the game's current build. Dump one from the running game with a tool such as Dumper-7 or UE4SS, then place it in the game's `Paks` folder or anywhere up to the game's install folder. GMConverter picks up the newest `.usmap` it finds there when it opens the archives, so restart GMConverter after adding one, and dump a new file after each game update.

Skeletal meshes and animations are matched by the skeleton they were made for. To add animations to a model, double-click the skeletal mesh to load it, select it again and click **Related** to list the animations made for its skeleton, then select one or more (Ctrl- or Shift-click) and click **Add Animations**. Each one becomes a separate sequence in the exported model. To bundle every animation for the skeleton automatically, turn on **Include all compatible animations** before previewing or exporting the mesh. This includes animation sequences and blend spaces; add montages by hand.

Blend spaces and aim offsets can be added the same way. Their sample animations come along, and MDL output turns aim offsets into an aim layer driven by the `aim_yaw` and `aim_pitch` pose parameters that Source NPC code sets, so a character points its weapon where it aims. Blend spaces whose axes have no Source pose parameter (such as hit-reaction pose blends) are left out, since Source models only have room for a few pose parameters. The preview always shows the model in its rest pose.

Additive animations (layers such as aim poses, flinches and hit reactions that the game adds on top of another animation) keep that role in MDL output: each becomes a `delta` sequence measured against the pose it was authored for, so it can be played as a layer or gesture in Garry's Mod. Played on its own, a delta sequence does not show a normal pose. Formats without layers, such as glTF, receive the layer already applied to its base pose. Layers that Unreal applies in model space are applied in each bone's local space by Source, so they match exactly on their own base pose and approximately on others.

Animations that use Unreal's root motion (the character travels or turns with its root bone, as walk cycles, starts, stops and turns usually do) carry that movement in MDL output, so Garry's Mod moves NPCs and players at the speed their feet step. Animations Unreal marks as looping, such as idles and walk cycles, loop in MDL output.

Sockets on Unreal meshes and skeletons (named attach points such as a weapon hand, a muzzle or an effect point) are exported as MDL `$attachment`s on the same bone, so Garry's Mod can attach weapons, effects and props to them.

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

MDL output targets Garry's Mod by default, which adds the attachments Garry's Mod looks up by name (see the Garry's Mod target options).

Garry's Mod and other Source games choose animations by *activity*. When the game an animation comes from is known (`--game`, or set from the Explorer in the GUI), GMConverter works out what each animation is for from its name and gives its sequence the matching activity: idles, walks, runs, turns, attacks, flinches and deaths. Unreal names such as `A_B1_2HRifle_Run_F` (a rifle run, forward) are recognised; starts, stops, transitions, aim poses and additive layers get no activity. NPC activities are used by default; set the Garry's Mod character use to `player` or `both` for player models.

If a guess is wrong, override it with `--role clip=role` in the CLI, or in the GUI's **Animation roles** table, which lists the animations after you load a preview. Use `--target source`, or choose **Other Source game** as the **Target game** in the GUI, for plain output for other Source games.

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

The default `auto` mode uses the model's own collision when it has one, and a simple collision box otherwise.

<details>
<summary>Ragdolls</summary>

Unreal Engine 4/5 skeletal meshes that come with a physics asset are exported as a ragdoll. Each collision body (sphere, box, capsule or convex hull) becomes a convex piece on its bone in `$collisionjoints`, and each constraint's angular limits become `$jointconstrain` limits on the child bone, so the model falls apart and goes limp in Garry's Mod. `--physics-mass` sets the whole ragdoll's mass, which studiomdl shares out between the parts by volume.

The same bodies are also written as hitboxes (`$hbox`), whether or not `--physics` is set. Hit groups (head, chest, stomach, arms, legs) are guessed from bone names, such as `head`, `spine_02` or `upperarm_l`. Bones they can't be guessed for are generic.

Unreal limits rotation about a constraint's own axes, while Source limits it about the bone's axes. Each limit goes to the bone axis closest to the constraint axis, so limits on constraints that are turned relative to their bone are approximate.

```powershell
./GMConverter.CLI --input-format psk --output-format mdl `
  --input-path "SK_B1_Battledroid.ue4scene" `
  --output-path "out1-source" `
  --physics
```

Use `--physics-mode bounds` or `--physics-mode coacd` to replace the ragdoll with a single rigid collision model.

</details>

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
