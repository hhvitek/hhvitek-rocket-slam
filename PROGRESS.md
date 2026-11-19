# Rocket Slam — progress

State of the work. Rules are in [CLAUDE.md](CLAUDE.md), the spec in [PLAN.md](PLAN.md).

## Status
- **Status: ALL MILESTONES DONE (M0–M6), 2026-11-19.** Final work amended into the single commit and force-pushed.
- **Next:** user playtest of Linux + Windows builds (`builds/`), feedback → follow-ups below.
- **Git (user rule 2026-11-19):** history squashed into one commit `f89f9ce` on origin; no more per-milestone commits —
  at the very end amend all work into that commit and force-push once. Local backup tag `backup/pre-squash`.
- **User request 2026-11-19:** when all work ends (M6 done + final amend/force-push, or any limit stop) and no other
  Claude session/subagent is still working → schedule computer shutdown (`shutdown -h +1`).
- **Remote:** `origin` = git@github.com:hhvitek/hhvitek-rocket-slam.git (private), push after each milestone.

## Done
### M0 — setup (2026-11-18)
- Usage guard: checked at session start (5h 2 %, weekly 9 %), watcher cron `7,17,27,37,47,57 * * * *` running.
- Test one-shot resume job fired on time (scheduled 20:11, fired 20:11:03).
- Template cleanup, Test Framework 1.8.0, Input System 1.20.1 (new input only).
- Assets folder skeleton, asmdefs, smoke tests, `BuildScript`, `SceneBuilder`, `THIRD_PARTY.md`.

### M1 — grey prototype (2026-11-18)
- Physics (`Scripts/Rocket/`): `RocketConfig` SO, Rigidbody with custom forces; 22 t dry, 10 t fuel, 850 kN,
  off or 40–100 %, 280 kg/s, gimbal ±5°, drag Cd 0.95 × A 10.75 m² (terminal ≈ 224 m/s), angular damping 1.0/s,
  weathervane torque while falling engine-first. Start 3000 m, −225 m/s, offset (60, −40) m.
