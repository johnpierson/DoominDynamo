# Vendored: ManagedDoom (engine subset)

Source: https://github.com/sinshu/managed-doom
Commit: `9365696eb44326a3aab72c4bab217f7db8a87c96` (2025-11-24)
License: GNU GPL v2 (see `LICENSE_ManagedDoom.txt` in this folder) — original Doom source
copyright (C) 1993-1996 Id Software, Inc.; ManagedDoom port copyright (C) 2019-2020 Nobuaki Tanaka.

## What was kept

Only the platform-independent simulation/rendering engine, under `src/`:

- `Doom/**` — game state machine, map/world simulation, WAD loading, menu logic
- `Video/**` — software rasterizer (`Renderer`, `DrawScreen`, `ThreeDRenderer`, ...)
- `UserInput/**` — `IUserInput`, `DoomKey`, `KeyBinding` (interfaces + enums only)
- `Audio/**` — `ISound`/`IMusic` interfaces and their `Null*` no-op implementations
- `ApplicationInfo.cs`, `CommandLineArgs.cs`, `Config.cs`, `ConfigUtilities.cs`
- `Silk/SilkSound.cs`, `Silk/SilkMusic.cs` — despite the folder name, these two
  don't touch Silk.NET.Windowing/Input/OpenGL/TrippyGL at all, only `DrippyAL`
  (OpenAL wrapper) and `MeltySynth` (software MIDI synth) — see "What was
  dropped" below for the rest of that folder.

## What was dropped

The rest of `src/Silk/**` — `SilkVideo`/`SilkUserInput`/`SilkDoom`/`SilkProgram`/
`SilkConfigUtilities`, which bind the engine to a GLFW window via
Silk.NET.Windowing/Input/OpenGL and TrippyGL. Nothing else vendored here
references them (verified by grepping for `Silk\.NET|OpenGL|GLFW|TrippyGL`
across the kept subset before vendoring — `SilkSound.cs`/`SilkMusic.cs` only
hit on their own `ManagedDoom.Silk` namespace/class names, not those libraries).

This repo supplies its own video/input backends instead, in
`src/DoomInDynamo/Engine/`: `WpfVideo` (implements `IVideo`, drives the same
`Renderer` class used by SilkVideo, but writes into a WPF `WriteableBitmap`
instead of an OpenGL texture) and `WpfUserInput` (implements `IUserInput`,
ports `SilkUserInput.BuildTicCmd`'s logic to read from WPF keyboard events
instead of Silk.NET's `IKeyboard`). `SilkSound`/`SilkMusic` themselves are used
as-is (unmodified) — see the README's "Sound" section for how `DoomSession`
constructs them.

## Third-party NuGet dependencies pulled in for audio

`SilkSound.cs`/`SilkMusic.cs` need `DrippyAL` (MIT, Copyright (C) 2022 Nobuaki
Tanaka) and `MeltySynth` (MIT, Copyright (C) 2014 Alex Veltsistas / 2021
Nobuaki Tanaka) - both referenced as ordinary `PackageReference`s in
`vendor/ManagedDoom.Engine/ManagedDoom.Engine.csproj`, not vendored as source,
so their own license terms travel with the NuGet packages as usual.

## Modifications (GPLv2 §2a: files changed by this project)

- `src/Doom/Opening/OpeningSequence.cs` — `StartDemo()` now catches the exception
  `Demo.cs` throws when a WAD's attract-mode demo lump (`DEMO1`-`DEMO4`) was
  recorded with a different engine version than this port targets (v1.9), and
  falls back to the title screen instead of crashing the whole session over a
  demo nobody asked to see. The four `demo.ReadCmd(cmds)` call sites in
  `Update()` were updated to null-check `demo` for the same reason. See the
  `[DoomInDynamo]`-tagged comments in that file for exactly what changed.

## License consequence

Because this package links directly against ManagedDoom's GPLv2 code (not merely
shells out to a separate process), **the combined work — this whole repository,
including the Dynamo node code — is GPLv2 as a whole** if you distribute it (e.g.
publish the package). Keep this notice and `LICENSE_ManagedDoom.txt` intact, and
if you distribute binaries, make the corresponding source available. Purely running
it yourself, unmodified or modified, is unrestricted.
