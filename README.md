# Command Forge

**Download:** Windows x64 builds are on the [Releases](https://github.com/MrHakan/command-forge/releases) page. Unzip and start `CommandForge.exe`; no .NET installation is needed. Requires Prism Launcher with a Minecraft account; live editing requires Java Edition 26.2.

License: GPL-3.0 (see `LICENSE` and `THIRD-PARTY-NOTICES.md`). Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.

Formerly *Universal Editor*. Internal identifiers that existing installs depend on (the `universal-editor-<release>` Prism instance, the `.universal-editor` IPC folder, the bridge mod and source folder names) keep the old name.

Windows prototype: build scenarios using visual timeline cards and searchable item/property forms beside a real embedded Minecraft window. Apply results to a Java 26.2 singleplayer world and inspect authoritative game read-back. Release/RAM controls and standalone binary NBT editing are also available.

The interface is a single dark workspace: a left page rail (**Scenario, Item, Commands, Import, NBT file, Settings**, `Ctrl+1-6`), the active page in the middle and the game panel on the right. Header badges show whether the game is embedded and whether the live bridge is connected. Errors and confirmations appear as a dismissible strip above the page instead of dialogs, so nothing steals focus from the game. See the [UI guide](docs/UI-GUIDE.md). Optional Discord Rich Presence shows the current page and game state on your Discord profile; see [Discord setup](docs/DISCORD.md).

## Run

Requires Windows x64, the .NET 9 desktop runtime/SDK and Prism Launcher with a usable Minecraft account. From PowerShell 7 in this directory:

```powershell
./launch.ps1
```

### Standalone executable

```powershell
./publish.ps1 -Verify -Zip
```

Creates `dist/CommandForge/CommandForge.exe`, a self-contained single-file x64 build (about 110 MB, no .NET installation needed), next to `docs/research` (release catalog) and `artifacts/universal-editor-bridge-26.2.jar` (live bridge). Copy or unzip the whole folder anywhere and start the exe. `-FrameworkDependent` produces a small exe that requires the .NET 9 Desktop Runtime. `-Verify` runs the windowless test modes against a temporary copy of the published exe. Settings, the autosaved draft and `crash.log` live in `%LOCALAPPDATA%/CommandForge` (copied once from the former `UniversalEditor` folder). The bundle is uncompressed because compressed bundling failed with *Access denied* on the development machine. A JAR build is not possible: the host is a .NET WinForms application.

To start minimized without activating the editor over a fullscreen game, use `./launch.ps1 -Background`. Restore it from the taskbar when ready. This starts the editor only; Minecraft still starts through its launch button.

Item/block selection includes real local Minecraft textures, an inventory-style searchable gallery and a large item preview. Effects, mob spawn eggs and scenario cards also have artwork. Assets come from the installed 26.2 client JAR; there is no additional image download. Unsupported special models retain text choices and a placeholder.

Pick a release in the game panel header, set RAM under **Settings** and click **Launch**. The application creates a separate vanilla Prism instance named `universal-editor-<version>` and uses Prism's supported `--dir` / `--launch` interface. A fresh private launcher session reads that profile without restarting the user's launcher. Session directories are under `%LOCALAPPDATA%/UniversalEditor/launcher-sessions`; assets, libraries, metadata and translations use directory junctions to the selected Prism installation. The account file is shared through a same-volume hard link and read by Prism, not parsed by the editor. The private launcher may refresh its own account-file path. Prism data must be on the same volume as local application data for this initial integration. Downloads and account selection remain handled by Prism. Existing instances, mods and worlds are not imported. Launcher paths can be changed under **Settings**; RAM, paths, last page and panel layout are remembered per Windows user.

Click **Focus game** to transfer keyboard focus. **Detach** restores its original window. **Close game** requests normal game shutdown. **Hide game panel** gives the editor the full width while no game is embedded. Closing the editor restores the game to its own window. A new launch is detected from a new Java process; ambiguous multiple launches are rejected. Avoid launching another Minecraft instance concurrently during automatic detection.

RAM applies at the next startup. A source protocol schema is reference coverage and does not establish working gameplay compatibility. All 103 releases appear in the selector; actual launch and embedding support requires per-version validation. The initial implementation is Windows-only and depends on Prism rather than implementing a standalone account/download pipeline.

The **NBT file** page opens standalone Java big-endian NBT with plain, gzip or zlib compression. Scalar values and typed arrays can be edited. Save creates a new file and refuses to overwrite an existing file. Names, binary tag types, compound ordering, list types and Java modified UTF-8 code units are preserved. File/decompressed size is limited to 64 MB, collection sizes to one million and depth to 64.

Live editing is implemented only for **26.2 private singleplayer**. Other releases currently launch vanilla. Owned dedicated-server transport, region/world readers, general SNBT editing and version conversion remain to be built. Catalog presence does not imply feature support. See [visual authoring guide](docs/NO-CODE-DESIGN.md), [live architecture](docs/LIVE-STUDIO.md) and `docs/research/` for the remaining gates.

## Live studio

Build the Java bridge from the local official 26.2 game jar with Java 25:

```powershell
./bridge/build.ps1
```

In this sandbox, newly compiled class files may require separate `-CompileOnly` and `-PackageOnly` invocations. No game classes or dependency jars are bundled. When `artifacts/universal-editor-bridge-26.2.jar` exists, the managed 26.2 profile uses Fabric Loader 0.19.5 and loads this bridge instead of launching vanilla.

With the default **Settings → World on launch**, 26.2 creates an editor world on the first launch (superflat, creative, cheats on, always day, clear weather, no mob spawning, quiet command blocks) and reopens it on later launches; you can instead create a new world each time, open a chosen world or stay on the main menu. The request is a one-shot file in the managed instance (`.universal-editor/launch-world.json`) that the bridge reads on the title screen; existing worlds are never overwritten, and rules are applied only to the world just created. When a world session starts with nothing installed, the anchor moves next to the player if it was unset or far away. Otherwise open a private singleplayer world yourself. The editor connects automatically (the **Live** badge turns green) while the game remains visible beside it. The live bar above the Scenario and Commands pages holds the actions below; it is disabled while offline and states which design it applies.

1. **Near player** chooses the anchor. Check that the whole southward (+Z) strip is air and loaded. Limits: 128 blocks, origin within 64 blocks of the player.
2. On the **Scenario** page, start from **Templates** or **+ Add action** and select cards. Choose actions, delays, intervals, time windows, target tags and variable conditions. Messages, particles, effects, mobs, items, teleports, sounds, blocks, time/weather and variables have forms. Cards that would not compile are outlined in red with the reason. **Generated commands** is an optional inspection view. Undo/redo (`Ctrl+Z` / `Ctrl+Y`) covers card edits, reordering and template loads.
3. **Stage** (`F6`) validates commands with the exact game parser and creates inactive blocks. **Run / restart** (`F5`) arms them and resets the timeline. **Live sync** applies edits after 700 ms, preserving its clock and chosen run/stage state.
4. Actual commands, success counts and output return to the panel. **Read game blocks** pulls changes made in Minecraft. Detected external changes switch Live sync off.
5. **STOP** (`Shift+F5`) disables and clears owned commands. Normal editor/world closure stops them; a four-second lost editor heartbeat stops them too. Previously executed effects, summons, fills and inventory changes are not undone.

The **Item** page has searchable item choices, appearance/lore, multiple enchantments, attribute modifiers, unbreakable, damage and glow controls, with an approximate Minecraft-style tooltip preview. **Apply to held item** replaces the main-hand slot explicitly. **Load held item** loads actual held components back into a draft and preserves unmapped complex components, including unchanged typed SNBT values. **Copy /give command** puts the generated command on the clipboard. Not every component has a form yet.

**Import → Import command.txt…** decodes the supplied minecart installer format into the **Commands** page. The actual sample yields **54 editable blocks and 10 other installer commands**. Setup/cleanup stays in review and never executes automatically. A separate one-command control executes explicitly entered setup at the player's position. Import is a linear draft: relocation, initialization and outdated syntax need review; it does not automatically become an equivalent visual scenario. **Ctrl+S / Ctrl+O** save and open project files for the scenario or the command list (whichever of the two pages was used last). Separately, the working state (scenario, item draft, command list, anchor) is autosaved to `%LOCALAPPDATA%/CommandForge/draft.json` and restored on the next start. Running state and installer review text are not serialized.

Launch sessions share persistent asset/library caches and seed their HTTP metacache from the previous owned session (or the selected Prism installation). Prism can still show **Updating assets index** during its normal launch check; that dialog does not establish a full game redownload. Unchanged bridge JARs are not overwritten. Build/deployment validates archive readability before starting Fabric. If an older artifact gives **Access denied**, rebuild with `./bridge/build.ps1` and retry after closing the editor-managed game.

## Verify

```powershell
dotnet build ./src/UniversalEditor/UniversalEditor.csproj -c Release
Start-Process ./src/UniversalEditor/bin/Release/net9.0-windows/CommandForge.exe -ArgumentList '--self-test' -Wait -WindowStyle Hidden
Get-Content ./artifacts/self-test-results.json
```

`--self-test-headless` runs the sixteen of these that need no desktop window and writes `artifacts/self-test-headless-results.json`. Eighteen local tests cover binary NBT, malformed data, profile ownership, native embedding, installer decoding, session IPC, form selections, timeline compilation, typed item preservation and bridge deployment. Fifteen real-game checks are separate in `artifacts/live-smoke-results.json`. The developer mode `--live-smoke <game-directory>` refuses to mutate a world unless the bridge reports a disposable editor smoke-world name. Test-world creation requires an explicit developer fixture flag; normal launches never create one automatically.

## Implementation choice

For visual verification without desktop windows or a game launch (this also renders every page of the main window to `artifacts/ui-*.png`; these are offscreen control renders, not screenshots of a running app, and closed dropdowns appear empty in them):

```powershell
Start-Process ./src/UniversalEditor/bin/Release/net9.0-windows/CommandForge.exe -ArgumentList '--asset-tests' -Wait -WindowStyle Hidden
Get-Content ./artifacts/asset-test-results.json
```

.NET Windows Forms hosts the native viewport; Minecraft remains a separate Java process. A Java/Fabric bridge applies changes on the integrated server thread and sends authoritative state back through local file IPC. Full-screen/input capture, mixed DPI, Vulkan and all-version embedding need separate tests.

Reference APIs: [Prism CLI](https://www.prismlauncher.org/wiki/getting-started/command-line-interface/), [Windows SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent).
