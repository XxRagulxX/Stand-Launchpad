# Launchpad

A cross-platform DLL injector/launcher for GTA V. Pick your own DLL(s) from
disk, inject them into a running game process, and optionally launch the
game through Steam / Epic Games / Rockstar Games.

Runs natively on Windows, and natively on Linux against a Steam Play
(Proton) install — no Wine needed for the UI itself.

## Project layout

```
CMakeLists.txt      orchestrates `dotnet build/publish`, replaces the old .sln
Injector/           Windows-only console app; the sole P/Invoke surface
                     (VirtualAllocEx/WriteProcessMemory/CreateRemoteThread).
                     On Linux it runs via `wine` inside the game's Proton
                     prefix, since injection has to happen inside the same
                     Windows/Wine process & memory space as the target.
Launchpad/           Avalonia UI, builds and runs natively on both Windows
                     and Linux. Never P/Invokes directly - it drives
                     Injector as a child process instead.
```

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/) and CMake 3.20+.
On Linux, injection additionally requires `wine` to be installed (the UI
itself needs no Wine dependency).

```sh
cmake -B build
cmake --build build
```

Output lands in `build/publish/` — `Launchpad`/`Launchpad.exe` alongside an
`Injector/` subfolder containing the injector.

## How injection works

- **Windows**: the UI runs `Injector/Launchpad.Injector.exe` directly.
- **Linux**: the UI locates the Proton prefix Steam created for GTA V
  (`steamapps/compatdata/271590/pfx`, auto-detected from common Steam
  install locations, or overridden via settings) and runs the injector
  under `wine` with `WINEPREFIX` set to that prefix, so it sees the game's
  actual Windows process table.

## Launching through Epic Games / Rockstar Games on Linux

Steam's `steam://` protocol works natively on Linux without Wine. Epic
Games and Rockstar Games don't have native Linux clients, so - same idea as
injection - the injector runs their registry lookups/protocol calls *inside*
the GTA V Proton prefix instead. This only works if you've installed the
Epic Games Launcher or Rockstar Games Launcher into that same prefix (the
standard setup for running the Epic/Rockstar editions of GTA V on Linux,
since GTA5.exe needs its parent launcher present there to run at all). If
neither is installed in the prefix, the launch button reports why it
failed rather than doing nothing silently.

There's no bundled DLL and no version/update checking against any server —
add whichever DLL(s) you want injected via the "Add..." button, tick the
ones you want active, and hit Inject once the game is running.
