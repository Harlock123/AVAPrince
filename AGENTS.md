# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A palace escape in the style of Jordan Mechner's 1989 game Prince of Persia, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. Eight generated levels against a one-hour clock. One self-contained executable per platform
(Windows, macOS and Linux, x64 and ARM). Everything in it is original: the graphics are drawn in code, and the
sounds, music and cries are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAPrince.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPrince.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAPrince          # play it; --no-audio for silence
PRINCE_SHOTS=$PWD/Images dotnet test tests/AVAPrince.Tests --filter ScreenshotTests   # refresh the README's pictures
PRINCE_PROBE=1 dotnet test tests/AVAPrince.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                # rebuild src/AVAPrince/Sounds (uses tools/speech.py)
```

`PRINCE_PROBE=lazy` runs the probe with a prince that just runs right and jumps now and then.
`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPrince/Engine/`: the game, with no Avalonia drawing in it.
  - `Palace.cs` has the `Field` (measures and the 1/60 s `Step`), the cell kinds, `LevelDef` and `PalaceBook`,
    which generates the levels.
  - `World.cs` has the `Dice` random source, `Controls`, the acts, and the `World`: the prince, the guards and
    the clock, kept together so it can be copied whole (`World.Copy()`).
  - `Sim.cs` is the rules, one 60th of a second at a time: the prince and guards move an act at a time.
  - `Graph.cs` finds every square the prince can stand in and the moves between them, by trying moves in the
    game's own rules.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `Pilot.cs` is the computer prince, used by the attract mode and the tests. It plans its way by the graph.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPrince/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs the
  60 Hz timer; `GameCanvas.Draw.cs` holds the drawing.
- `src/AVAPrince/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAPrince/Sounds/`: the generated WAV files.
- `tests/AVAPrince.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and the opt-in `PilotProbe`.
- `tools/`: `make_sounds.py` and `speech.py`, a small formant speech synthesiser for the cries.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the simulation in fixed
  `Field.Step` (1/60 s) steps, so tests can play levels deterministically. Draw random numbers from the world's
  `Dice` (`w.Dice`, reseeded per level), never from `Random.Shared`.
- **Every level can be finished.** `PalaceBook` builds each level from a seed and keeps it only if `Graph` shows
  the door can be reached from the start. `GameplayTests` checks this for every level and that the computer
  prince gets through them all. Change the moves in `Sim` and `Graph` together.
- **Copyable world.** Everything that changes during play lives in `World`, and `World.Copy()` must copy all of
  it: the graph and pilot try moves out on copies. A new field needs adding to the copy.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** There is no `dy` here: `Controls` has `Dx` and the `Up`, `Down` and
  `Shift` buttons. `Up` comes from the up keys, D-pad up, the stick pushed up (negative `LeftStickY`) or pad A.
  Test new controls through `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. Shift matters here (careful steps, striking), and `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAPrince`); tests load
  the table from a temporary folder and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
