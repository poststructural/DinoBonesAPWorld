# Dino Bones AP: handoff (2026-11-03)

## Done this session
- Decompiled `DinoMuseum.dll`, `Assembly-CSharp.dll`, `IndieSouls.Runtime.dll` into `research/decomp/` (git-ignored).
- `tools/scan_bones.py`: reads the `D_Atlas_*` MonoBehaviours raw (typetrees are stripped) and writes
  `research/bones.json`. Result: 1,940 bones across 9 species, 17 support parts. Every atlas parsed to its exact end.
- Plan `docs/plans/2026-11-03-dino-bones-ap.md` was approved by Austin. He confirmed species permits as the
  main gate and that the Mac build is Mono (`Contents/MonoBleedingEdge`). Contract: `docs/SPEC.md`.
- APWorld `world/dino_bones/` (Sonnet subagent, reviewed). 36 unittest tests pass.
  Location counts: 18 (all off), 188 (default), 2,055 (max).
- Plugin `plugin/DinoBonesAP/` (Sonnet subagent, reviewed) builds with 0 warnings. Codex 0.166.0 ran a
  read-only review for blocking issues only. It found one: connecting after the normal save loaded would
  autosave AP progress into the real save. Fixed in `Bridge.ReloadSlot`, where AP stays off until restart.
- Changed after review: the "Support Hammer" became the **Crate Hammer**. The Brachiosaurus secret hammer only
  unlocks crate breaking (`BreakAbility`), and crates also open by prying the lid (`LidPry`). It is a useful
  item, and support straightening is never gated.
- Plugin smoke test in the game copy confirmed that the plugin loads, the bridge connects, and the save is
  redirected to `<bridge>/save`, with no Harmony errors.
  - Windows needs `version.dll` (Doorstop). `winhttp.dll` did not load on this game.
  - `doorstop_config.ini` must keep `dll_search_path_override` empty. The Cozy copy's `BepInEx\corlib_only`
    value crashes the preloader.
  - A process started from Claude's sandbox can't read `~/DinoBonesAP`. Use the `DINOBONES_AP_ROOT` env var
    for test bridges.
- Release files: `release/DinoBonesAP.dll` and `release/dino_bones.apworld`.

## State
- Build the plugin: `cd plugin/DinoBonesAP && dotnet build -c Release`. This copies the DLL to `release/`.
- Run the world tests: copy `world/dino_bones` to `vendor/Archipelago/worlds/`, then from `vendor/Archipelago` run
  `AP_TEST_WORLDS=dino_bones .venv/Scripts/python.exe -m unittest discover -s worlds/dino_bones/test -t .`
- The game copy has BepInEx (version.dll) and the plugin installed.
- **Not verified in gameplay:** bone and permit gating, check sending, item grants, the counts dump
  (gardens, supports), and the goal. None of it has been played.

## Remaining (plan: docs/plans/2026-11-03-dino-bones-ap.md)
1. Play test with a real AP server:
   - Start a new game in the AP save.
   - Confirm that placing a bone on a permitted species sends `{S} Bone 1` and that other species refuse bones.
   - Check the first-scene log dump of counts per rig, garden beds and supports. If needed, fix
     `GARDEN_BEDS` or the support counts in `world/dino_bones/data.py` and the plugin `Spec`.
2. Test the Mac install (run_bepinex.sh, Rosetta).
3. Optional: traps, and prettier achievement location names.
4. Released v0.1.0 to github.com/poststructural/DinoBonesAPWorld (public; commits as Jerrod with the no-reply email, no Claude trailer). Ask Austin before later pushes.

## Decisions and dead ends
- Bone checks count "Nth bone placed on species S", not mesh names. A skeleton completing sweeps any checks
  left on that species.
- No shop or currency exists. The coins are 9 album medals.
- The garden count is fixed at 7, the achievement threshold, until the dump confirms it.

## Continuation prompt
Read `DinoBonesAPWorld/docs/HANDOFF.md`, `docs/SPEC.md` and the plan. Next task: help Austin play-test
Dino Bones with a local AP server. Acceptance check: the first bone placed on the starting-permit species
appears as a sent check in the client, a bone on another species is refused, and receiving a permit lets
that species accept bones.
