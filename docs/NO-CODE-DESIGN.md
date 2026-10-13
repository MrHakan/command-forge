# Visual scenario and item authoring

The editor opens on the **Scenario** page, with **Item** next to it in the left rail. You can create the supported actions and item properties without writing Minecraft commands. **Commands** remains available for actions that do not yet have a form.

## Build a scenario

Use **Templates** (Countdown reveal, Fog introduction, Particle loop, Mob showcase) or **+ Add action**, which lists actions by group with their Minecraft icons and inserts after the selected card. Select a card to edit it; drag cards to change execution order (a line shows the drop position), or use `Alt+Up/Down`, `Ctrl+D` duplicate, `Del` delete and the right-click menu. A card's scheduled time controls when it runs, so moving it does not change its delay. The time column beside the cards shows each start time or repeat interval. Cards are colour-coded by group and carry chips for timing, end time, tag filters and variable conditions; a card that would not compile is outlined in red and its form shows the reason. The footer shows the compiled command-block budget (of 128). Template loads and edits can be undone with `Ctrl+Z`.

Choose an action: message, simple particle, effect, summon, give item, teleport, sound, set block, time, weather, variable or player tag. Select resources using searchable lists supplied by the current game's registries. Before connection, a small starter list is available. Parameterized particle types such as dust are not exposed yet.

Timing choices are On start, Once after delay, Every game tick and Every interval. Repeating cards can have an end time measured from scenario start. Time is game time: 20 ticks equal one configured second; pausing and server lag affect wall-clock duration. Minimum repeat interval is 0.05 seconds. End ticks are inclusive.

Variables are shared within a scenario. Initialize them with On start cards, then Set/Add/Subtract and gate other cards with Equals, Not equal, At least or At most. Variable commands run once per matching tick, regardless of player count. An unset variable has Minecraft's missing-score behavior; initialize it before use. Player tags can include or exclude targets. Teleport destinations are absolute; particles, summons, sounds and set-block positions are offsets from each selected player.

**Near player → Stage in game → Run / restart** places actual repeating/chain command blocks and starts the timeline. Run/restart resets the project clock. Live sync preserves the clock; an action whose one-shot time already passed will not replay until restart. Stop clears/deactivates owned block commands and does not reverse their world effects. Repeated summons and item grants accumulate entities/items.

The compiler generates a clock and interval helper blocks. Limits are 100 action cards and 128 compiled physical blocks; an interval card uses five blocks. Generated blocks shows the exact output for inspection. This is an ordered visual timeline, not yet a free-wiring Blueprint graph.

## Build an item

The item library now uses Minecraft's real local textures in a searchable inventory grid and icon dropdown. The header shows a large item preview with name, count and lore. Blocks have a cube thumbnail when their model is a full cube; other supported models show a texture preview. Effects use their real status icons, entities use their spawn-egg artwork, and particles show a source sprite. Scenario cards also carry Minecraft artwork.

Artwork is read from the locally installed official **26.2 client JAR** in the selected Prism library cache. Nothing is downloaded or bundled into the editor. Client item definitions populate the library before connecting to a world; a connected bridge supplies authoritative registry choices. Missing textures keep text choices usable. Pixel art uses nearest-neighbor scaling, a bounded image cache and on-demand decoding of visible entries.

These are static base-model previews. Runtime glint, damage states, custom model data, full special/entity model rendering, resource packs and mod-provided assets are not implemented. Unsupported images show a placeholder rather than a fabricated model.

Appearance supports searchable item selection, count, display name, color, bold/italic, multiline lore, unbreakable, damage and glow override. Enchantments supports multiple entries with levels. Attributes supports multiple modifiers, amount, operation and equipment slot; adding modifiers replaces the game's default modifier list for that component.

**Apply to held item** explicitly replaces the current player's main-hand item. **Load held item** loads authoritative held-item data into a draft. Draft changes require Apply; they do not silently overwrite an item edited in Minecraft. Actual state is also visible in the **In game** tab, and **Command** shows the generated argument. The header shows an approximate Minecraft-style tooltip (name colour and style, enchantments with Roman levels, lore, Unbreakable and attribute lines); the game renders the final item.

Simple supported fields are hydrated back into their forms. Unknown/complex components are retained, and their original SNBT value tokens preserve byte/short/long/typed-array distinctions on unchanged components. Complex text or lore is preserved rather than flattened. Explicitly adding a form value for a preserved component replaces that entire component. This is not a general editor for every possible component or rich text structure yet. The exact game's parser/codecs validate the final item.

## Save and import

Save/open project (`Ctrl+S` / `Ctrl+O`) stores structured action choices, per-project objective identity, timing, conditions and copied item recipes. Anchor, running state and the standalone item draft are not part of the scenario file; they are kept in the autosaved working draft under `%LOCALAPPDATA%/CommandForge/` and restored on the next start. The Give item card copies a recipe from Item designer and keeps that snapshot; later item-draft changes do not silently modify the card.

Importing command.txt still extracts 54 block commands and 10 installer commands into an advanced linear draft. It does not infer equivalent visual cards, placement contexts, initialization or cleanup. Imported setup remains review-only until explicitly executed. Supporting arbitrary complex installers without code requires a semantic import/compiler layer in addition to this form palette.

## Verified scope

Java **26.2, private singleplayer**, remains the implemented live adapter. The other releases are listed for launching/research, not validated live editing. Owned dedicated servers, all-version component forms, parameterized particles, full mob NBT/equipment, block-state forms, richer selectors and arbitrary visual graphs remain future work.

Local test results are in `artifacts/self-test-results.json`; exact-game integration results are in `artifacts/live-smoke-results.json`. Tests cover real timeline windows/interval counts, live-edit clock preservation, restart, tag filters, selectable item components and typed custom-data round trips. Viewport input capture across all renderers/DPI combinations is not established by these tests.

`--asset-tests` is a separate verification mode that never shows a desktop window or launches Minecraft. It renders controls to bitmap files and writes `artifacts/asset-test-results.json`. Ordinary self-tests include visible embedding tests and should not be used while the user's fullscreen game must remain undisturbed.
