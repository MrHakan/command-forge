# Third-party notices

Command Forge is licensed under the GNU General Public License v3.0 (see `LICENSE`).

## Bundled in the release executable

- **.NET runtime and Windows Forms** (self-contained `CommandForge.exe`) — © .NET Foundation and contributors, MIT License. https://github.com/dotnet/runtime, https://github.com/dotnet/winforms

## Used at runtime, not bundled

- **Minecraft: Java Edition** client files are read from the user's own Prism Launcher installation for artwork and compilation of the bridge. No game classes, textures or other Mojang assets are included in this repository or in releases.
- **Fabric Loader 0.19.5** and **Mixin** are downloaded by Prism Launcher when the editor-managed 26.2 instance starts. Apache License 2.0 / MIT. https://github.com/FabricMC/fabric-loader
- **Prism Launcher** handles downloads, Java and accounts. GPL-3.0. https://github.com/PrismLauncher/PrismLauncher
- **Discord** desktop app, through its local Rich Presence IPC (optional).

## Data in `docs/research`

- `mojang-version-manifest.json` and `release-metadata/` are copies of Mojang's public version metadata (https://piston-meta.mojang.com). Mojang and Minecraft are trademarks of Mojang Synergies AB / Microsoft.
- Protocol and data-path indices were derived from PrismarineJS **minecraft-data** at commit `dd01127457231e7da629d645b3c6738068018eb0` (MIT; see that project's README for per-source notes). The raw `minecraft-data/` snapshot is not part of this repository; `docs/research/build-coverage.ps1` documents how it was used.

Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.
