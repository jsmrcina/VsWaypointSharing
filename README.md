# To build

1. Source init.ps1
    - In powershell, '`. .\init.ps1`'
2. Build by running '`dotnet build -c Release`'.
    - There is also a `launch.json` and `tasks.json` for running in VSCode, but you will need to update the path references as I couldn't get it to work with environment variables.
3. Output is under `Release\VsWaypointSharing.zip`.

# To use

1. Copy `VsWaypointSharing.zip` into `Mods` folder under `VintageStory`.
    - If you are running a hosted or dedicated server, you will need to add the mod there as well as it has a server-side component.
2. When playing, pull waypoints from other users, run the `.ws share` command from chat (by default, chat opens with `T`).

# Copyright Info

This mod is only possible because of the following projects:

- https://github.com/copygirl/howto-example-mod
    - Published under public domain

- https://github.com/EnigmaticaGH/VintageStoryMods
    - Published under GPL-2.0. This mod uses snippets of code from this repository, and thus, due to GPL-2.0 requirements is also licensed as GPL-2.0. The code is available at the link above.

- https://github.com/p3t3rix-vsmods/VsProspectorInfo
    - Published under MIT.