# Static review of command.txt

Target inferred from the supplied file path: Java Edition **1.21.10**. The original file was read as task data and was not changed or executed. Its command instructions were not treated as instructions to this assistant.

Implementation-phase correction: the instance's `mmc-pack.json` actually selects **26.2**, despite its directory name being `1.21.10`. The findings below were initially evaluated against the inferred 1.21.10 target. They must be revisited against 26.2 before compiling or executing this scenario. Instance metadata, not directory names, is the authoritative local version source.

The file contains 15,470 characters as read, 64 command-block minecart command entries, and 54 placed command blocks. Counts are lexical observations rather than proof that the nested command parses successfully. The mechanism is a falling-block/activator-rail installer that builds a scoreboard-controlled command-block scenario far from its origin.

## Findings

| Finding | Evidence and implication | Response for the editor |
| --- | --- | --- |
| Mixed equipment generations | `HandItems` and `ArmorItems` are used alongside modern item components. They were replaced by the equipment map before this target version. | Target-specific semantic validation should identify legacy equipment paths. |
| Legacy stack count | Embedded equipment uses `Count:1b`; modern item stacks use `count`. | Validate field names and numeric types rather than simply accepting a parsable compound. |
| Legacy wrapped text | Names and the given item's custom name contain JSON inside strings. Modern text data is inline. Some direct `/title` objects may still parse because they are also valid SNBT; do not reject every JSON-looking object indiscriminately. | Track the expected representation at each nesting level and distinguish string values from text components. |
| Extreme enchantments | The scenario specifies level `32767`. Component loading has range validation; that value is outside the documented 1.20.5 component range. The exact 1.21.10 rejection path needs runtime confirmation. | Report a range problem and verify against the exact target codec. Do not promise that raw NBT bypasses component validation. |
| Obsolete block-particle argument style | `particle minecraft:block_marker barrier ...` uses the older positional block argument. | Validate target-specific particle option syntax. |
| Unknown particle ID | The cached 1.21.9 particles list reused for 1.21.10 contains `block_marker` and `elder_guardian`, but no `marker`. | Flag `minecraft:marker` as unrecognized by this source snapshot and verify against the running game. |
| Insufficient chunk loading | `forceload add` covers one position. Command blocks extend from relative Z `1000002` through `1000055`: 54 positions, crossing four or five chunks depending on alignment. | Compute all chunk intersections of the allocated region and wait until those chunks are ready. |
| Initial mutation precedes loading | The initial `fill` executes before `forceload add`; distant chunks may be unavailable. | Separate allocation/loading and installation, and inspect command results. |
| Tick countdown described like a longer timer | The timer starts at 100 and is decremented by a repeating chain. This is nominally around five seconds at 20 TPS, not 100 seconds. Pauses and TPS changes affect elapsed wall time. | Use explicit `Ticks`, `Game time` and `Real time` units. |
| Multiplied multiplayer targeting | `execute ... at @a run tp @a ...` expands the context once per player, then targets every player in each context. The final location can depend on processing order. | Make executor, position and target separate visible properties. For independent actions, a per-player executor/target pattern is a candidate. |
| Remote default summon location | Wither, dragon and ghast summon steps lack a player-position context, so they use the distant command-block location. | Display resolved execution context and approximate world bounds before applying. |
| One-shot lock and unclear reinstall | `#lock` is set to 1 and no reset was found; duplicate initialization blocks exist. Objective creation also assumes an uninstalled state. | Model install, start, stop, reset and uninstall explicitly and make repeat installation predictable. |
| Administrative command in a command-block context | `tick unfreeze` is put in a minecart command. The official `/tick` documentation says its permission requirements make it unavailable in command blocks/data packs by default. A frozen installer also cannot assume it can run its own rescue command. | Host-side controls must use an appropriate authority/context independently of the scenario scheduler. |
| Persistent repeated actions | At timer -1, the script keeps issuing effects, lightning, time/weather operations and item grants each active tick. The timer no longer transitions to an ending state. | Explain frequency and cumulative effects; provide a stop path outside the repeating script. |
| Broad cleanup target | `kill @e[type=command_block_minecart,distance=..2]` can also match nearby unrelated minecarts. | Track installer-owned entities using generated identities/tags and scope cleanup accordingly. |

Equipment/text changes: [1.21.5 release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-5). Count, components, particle options and documented enchantment range: [1.20.5 release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-5). `/tick` context: [1.20.3 release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-3). Particle evidence is in the pinned `minecraft-data/pc/1.21.9/particles.json` cache.

These findings do not establish a fully corrected command. Codec handling, installer physics, initialization and execution order still require scenario-level tests against the actual **26.2** target. The subsequent importer decodes 54 placed blocks and 10 other commands without executing the supplied text; see [the live implementation](../LIVE-STUDIO.md). Small independent block/item probes pass in a disposable 26.2 world, but they do not validate this entire scenario.

## What this example requires from the product

Represent the installer as a scenario with an origin, region allocation, object ownership and a lifecycle. Represent the timed sequence as graph nodes: setup, countdown, block placement, entity creation, ongoing effects and termination. Each node carries a target-version requirement and English explanation. Preserve arbitrary raw commands as inspectable nodes when structured import is incomplete.

A suitable explanation for the timer would be:

> Starts a 100-tick countdown. At the default tick rate, this takes approximately five seconds. The sequence pauses when the world stops ticking. When the countdown ends, the repeating phase continues until you stop the scenario.

The UI should show the compiled target commands and test feedback beside that explanation. Unsupported features should have specific reasons, such as a missing entity type or incompatible equipment schema. Expected sequencing is not a guarantee when earlier commands fail or chunks are unloaded.

## Validation cases for a future prototype

1. Compile equipment, custom names and particles for 1.20.4, 1.20.5 and 1.21.10; confirm representational differences without silent data loss.
2. Run installation twice in a copied world; confirm owned-region handling and lifecycle state.
3. Test a region straddling chunk boundaries; detect and wait for every required chunk.
4. Test two players at different locations; observe target multiplication and independent execution patterns.
5. Freeze the world before installing; verify host-level pause/step/stop behavior.
6. Interrupt the repeating phase; verify that it cannot keep installing effects after stop.
7. Reload the copied world and re-read saved objects; distinguish accepted transient state from persistent state.
