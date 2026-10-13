# Bridge archive and repeated launch verification

On 2026-10-13 Fabric reported “Mod discovery failed” because Java could not open the editor bridge JAR (“Access denied”). This was reproduced with the JDK jar tool independently of Fabric. The error did not establish a Minecraft/mod version mismatch.

Packaging now completes the ZIP in memory, disposes the ZIP writer, writes the final archive and verifies readability. Deployment reads/validates the archive before launch, writes a verified temporary file and replaces only the editor-owned mod. Identical content is left unchanged. A locked/unreadable/broken archive produces a specific launcher error before starting Fabric. The corrected archive was opened by the JDK jar tool and loaded in two real 26.2 launches with a connected bridge.

Prism launch sessions share persistent assets/libraries/meta/translations, including when those folders must first be created. HTTP metacache is seeded from a previous session marked as owned by the editor for that Prism data directory, or from the chosen Prism installation. The source launcher metacache is not written by the editor.

Prism 10.0.5 still sets the **Updating assets index** status and marks the index entry stale during launch. This is visible in its [AssetUpdateTask implementation](https://github.com/PrismLauncher/PrismLauncher/blob/10.0.5/launcher/minecraft/update/AssetUpdateTask.cpp). The index request/check can still occur; suppressing the dialog is not the same as eliminating unnecessary full-game downloads.

`artifacts/cache-relaunch-results.json` records unchanged modification times, lengths and SHA-256 hashes for the Minecraft 26.2 client, Fabric Loader, ASM and bridge JAR across the second launch. Those four files were reused rather than downloaded/replaced. This check does not claim there were zero network requests for authentication, metadata or index validation.
