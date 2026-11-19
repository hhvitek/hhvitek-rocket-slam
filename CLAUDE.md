# Rocket Slam — rules for Claude

3D rocket-booster landing game (Unity 6 + URP), built almost entirely by Claude as a showcase.
**[PLAN.md](PLAN.md) is the spec** (game design, tech choices, milestones M0–M6). This file holds
the working rules. **Current state lives in `PROGRESS.md`**, never here.

The user (Vítek) writes Czech. Talk to him in Czech; code, comments, commit messages and in-game
text are English.

## Machine
- Linux Mint 22.3 (Ubuntu 24.04 base), Cinnamon on X11, Ryzen 5 5500U, integrated Radeon, 14 GiB RAM.
- **No sudo, no system installs, no new accounts.** If something is missing, tell the user what to do.
- Performance target: 60 FPS at 1080p, URP medium quality, on this integrated GPU.

## Repository layout (monorepo)
| Path | What | In git |
|---|---|---|
| `unity/` | Unity project, editor `6000.6.9f1`, URP (Universal 3D template) | yes, except `Library/ Temp/ Logs/ UserSettings/ *.csproj *.slnx` |
| `blender/scripts/` | Blender Python generators | yes |
| `blender/blend/`, `blender/renders/` | `.blend` saved by generators, preview PNGs | yes |
| `tools/` | helper scripts (asset download, build wrappers) | yes |
| `apps/<name>/` | anything outside the game, one folder per app | yes |
| `docs/` | screenshots and notes for README | yes |
| `builds/` | `Linux/RocketSlam.x86_64`, `Windows/` | no |
| `screenshots/` | working screenshots from automated checks | no |
| `THIRD_PARTY.md` | register of every third-party asset (source, URL, licence) | yes |

## Tools and commands
```bash
UNITY=~/Unity/Hub/Editor/6000.6.9f1/Editor/Unity

# import + compile (fails on compile errors; read the log)
$UNITY -batchmode -nographics -projectPath unity -quit -logFile unity/Logs/batch.log

# tests (no -quit with -runTests); PlayMode and screenshots need graphics: drop -nographics
$UNITY -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile unity/Logs/tests.log
$UNITY -batchmode -projectPath unity -runTests -testPlatform PlayMode -testResults Logs/playmode.xml -logFile unity/Logs/tests.log

# editor scripts (scene building, build, screenshots)
$UNITY -batchmode -projectPath unity -executeMethod BuildScript.BuildLinux -quit -logFile unity/Logs/build.log

# Blender generator, headless
blender -b --factory-startup --python blender/scripts/booster.py
```
- Blender 5.3.0 (`blender` on PATH). Python 3.12. No `gh` CLI, no Git LFS.
- `-testResults` (and the scene/build paths inside editor scripts) resolve relative to `unity/`, not the repo root.
- Harmless Unity log noise: `Curl error 7` to `*.cloud.unity3d.com` (analytics), `Access token is unavailable`.
- **Only one Unity process per project at a time.** If Unity reports the project is open elsewhere,
  the user has the editor open: ask him to close it and wait.

## How to work: orchestrator + subagents (PLAN.md §4A)
The main session **only orchestrates**: plans the milestone, delegates tasks to subagents (`Agent`
tool, general-purpose, role given in the prompt), integrates results, talks to the user, commits.
It edits only `CLAUDE.md` and `PROGRESS.md` itself and does not read big files or logs (subagents
return summaries).

| Role | Does | Writes to | Model |
|---|---|---|---|
| implementer | C#, UI Toolkit (UXML/USS), editor scripts, scenes, tests; compiles, tests, screenshots and builds in Unity | `unity/` (except `Assets/Models`, `Assets/Textures`, `Assets/ThirdParty`), `screenshots/`, `builds/` | strong |
| modeler | Blender Python generators, FBX export, preview renders, palette atlas | `blender/`, `unity/Assets/Models/`, `unity/Assets/Textures/` | strong |
| asset-scout | finds and downloads CC0 assets, records licences | `unity/Assets/ThirdParty/`, `THIRD_PARTY.md`, `tools/` | Sonnet |
| reviewer | independent review, read-only | nothing | strong |

- At most **3 subagents** at once, each only in its own folders. Only **one implementer** at a time (Unity lock).
- No git worktrees (each would re-import the whole Unity Library).
- **Only the orchestrator commits.** **User rule 2026-11-19: no per-milestone commits.** Keep work uncommitted
  until ALL milestones are done, then amend everything into the single existing commit on `master`
  (history is one squashed commit) and force-push once (`--force-with-lease`). Graceful stops don't commit either.
- **Review:** once at the end of every milestone, a *fresh* reviewer gets only the milestone diff,
  the done-criteria from PLAN.md §5 and screenshots/renders — not the implementer's reasoning.
  It returns findings with severity and fixes nothing. If serious findings were fixed, a new
  reviewer checks again (max 2 rounds). Leftover minor findings go to `PROGRESS.md`.

## Usage guard (PLAN.md §4B) — mandatory
Data: `mcp__ccd_session_mgmt__get_usage` → `plan.windows` (5-hour window + every weekly window).
Check: at session start before anything else, before launching each subagent, whenever a subagent
reports, and by a watcher `CronCreate` job `7,17,27,37,47,57 * * * *`.

**Current thresholds (set by the user 2026-11-19; only the user changes them):**
| Window | Graceful stop | Hard stop (kill) | After the stop |
|---|---|---|---|
| 5-hour | **none** (no stop on this window) | none | — |
| any weekly | **≥ 50 %** | ≥ 53 % (3 % reserve, as before) | **no auto-resume — stop and wait for the user's instructions** |

- **Graceful:** start nothing new → `SendMessage` running subagents to finish the current step so the
  project compiles → write `PROGRESS.md` (no commit, user rule 2026-11-19) (done, remaining, exact
  next step, which limit stopped work, reset time) → cancel the watcher → end the turn with the numbers.
- **Hard:** `TaskStop` all subagents and background tasks, cancel the watcher, short note in
  `PROGRESS.md` (what may be half-done per `git status`), no commit.
- `unavailable` → start nothing new, retry at the next check. `not_applicable` → no guard.

## Rules
1. **Milestones in order** (PLAN.md §5). End of each: tests green, Linux build, review,
   `PROGRESS.md` updated (no commit/push until the end — see Only the orchestrator commits), short note to the user on what to try.
   **After M1 stop and wait for the user's feedback.**
2. **Verify, don't guess:** compile and test from the command line; render screenshots to
   `screenshots/` and look at them; Blender renders a preview of each model to `blender/renders/`.
3. **Models only from scripts.** Never hand-edit `.fbx`/`.blend`; change the generator and rerun.
   1 unit = 1 m; FBX axes set for Unity; transforms applied.
4. **Scenes from editor scripts** (`unity/Assets/Editor/SceneBuilder.cs`), reproducible.
5. **Third-party assets: CC0 only, from official sites only** (kenney.nl, polyhaven.com,
   ambientcg.com), stored in `unity/Assets/ThirdParty/<source>/`, each recorded in `THIRD_PARTY.md`.
6. **Independence:** do not read or reuse anything from the user's web game "Přistání" in
   `~/Documents/GIT/hhvitek/hhvitek-games` (code, design, tuning). Inspiration from the internet is
   welcome; never copy a specific existing game (name, art, levels) or company logos/names.
7. No secrets in the repo. Keep files that git tracks small (no Git LFS; flag anything > 10 MB).
