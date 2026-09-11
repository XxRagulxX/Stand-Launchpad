# Stand Launchpad

A DLL injector and game launcher for **GTA V Enhanced**. Pick your DLL(s),
launch the game through Steam / Epic Games / Rockstar Games, and inject
automatically when the process starts — or manually with one click.

Works natively on **Windows** and on **Linux via Steam Play (Proton)** —
Avalonia's self-contained Skia renderer means no extra runtime is needed in
either environment.

## Features

- **Auto-inject** — watches for `GTA5_Enhanced.exe` and injects the moment it
  appears, with an optional configurable delay.
- **Multi-DLL list** — add any number of DLLs, toggle each on/off with a
  checkbox, reorder with Move Up / Move Down.
- **Launcher support** — starts GTA V through Steam (`steam://`), Epic Games,
  or Rockstar Games from the same window.
- **Open Stand Folder** — quick shortcut to `%AppData%\StandEnhanced\`.
- **Settings persistence** — all choices saved automatically to
  `%AppData%\Launchpad\settings.json`.

## Download

Grab the latest `Launchpad.exe` from the
[Releases](../../releases) page. No installer, no runtime to install — just
run it.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/).

```sh
dotnet publish Launchpad/Launchpad.csproj -c Release -r win-x64 -o publish
```

Output is a single `publish/Launchpad.exe` (~19 MB, fully self-contained).

## Project layout

```
Launchpad/
  Launchpad.csproj       .NET 10 / Avalonia, win-x64 self-contained single-file
  MainWindow.axaml        UI layout (two-column: controls | Advanced panel)
  MainWindow.axaml.cs     Event handlers, game-watch loop, auto-inject logic
  DllInjector.cs          VirtualAllocEx / WriteProcessMemory / CreateRemoteThread
  GameProcess.cs          Finds GTA5_Enhanced.exe by process name
  InjectorRunner.cs       Bridges UI ↔ injector; background watch task
  StorefrontLauncher.cs   Steam URL / Epic URL / Rockstar registry launch
  Settings.cs             JSON settings (source-generated, trim-safe)
  GameLauncher.cs         Launcher option list
  NativeMethods.cs        kernel32 P/Invoke declarations
.github/workflows/
  release.yml             Manual-trigger workflow → single-file exe → GitHub Release
```

## How injection works

Injection runs in-process via the classic `LoadLibraryW` remote-thread
technique:

1. `OpenProcess` with VM + thread rights on the GTA V pid.
2. `VirtualAllocEx` — allocate a page in the target process.
3. `WriteProcessMemory` — write the DLL path (wide string) into that page.
4. `CreateRemoteThread(LoadLibraryW, &path)` — game loads the DLL.

Each DLL is staged to a temp copy first to avoid AV file-lock conflicts on
the source path.

## CI / Releases

The [release workflow](.github/workflows/release.yml) is triggered manually
from the Actions tab. Provide a version tag (e.g. `v1.2.3`) and it builds,
packages, and publishes a GitHub Release with the single-file exe attached.
