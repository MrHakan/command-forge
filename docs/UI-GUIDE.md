# Interface guide

One window, three columns: a page rail on the left, the active page in the middle and the game panel on the right.

## Layout

| Area | Contents |
| --- | --- |
| Header | Current project name (`•` = unsaved changes), **Live** badge (bridge state; hover for player, dimension and last game message), **Game** badge (not running / launching / embedded / closed), **Hide game panel** |
| Page rail | **Scenario** · **Item** · **Commands** · **Import** · **NBT file** · **Settings**. `Ctrl+1` … `Ctrl+6`, or arrow keys when the rail has focus |
| Notification strip | Errors, warnings and confirmations appear above the page and never open a dialog, so focus stays where it is. Info/success messages disappear after a few seconds; errors stay until dismissed |
| Game panel | Release picker, **Launch**, then **Focus game / Detach / Close game** once embedded. Before launch it shows the three setup steps |
| Status bar | Last event, shortcut summary and the **Activity** log toggle (log appears under the game) |

## Live bar (Scenario and Commands pages)

**Run / restart** (`F5`), **Stage** (`F6`), **STOP** (`Shift+F5`, works from every page), **Live sync**, **Read game blocks**, anchor X/Y/Z and **Near player**. The right side states whether it applies the visual scenario or the command list — whichever of those two pages you used last. The buttons are disabled while the bridge is offline or while the game has not yet acknowledged the previous request. The second row shows the last game response (✓ accepted / ✕ rejected); rejections also appear in the notification strip.

## Scenario page

- Editable scenario name at the top, **Undo/Redo** (`Ctrl+Z` / `Ctrl+Y` outside text fields), **Templates** and **+ Add action** (grouped: Show & sound, Players, World, Logic).
- Timeline cards: icon, number, action name, a summary of the selected resource or text, and chips for timing, end time, nearest-player, tag filter and variable condition. The colour strip marks the group. A red outline with a *needs attention* chip means the card would not compile; the form repeats the reason.
- Mouse: click selects, drag reorders (a line marks the drop position), right-click opens Duplicate / Move / Delete, the dashed card at the end adds an action.
- Keyboard on the timeline: `↑/↓` select, `Alt+↑/↓` move, `Ctrl+D` duplicate, `Del` delete, `Insert` add.
- The form is grouped into **Action**, **When**, **Who** and **Only if**. Colours show swatches; resources use the Minecraft artwork gallery; positions use a single X/Y/Z row.
- Footer: cards → compiled command blocks out of 128.

## Item page

The header shows the slot artwork and an approximate in-game tooltip (name colour/bold/italic, enchantments with Roman numerals, lore, Unbreakable, attribute lines, item ID). Buttons: **Apply to held item**, **Load held item** (enabled only while connected) and **Copy /give command**. Tabs: Appearance, Enchantments (one line per enchantment: name, level, remove), Attributes, In game (actual held item and raw SNBT), Command (generated argument).

## Commands page

Chain list with repeating/chain block artwork, command preview and a COND marker; **+ Block** inserts after the selection. The editor uses a monospace command field. **Game read-back** shows the selected block's position, success count and output after Stage/Run.

## Import page

Import command.txt (nothing executes), installer review, a separate **Execute once in game** box for reviewed setup commands, and a short live-editing walkthrough.

## World on launch (Settings, 26.2 only)

- **Open my editor world** (default): the first 26.2 launch creates a world from the options below and remembers it; later launches open it straight from the title screen.
- **Create a new world every time**, **Open a specific world** (worlds of the managed instance, newest first) or **Stay on the main menu**.
- New-world options: name, Superflat or default terrain, game mode, difficulty, seed, structures, and one-time rules: always day, clear weather, no natural mob spawning, hidden command-block chat output. Cheats are always on.
- **Keep the world running while the editor has focus** turns off pause-on-lost-focus for worlds the editor opens.
- Existing worlds are never overwritten; a name clash gets a numbered folder. Other releases launch vanilla and stay on the main menu.

## Saved state

- Settings (RAM, Prism paths, release, last page, splitter, activity log) are saved to `%LOCALAPPDATA%/CommandForge/settings.json` when the editor closes.
- The working draft (scenario, item draft, command list, anchor, last project path) is autosaved about two seconds after edits and on close to `%LOCALAPPDATA%/CommandForge/draft.json`, then restored on the next start.
- Developer smoke/QA launch modes do not read or write either file.

## Not covered by this UI pass

Free-wiring graph editing, semantic command.txt conversion, more component/mob/block-state forms, dedicated servers, other version adapters and resource-pack artwork remain open. Dropdown contents are drawn by Windows and cannot be fully themed; the offscreen test renders show closed dropdowns empty because `DrawToBitmap` does not capture them.
