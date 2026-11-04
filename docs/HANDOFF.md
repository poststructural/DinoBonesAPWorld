# Dino Bones AP: handoff (2026-11-03, native loader)

## Done this session (plan: docs/plans/2026-11-04-native-loader.md)
- Plugin 0.2.0 has no BepInEx or Harmony. `Plugin.Boot` runs from a `RuntimeInitializeOnLoads.json` entry
  (BeforeSceneLoad) and creates a DontDestroyOnLoad `Runner`, which handles the poll, the scans and the overlay.
  Logs go to Player.log and `<bridge root>/plugin.log`.
- Hooks (`Hooks.cs`, was `Patches.cs`) use `[Hook(typeof(T), "Method", args...)]` with Harmony conventions.
  `Weaver.cs` uses Mono.Cecil 0.10.4 (NuGet, shipped next to the plugin) to weave them into the IL of
  `DinoMuseum.dll` (14 methods) and `IndieSouls.Runtime.dll` (5 methods), **in place**. The originals are kept as
  `*.dll.orig`. A marker type `DinoBonesAP.Woven.Marker.Stamp` holds the hook-set version, the hook-signature
  hash and the original-DLL hash. When the stamp is stale, the plugin re-weaves and asks for a restart.
- `tools/install_windows.ps1` is new, and `tools/install_mac.sh` was rewritten (JXA JSON edits, no Rosetta or
  BepInEx). Both have an uninstall option, which puts the `.orig` files back and restores the JSON backups.
- Verified on Windows with BepInEx disabled (`version.dll.off`):
  - Launch 1 wove and showed the restart notice.
  - Launch 2 had the hooks active, the bridge connected, and the save redirected by the woven SavePaths prefix.
  - The stale-stamp re-weave worked.
  - After uninstall the DLL hashes were back to the originals and the game launched vanilla.
  - Player.log showed 0 exceptions and 0 missing scripts in every run.
  - The JXA logic was tested under node with a shim, and round-trips the JSON byte for byte.

## State
- Build: `cd plugin/DinoBonesAP && dotnet build -c Release`. This puts `DinoBonesAP.dll` and `Mono.Cecil.dll` in `release/`.
- Install into the game copy: `powershell -ExecutionPolicy Bypass -File tools/install_windows.ps1 -GamePath "<game>"`.
- The game copy has the native loader installed and woven. BepInEx is disabled (`version.dll.off`), and its old
  plugin was removed.
- **Not verified:** gameplay hooks (bone gating, checks, equipment, goal) beyond the main menu. The Mac installer
  and the weave on arm64 Mono have not been run on a Mac.

## Remaining
1. Play-test with a real AP server (plan: docs/plans/2026-11-03-dino-bones-ap.md). Check gating, checks and the
   counts dump.
2. Austin tests `install_mac.sh` on his Apple Silicon Mac. Expect launch 1 to show the restart notice and launch 2
   to log "hooks active" in `~/DinoBonesAP/plugin.log`.
3. Release 0.2.0 assets: `DinoBonesAP.dll`, `Mono.Cecil.dll`, `install_windows.ps1`, `install_mac.sh`,
   `dino_bones.apworld`. Ask Austin before pushing.

## Decisions and dead ends
- **Woven copy under a new file name (`DinoMuseum.AP.dll` in ScriptingAssemblies.json) does not work.** It loads
  and the hooks run, but Unity binds scene MonoBehaviours to the assembly by file name. Player.log then reports
  `The referenced script (DinoMuseum.MenuPreview.MenuSnowGlobePreviewDriver) ... is missing`. So weaving is in place.
- Windows does not lock loaded managed DLLs, because Unity reads them into memory. Rename and overwrite while the
  game runs both work (tested). `File.Replace` swaps atomically.
- The installers own all manifest edits. The plugin edits no JSON.
- Bone checks count "Nth bone placed on species S", not mesh names. No shop exists. The garden count is 7 until
  the dump confirms it.

## Continuation prompt
Read `DinoBonesAPWorld/docs/HANDOFF.md` and `docs/plans/2026-11-04-native-loader.md`. Next: help Austin run
`tools/install_mac.sh` on his Apple Silicon Mac. Acceptance check: the second launch logs
`[DinoAP] hooks active (19 hooks ...)` and `save redirected to ...` in `~/DinoBonesAP/plugin.log`. Then play-test
the gating with a real AP server.
