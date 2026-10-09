# To build

Requires the .NET 10 SDK and Vintage Story 1.22.x installed locally (the build references the game's DLLs).

1. Set `VINTAGE_STORY` to the game directory (the one containing `VintagestoryAPI.dll`).
    - In PowerShell, source `init.ps1`: '`. ./init.ps1`' (Windows: `%APPDATA%\vintagestory`, Linux: `/opt/vintagestory`).
2. Build by running '`dotnet build -c Release`'.
    - `.vscode/launch.json` launches the game with the Debug build loaded (via `${env:VINTAGE_STORY}`), so start VS Code
      from a shell where `VINTAGE_STORY` is set.
3. Output is under `bin/Release/VsWaypointSharing.zip`.

# To test

- Unit tests: '`dotnet test tests/VsWaypointSharing.Tests`'. These run the sync logic, the server message handlers and a
  real `WaypointMapLayerExtension` against mocked game APIs, plus canary checks for game updates (the private
  `WaypointMapLayer` methods used via reflection, the `waypoints` layer registration, serialization, and that
  `modinfo.json` targets the game version being built against).
    - The test assembly is named `VSTests` on purpose: as of 1.22 `IPlayer` has an `internal` member, so only assemblies the
      game grants `InternalsVisibleTo` (it grants `VSTests`) can implement `IServerPlayer` — see `FakeServerPlayer.cs`.
- Smoke test: '`bash tests/smoke/server-smoke.sh`' boots a throwaway dedicated server with the Release build and checks
  that the mod loads, hooks the waypoint layer, and that nothing logs an error (~20 s, needs no account).
- Both are VS Code tasks (`Test`, `Smoke test (dedicated server)`).

# To use

1. Copy `VsWaypointSharing.zip` into `Mods` folder under `VintageStory`.
    - If you are running a hosted or dedicated server, you will need to add the mod there as well as it has a server-side component.
2. When playing, run these from chat (by default, chat opens with `T`):
    - `.ws sync` copies other players' waypoints onto your map (titled `<sync from: Name>...`, unpinned).
    - `.ws revert` removes the copies, leaving only your own waypoints.
    - `.ws autosync` toggles re-syncing every 15 seconds (resets when you leave the server).
    - `.ws status` tells you whether auto-sync is on.
    - Copies are updated in place, so pinning a copy sticks, and a copied death waypoint reads `<sync from: Bob>Bob died here`.

# Copyright Info

This mod is only possible because of the following projects:

- https://github.com/copygirl/howto-example-mod
    - Published under public domain

- https://github.com/EnigmaticaGH/VintageStoryMods
    - Published under GPL-2.0. This mod uses snippets of code from this repository, and thus, due to GPL-2.0 requirements is also licensed as GPL-2.0. The code is available at the link above.

- https://github.com/p3t3rix-vsmods/VsProspectorInfo
    - Published under MIT.