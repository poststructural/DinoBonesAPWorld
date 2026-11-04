# Dino Bones Archipelago

An [Archipelago](https://archipelago.gg) world and game plugin for **DinoBones: Tidy Up the Museum**,
for Windows and macOS.

Rebuild the museum's 9 skeletons while your Exhibit Permits, the Polaroid Camera, Glider, Watering Can and
Crate Hammer are scattered across the multiworld. A skeleton accepts bones only once its permit arrives.

## Checks

| Option | Checks |
|---|---|
| always | 9 skeletons completed, 9 secrets revealed |
| `bonesanity` | off / every 10% of each skeleton (90) / every bone (1,940) |
| `collectionsanity` | 9 coins, 9 cassettes, 9 books |
| `photosanity` | 9 exhibit photos, the cat, the model and snow globe |
| `gardensanity` | 7 garden beds |
| `kitsanity` | 6 model-kit pieces |
| `supportsanity` | 17 skeleton supports (off by default) |
| `achievementsanity` | 29 achievements |

That is 188 checks by default and up to 2,055 with everything on. The goal is the endgame, or a set number
of skeletons.

## Install

No BepInEx is needed (from 0.2.0). Download `dino_bones.apworld`, `DinoBonesAP.dll`, `Mono.Cecil.dll` and the
installer for your system (`install_windows.ps1` or `install_mac.sh`) from the
[latest release](https://github.com/poststructural/DinoBonesAPWorld/releases/latest), run the installer, then start
the game once and restart it once (the first start sets up the plugin's hooks). The
[setup guide](world/dino_bones/docs/setup_en.md) has the details. Apple Silicon Macs run the game natively.

**Status:** first release. The world is tested, and the plugin is confirmed to load and keep a separate
save, but full play-throughs have not been done yet. Please report issues.

## Building

- Plugin: `cd plugin/DinoBonesAP && dotnet build -c Release -p:GameDir="<game folder>\"`. It compiles against the
  game's own `DinoBones_Data/Managed` DLLs and copies `DinoBonesAP.dll` and `Mono.Cecil.dll` (NuGet 0.10.4) to
  `release/`. Install into a game copy with `tools/install_windows.ps1 -GamePath <folder>`.
- World: copy `world/dino_bones` into an Archipelago checkout's `worlds/` folder, then run its tests with
  `python -m unittest discover -s worlds/dino_bones/test -t .`.
- `tools/scan_bones.py` re-reads the bone lists from the game data (needs UnityPy).
