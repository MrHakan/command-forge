# Sources and provenance

Consulted on 12 October 2026. Prefer the saved metadata and pinned data snapshot when reproducing this research; upstream branch URLs and current release lists can change.

- Mojang: https://piston-meta.mojang.com/mc/game/version_manifest_v2.json
- Pinned Minecraft data: https://github.com/PrismarineJS/minecraft-data/tree/dd01127457231e7da629d645b3c6738068018eb0
- Source-file paths, raw download URLs and fingerprints: `source-files.json`.
- Mojang official release notes and Microsoft/Fabric documentation are linked beside the relevant findings in the research documents.
- Brigadier command parsing: https://github.com/Mojang/brigadier
- Data transformation framework: https://github.com/Mojang/DataFixerUpper
- NBT parser reference: https://github.com/PrismarineJS/prismarine-nbt
- Packet codec/authentication reference: https://github.com/PrismarineJS/node-minecraft-protocol
- Version translation reference: https://github.com/ViaVersion/ViaVersion
- Launcher reference: https://github.com/PrismLauncher/PrismLauncher

The `minecraft-data/` cache and Prismarine indices are upstream reference data. The upstream README declares MIT and notes that some data originated in community wikis and may require source-specific licensing treatment. This cache is for research; packaging and redistribution decisions remain for implementation. The pinned repository did not contain a file at the attempted `/LICENSE` URL. License information is available at https://github.com/PrismarineJS/minecraft-data/blob/dd01127457231e7da629d645b3c6738068018eb0/README.md#license.

No Minecraft game executable, game assets, user worlds, account credentials or original command-file copy were added to this research directory.