- Outcomes (`LandingEvaluator`, pure C#): success / hard impact / tipped over / no legs / off pad / too tilted /
  out of fuel (fails at touchdown, not mid-air). Engine cuts at touchdown, 3 s settle.
- Input System actions (keyboard + gamepad), UI Toolkit HUD (radar altitude, speeds and tilt go green within limits).
- Cameras (C cycles, 0.5 s blend), designed from a short research pass on landing games:
  chase cam above/behind on the pad→rocket line with altitude curve and pad-in-frame clamp (default),
  engine cam looking down, pad-side zoom cam. Gimbal input is relative to the active camera.
- Visual aids: large ground target rings, on-screen pad marker (edge arrow when off-screen),
  predicted ballistic impact reticle, drop line + shadow puck.
- `SceneBuilder` builds `Level01.unity` (only build scene); template SampleScene/Bootstrap removed.
- `ScreenshotTool.CaptureM1` + player `-autocapture <dir> [-autocapture-crash]`.
- Tests: EditMode 44/44, PlayMode 8/8 (autopilot lands at 0.8 m/s, free fall crashes). Linux build OK.
- Review: no serious findings; fixed: physics read interpolated transform, out-of-fuel flow, Esc after result,
  diagonal arrow keys. Camera reworked after user playtest feedback (pad was not visible during descent).

### M1 feedback round 1 — steering (2026-11-18)
- User: "cannot use WASD or arrows to steer". Causes: engine off = no torque (gimbal only), A/D unbound, ~7°/s cap.
- Fix: WASD + arrows steer (camera-relative); throttle Shift/Ctrl, Z full, X min, wheel ±10 %.
  Grid fins (38 N·m/Pa), RCS 150 kN·m, body lift Cl 0.5·sin2α; HUD STEERING panel. Grid fins pulled forward from M5.
- Lift pushes toward the leaning nose (intuitive); `RocketConfig.liftTowardsNose` = false gives physical behaviour.
- Tests: EditMode 47/47, PlayMode 9/9 (incl. steering with engine off). Linux build OK.

### M1 feedback round 2 — Easy mode (2026-11-18)
- User: "garbage, I fly UP instead of down; make it simple, explain controls". Cause: 40 % min throttle > weight, too many keys, no cue.
- New default **Easy** mode: hold SPACE = engine, release = off; thrust capped so the rocket never climbs;
  auto steering to pad + auto legs < 300 m; burn cue FIRE IN → FIRE NOW → TOUCHDOWN speed readout;
  start screen with controls; simplified HUD. **Manual** mode (M) keeps the realistic controls.
- Tests: EditMode 53/53, PlayMode 14/14 (naive-human Space-only lands with 0.5 s and 1.5 s reaction; Space from start never climbs).
- Notes: cue box overlaps rocket in final approach; 20 m/s floor 400→100 m may feel slow (`easyMaxFloorSpeed`, `easyFloorPerMetre`);
  RocketConfig.asset not re-saved (Easy fields use code defaults).

### M2 — Blender models (2026-11-18)
- Generators `blender/scripts/{common,palette,booster,landing_pad,droneship}.py` → `Booster.fbx` (+ `Booster_rig.json`),
  `LandingPad.fbx`, `Droneship.fbx`, `Textures/palette.png` (8×8 swatches). Own livery "SLAM-1", no logos.
- Unity: `ModelImportSettings` postprocessor + `Palette.mat`; booster with leg (111.94°) and grid-fin deploy animation,
  feet re-parented under legs; pad model under collider; `Prefabs/Droneship.prefab` (not placed yet). Axis mapping verified.
- CC0 assets fetched for M3/M4 (`tools/fetch_assets.py`, THIRD_PARTY.md): Kenney sci-fi/impact/UI audio + particles,
  Poly Haven day/night HDRI 2k, ambientCG Concrete034 + MetalPlates006. No wind loop in Kenney (synthesise or find one).
- Tests: EditMode 53/53, PlayMode 17/17 (incl. BoosterModelTests). Linux build OK, Easy autocapture lands.
- Review: no serious findings. Preview renders shrunk before commit.

### M3 — effects and sound (2026-11-18)
- Particles by script (`Editor/EffectsBuilder.cs`, `Scripts/Effects/`): engine flame (core, plume, glow, light) by thrust, ground dust < 60 m,
  explosion (flash, fireball, smoke, sparks, debris); `CameraShake` (explosion kick + rumble < 50 m only).
- Audio (`Scripts/Sound/`): crossfaded engine loops by thrust, synthesised wind, legs, touchdown, explosion, UI; `Audio/Main.mixer` (MasterVolume exposed).
- Bloom via `Settings/GameVolumeProfile.asset`. Tests: EditMode 63/63, PlayMode 19/19. Linux build OK; easy + crash autocapture clean.
- M4 prep assets committed with M3: `blender/scripts/scenery.py` → 20 `Scenery_*.fbx` + `Scenery_catalog.json` (palette swatches 32–43 added),
  ambientCG Grass004 / Gravel023 / Asphalt012.
- Review: no serious findings.

### M4a — environment, WIP (2026-11-18)
- Done, uncommitted until the WIP commit: HDRI sky (rotated 75°, sun from brightest pixel), skybox ambient + reflection probe,
  exp2 fog, ACES tonemapping/colour/vignette; runtime-generated terrain (`Scripts/Environment/GroundTerrain.cs`, `TerrainShape.cs`,
  5×5 tiles × 4 km, flat within 1.7 km); `Editor/EnvironmentBuilder.cs` places 19 buildings (colliders) + ~36k instanced props
  (`InstancedScenery.cs`); `AdaptiveShadows.cs`; far clip 20 km; M3 fixes (AudioListener.pause, restart stops SFX, flame size range
  + ground clamp, result panel delayed 1.5 s after explosion). New `-autocapture-m4a` and `-benchmark [file]` player modes.
- Verified: compiles; Linux build OK; m4a capture lands (1.4 m/s). NOT run: benchmark, test suites (last green M3), easy/crash captures.
- Known bad: grass too lime, slopes paint near-white, fields look pasted, forests dots from 3 km, fences solid lines;
  terrain instancing off (rendered black); terrain generation ~1.9 s at load.

### M4a — Level01 environment, finished (2026-11-19)
- Retinted terrain layers + forest floor/soil layers, varied field strips with hedgerows, forest blobs (dark far-tree material),
  scrub/earth patches around the site, fences only within 250 m, threaded terrain generation (1926 → 820 ms),
  `BuildScript.BuildScenesAndLinux`, benchmark waits 8 s and lists slow frames.
- FPS 1920×1080 on this laptop: avg 98, 1 % low 47 (above 1000 m 93/39, below 100 m 84/38); dip ~35 FPS at touchdown (dust).
- Tests: EditMode 67/67, PlayMode 20/20. Linux build OK; m4a/easy/crash captures clean.
- Open: Level01.unity 6.6 MB (52k prop records) → move to runtime; terrain instancing off (black); bushes/rocks dark specks close up;
  hazy horizon vs darker sky band in high wide shot; first 1–2 s after load slow (start screen covers it).

### M4b — levels 1–5, WIP (2026-11-19)
- `LevelConfig` (site, fuel, offset, wind, sea, day/night, fog, sky rotation) → `Level01..05.asset` written by SceneBuilder;
  `Level01..05.unity` in build settings. Props + ground paint generated at load (`LaunchSiteLayout.cs`, `LaunchSite.cs`) →
  scenes 0.6–0.93 MB (was 6.6 MB).
- Sea: `WaveField` (sum of sines) drives `OceanSurface` (700 m Burst-updated patch + flat far ring to 30 km) and `ShipMotion`
  (kinematic droneship, plane fit through 15 wave samples). Droneship colliders for deck/parapets/walls/containers; deck only = pad.
- `LandingSite` + `LevelRuntime`: deck-relative altitude/velocity; touchdown speed and tilt judged relative to the deck.
- Wind: steady + gusts, side force; Easy assist and autopilot compensate; HUD wind panel. Night L5: night HDRI, moonlight, fog,
  deck floodlights. Level select: keys 1–5, N next, `-level N`. Engine cam no longer blocked; ground dust reduced.
- Tests: EditMode 78/78; PlayMode last full run 33/33 before final tuning; stopped run showed L1–L2 naive-player touchdown 2.5/2.7 m/s.
- Helper scripts `tools/unity/{full,bench,cap}.sh`.

### M4 — finished (2026-11-19)
- M4b finish: Easy touchdown guard (`EasyFlight.GuardThrottle`, below 15 m after a real burn → 1.5 m/s relative to the deck);
  cause of L1–L2 hard landings was the naive test bot pulsing Space with the 0.3 s ramp lag. Night fog/ocean colours fixed.
- M4 review: serious = committed 10 MB mono crash dump (removed, `mono_crash*` ignored, history squashed); other two "serious"
  were based on stale PROGRESS (guard already tested 33/33; cause found). No second review round (no code-level serious findings).
- Review fixes: `LevelClock` shared by ocean/ship/wind, reset on Restart (`ShipMotion.Snap`); Burst ocean job uses `WaveField`;
  wind counted once (`AxialDrag` + `SideWindForce`); altitude = min clearance of feet/engine to deck plane; bloom threshold 2.5;
  sky panorama mipmaps off (dashed seam line gone); tools/unity scripts portable + `set -euo pipefail`.
- Tests: EditMode 82/82, PlayMode 38/38 (incl. NoInputCrashesOnEveryLevel ×5, L4 deck slide 0.26 m / limit 0.3).
  FPS 1080p: L1 84 avg / 42 1 %-low, L5 113 / 75. All 5 levels land at 1.5 m/s in Easy captures.
- Open (minor): L4 settle slide close to limit; white palette swatch (0.89 linear) still near-clipping in full sun → modeler tweak
  in M6; L1 1 %-low frames in last 15 m (dust) → M6 perf; AdaptiveShadows edits the shared URP asset at runtime; terrain splat
  copies ~48 MB transient at load; landing area DeckHalf includes stern obstacles (HUD/IsOver only); Easy guard makes the last
  15 m automatic (by design, Manual keeps the skill).

### M5 — game loop (2026-11-19)
- MainMenu scene (orbiting backdrop), level select with unlocks + stars, results screen (breakdown, stars, NEW BEST,
  Painter2D telemetry graph), pause menu, settings (volume → mixer, quality Low/Medium/High via `QualityBuilder`, Easy assist,
  default camera). One menu navigator for keyboard/gamepad/mouse (`Menus/MenuList.cs`, `MenuInput.cs`).
- Scoring 0–1000 (`Gameplay/Scoring.cs`): fuel 400 (full at 65 % tank), precision 300 (0.5 m → 12 m), softness 300;
  stars 2/3 thresholds L1 650/795 … L5 560/700 (calibrated by bots: first-time Easy = 2★, skilled Easy = 3★, autopilot = 3★).
- Save `rocketslam_save.json` (editor: `rocketslam_save_editor.json`) in persistentDataPath, atomic replace, corrupt → defaults
  (+ `.corrupt` copy), `-save <path>`, autocapture/benchmark keep save in memory.
- Tests: EditMode 105/105, PlayMode 48/48. Benchmark L1 83 avg / 40.5 1 %-low (no regression).
- Palette white swatch lowered to #E2E4E6; "SLAM-1" digit built from geometry (rig JSON byte-identical).
- Review: no serious findings. Open minor (→ M6): hidden Next still selectable on L5; keys dead for 1.5 s after explosion;
  older PlayMode fixtures don't use `SaveGame.UseMemory` (read/write the editor save); settings→file not PlayMode-tested;
  future save version overwritten without backup; statics not reset with domain reload off; crash graph fall-speed drops to 0
  at end; gamepad Start = Submit in pause menu; double sound on Resume; NaN/Infinity guards in Scoring; per-frame string in results.
- Menu backdrop: booster looks blue because the orbit starts on the shadow side (sky ambient) and lettering faces away → M6.

### M6 — polish and release (2026-11-19)
- Gamepad everywhere (Start resumes, B back, Select restart, RB/LB full/min throttle), key/gamepad hints (`InputHints.cs`),
  `GamepadTests` (gamepad-only full loop). M5 review fixes all done (hidden Next, keys after explosion, UseMemory in all
  fixtures, settings→file test, newer-version save backup, statics reset, crash graph, single Resume sound, NaN guards).
- Visuals: menu orbit on sun side + lettering to camera; day ambient fixed neutral gradient (no blue shadows); L4 settle slide
  0.12 m (feet along deck) + non-skid deck + foot grip (engine off, ≥3 feet on pad, ≤1.5 m/s side speed; doesn't rescue bad landings).
- Perf 1080p Medium: L1 93.6 / 54.6 (avg / 1 %-low), L4 124 / 89, L5 113.5 / 75 (trees were the cost: far density/culling,
  prop shadows ≤ 1 km, cheaper Medium soft shadows).
- Balance: Manual autopilot 3★ on all levels, fuel left 62/60/57/55/51 %; first-time Easy 2★, skilled Easy 3★.
- Builds: Linux `builds/Linux/RocketSlam.x86_64` (116 MB), Windows `builds/Windows/RocketSlam.exe` (Mono, 112 MB, not run here).
- README rewritten + `docs/screenshots/01–06*.jpg`. Tests: EditMode 110/110, PlayMode 56/56.
- Review: no serious findings.

## Follow-ups (minor, open)
- `Settings/DeckGrip.physicsMaterial` on disk has default friction (SceneBuilder sets 1.1/1.0 Maximum in memory) → rerun builder, check SaveAssets.
- L1 ~30 ms hitches at 13 m and 8 m, ~75 ms frame at touchdown on every level; HUD allocates strings per frame.
- Night HDRI shows tree silhouettes on the sea horizon (L5); red hull bottom visible below waterline in swell (L4).
- InputHints don't switch back to keyboard on mouse use; "Esc back" hint on the root menu; bench.sh writes to screenshots/m4b;
  QualityBuilder Low/High copies don't follow PC_RPAsset changes; benchmark-only `-bench-*` flags left in AutoCapture (harmless).
- Exclude `builds/Windows/*_BackUpThisFolder_ButDontShipItWithYourGame` when zipping a release.

### M4 scope additions (user request 2026-11-18)
- Scenery around the pad: buildings, roads, trees, grass (sense of scale and speed), plus post-processing (tonemapping, bloom).

## Open review findings (minor, not fixed)
- `LandingEvaluator` latches Success after 3 s; a later topple still shows LANDED.
- HUD allocates ~8 strings per frame (cache / update on change) — before M6 perf pass.
- Horizontal speed 1.6 m/s at touchdown counts as HardImpact explosion; maybe a separate non-explosive "sideways" failure.
- Test gaps: restart test doesn't check angular velocity / x-z position; gamepad bindings untested.
- One extra FixedUpdate of thrust at touchdown (20 ms, negligible).
- `RocketSlam.Editor` rootNamespace shadows `UnityEditor.Editor`; template `InputSystem_Actions` still project-wide (unused).
- No tonemapping/bloom since sample volume profile removed → M3/M4.
- From 3 km the chase cam is well above the rocket (foreshortened); pad label overlaps impact reticle.
- Check Windows build module before M6.
- M2: droneship only has a deck collider (parapets/blast walls/containers pass-through) → fix in M4.
- M2: `fetch_assets.py` uses undocumented Kenney download endpoint + spoofed UA, no checksums for Kenney/ambientCG zips.
- M2: BoosterModelTests only check the deployed pose (not mid-swing clearance / stowed pose).
- M2: RocketConfig leg geometry fields unused (only `legDeployAngleDeg` matters); `ModelImportSettings` lacks `GetVersion()`.
- M2: "SLAM-1" reads as "SLAM-I" in Blender's default font; FBX/.blend embed timestamps (binary diffs on rerun).
- M2: HDRIs 11.7 MB total (2k); consider 1k if repo size matters.
- M3 (→ M4 fix pass): pause doesn't pause SFX one-shots (AudioListener.pause unused); restart doesn't stop SFX;
  EngineFlame startSizeMultiplier scales only max of range; flame not clamped by ground height (dark blob on pad);
  smoke/dust overdraw risk on iGPU — measure FPS; SceneBuilder material re-save churn; no pause/restart audio tests.
- M3 nits: runtime AudioClips not destroyed on scene reload; LateUpdate order RocketEffects vs CameraDirector;
  mixer created via internal API (only when missing); all audio 2D; CRASHED panel covers explosion; flame hidden at low thrust in steep chase view.

## Notes
- `-testResults` path is relative to `unity/`; test XML lands in `unity/Logs/`.
- Player `-logFile` resolves relative to the build folder.
