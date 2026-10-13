# Discord Rich Presence

When enabled, your Discord profile shows **Playing Command Forge** with the current page, a short state line, elapsed time, a large page image and a small game-status badge.

| Page | Details | State (names off) | Large image |
| --- | --- | --- | --- |
| Scenario | Designing a scenario | 5 action cards | `scenario` |
| Item | Crafting an item | Diamond Sword · 1 enchantment | `item` |
| Commands | Editing a command block chain | 54 command blocks | `nbt` (holds the command-chain art) |
| Import | Reviewing a command.txt import | 54 command blocks in the draft | `logo` |
| NBT file | Inspecting an NBT file | Binary NBT data | `logo` |
| Settings | Configuring the editor | Minecraft 26.2 | `logo` |

Small badge: `live` (bridge connected), `game` (Minecraft launching or embedded), `idle` (no game).

With **Include scenario, item and file names** on, the state line also contains the scenario name, the item's custom name or the NBT file name. Player names, worlds, servers and coordinates are never sent.

## How it works

The editor talks to the Discord desktop app on the same PC through its local IPC pipe (`\\.\pipe\discord-ipc-0` … `9`). It sends `SET_ACTIVITY` frames at most every four seconds, only when something changed, reconnects every 15 seconds when Discord is closed, and clears the activity when the editor closes. It does not use the network, a Discord login or a token. Developer smoke/QA and test modes never connect.

## One-time setup (Discord account owner)

1. Discord Developer Portal → **New Application** → name it `Command Forge` (this name appears after “Playing”).
2. The **Command Forge** application ID `1559628127410331810` is built in (`DiscordPresence.DefaultApplicationId`); **Settings → Discord application ID** can override it. `CommandForge.exe --discord-check` verifies the ID with a handshake only (no activity is shown) and writes `artifacts/discord-check.json`.
3. **Rich Presence → Art Assets**: upload the images under these exact keys:

| Key | Size | Shown as |
| --- | --- | --- |
| `logo` | 1024 × 1024 | large image (Import, Settings) |
| `scenario`, `item`, `nbt` | 1024 × 1024 | large image per page |
| `live`, `game`, `idle` | 512 × 512 | small circular badge on the large image |

Uploaded so far: `game`, `idle`, `item`, `live`, `logo`, `nbt` (command-chain art), `scenario`. Asset keys cannot be renamed; for separate Commands and NBT art, upload the chain image as `commands` and a new image as `nbt`, then map page 2 to `commands` and page 4 to `nbt` in `MainForm.UpdatePresence`. Newly uploaded assets can take a few minutes to appear. Source images are kept in `branding/discord/`.
