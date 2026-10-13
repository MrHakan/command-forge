# Universal Minecraft Editor: research baseline

Research date: 12 October 2026. Product language: English. This is preparation for architecture, not a finished architecture or an implementation.

## Agreed scope

- Every Java Edition version classified as `release` in Mojang's launcher manifest; alpha, beta, snapshots and Bedrock are outside the initial scope.
- Local singleplayer worlds and servers administered by the user.
- A real Minecraft instance visible and playable inside the application panel from the first release.
- Per-instance memory and runtime settings.
- NBT inspection/editing plus commands, command blocks, entities, equipment, effects, particles, sounds, game rules, scoreboards and authored scenarios.
- English explanations of prerequisites, execution order and observable results.
- Challenge assumptions and surface unsupported operations rather than silently approximating them.

The project directory was empty before this research. No existing application or repository instructions were found there or at the checked parent locations.

## Evidence collected

The downloaded [Mojang launcher manifest](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json) lists **103 releases**, from `1.0` to `26.3`. Their individual metadata files were downloaded and checked against the manifest's SHA-1 values. The manifest snapshot also contains 754 snapshots, 35 old alpha entries and 26 old beta entries; these were counted but not researched as supported targets.

The Minecraft data snapshot is pinned to commit `dd01127457231e7da629d645b3c6738068018eb0`. A conservative exact-name audit found **101 protocol ID matches**, **55 direct release entries**, **43 unique protocol schemas**, and **303 distinct JSON source files**, all downloaded and JSON-parsed. Command trees are available through direct mappings for **29 releases**. Sources include blocks, items, entities, effects, enchantments, particles, attributes, biomes and sounds where indexed. These are source coverage counts, not working-product coverage.

The audit intentionally does not invent aliases. For example, `1.7.10` uses the source's major name `1.7`, and Mojang's `1.0` is represented as `1.0.0` in the protocol list. Alias resolution can recover additional entries, but must retain the reason for every mapping. Missing exact entries therefore do **not** mean the upstream library cannot support a version. [Source index](https://github.com/PrismarineJS/minecraft-data/blob/dd01127457231e7da629d645b3c6738068018eb0/data/dataPaths.json), [version list](https://github.com/PrismarineJS/minecraft-data/blob/dd01127457231e7da629d645b3c6738068018eb0/data/pc/common/protocolVersions.json), [resolver implementation](https://github.com/PrismarineJS/node-minecraft-data/blob/master/index.js).

**No Minecraft instance was launched; no command, world edit, migration or embedding behavior was tested.** Downloading or structurally indexing a schema does not establish that every packet or gameplay feature has been understood. Exhaustive per-release feature research remains open.

## How to read the artifacts

| Artifact | Meaning |
| --- | --- |
| `java-release-inventory.csv` | Every in-scope release, launcher runtime, source protocol ID, schema paths and explicit untested statuses |
| `release-metadata/` | 103 checksum-verified Mojang metadata files; libraries, launch arguments, assets and runtime requirements |
| `release-coverage.json` | Per-release dataset paths, statuses, counts and available command names |
| `protocol-schema-index.json` | Packet type names and counts grouped by state and direction for 43 schemas |
| `minecraft-data/` | The 303 pinned upstream JSON files; a reference cache, not our own game implementation |
| `source-files.json` | Source URLs, byte counts and SHA-256 fingerprints |
| `coverage-summary.json` | Aggregate audit results |
| `build-coverage.ps1` | PowerShell 7 script rebuilding the exact-name audit from the saved manifest indices |
| `SAMPLE-REVIEW.md` | Static review of the supplied command; nothing from it was executed |
| `NEXT-PHASE.md` | Research gaps and experiments required before choosing the architecture |

Run `./build-coverage.ps1` from this directory with PowerShell 7 to rebuild coverage. It fetches missing files from the pinned commit and reuses cached files. SHA-256 values record local content; they are not signatures certifying upstream correctness. Mojang metadata verification must be repeated against the saved manifest if those files change.

`javaMajor` is the runtime declared by today's launcher metadata, **not proof of the historical minimum Java version**. Older releases may have absent or source-assigned data version values. Never assume that a number in the community version list proves that an old world contains a `DataVersion` tag.

## Version boundaries that change the design

| Boundary | Verified change | Consequence for this project |
| --- | --- | --- |
| 1.9 | Repeating and chain command blocks exist alongside impulse blocks | A visual chain editor needs legacy output behavior for earlier targets. [Official explanation](https://www.minecraft.net/pl-pl/article/block-week-command-block) |
| 1.13 development | Command redesign and flattening of block/item identities | Older commands and ID/data-value representations need dedicated handling. [Commands](https://www.minecraft.net/nl-nl/article/minecraft-snapshot-17w45a), [flattening](https://www.minecraft.net/en-us/article/minecraft-snapshot-17w47a) |
| 1.17 | Java 16 runtime transition | Choose a per-instance runtime. [Release notes](https://feedback.minecraft.net/hc/en-us/articles/4402626897165-Minecraft-Caves-Cliffs-Part-1-1-17-Java) |
| 1.18 | Java 17 runtime transition | Runtime and world-format compatibility are separate checks. [Release notes](https://feedback.minecraft.net/hc/en-us/articles/4415128577293-Minecraft-Java-Edition-1-18) |
| 1.19.4 | Display/interaction entities and new damage/ride commands | Scenarios need feature requirements, not just command strings. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-19-4) |
| 1.20.2 | Configuration protocol phase and function macros | Network state machines and scenario exporters must be versioned. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-2) |
| 1.20.3 | Administrative `/tick` command | Preview timing and pause/step controls need an appropriate execution context. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-3) |
| 1.20.5 | Java 21; item components, changed stack and particle forms | NBT syntax, item semantics and packet encoding require separate adapters. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-5) |
| 1.21.5 | Mob equipment map; inline text components; expanded SNBT | Equipment editors and nested text emitters must use the selected version's schema. Binary NBT list storage still has constraints. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-5) |
| 1.21.9 | Dedicated-server management API; command-block enablement becomes a game rule | Discover available management methods and choose the correct setting names. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-9) |
| 26.1 | Java 25 and unobfuscated executables | Modern runtime selection and extraction methods need an additional boundary. [Release notes](https://feedback.minecraft.net/hc/en-us/articles/44551668333837-Minecraft-Java-Edition-26-1) |
| 26.2 | Experimental Vulkan backend, management protocol 3.0.0, `/unpublish`, new physics attributes and command spam settings | Test both renderers for embedding; model startup readiness and new command/attribute/settings capabilities. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-2) |
| 26.3 | SDL3 replaces GLFW for windows/input; block-state field names change; management protocol 3.1.0 | Embedding needs a separate modern test family; block-state emitters and management discovery need updates. [Release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-3) |

These are verified architectural boundaries, not a complete feature history. Patch releases can change behavior even when they share a protocol schema. Versions must be ordered using metadata and explicit relations, not decimal arithmetic or lexicographic string comparison.

26.2 was already included in the release inventory but initially omitted from this summary table. Its inventory records Java 25, source protocol ID 776 and source data version 4903. Neither 26.2 nor 26.3 has an exact data-path entry in the pinned source index; their missing schemas remain research gaps, not evidence of unchanged behavior.

## Challenge and response

| Assumption to challenge | Response |
| --- | --- |
| Knowing all protocols provides a universal editor | Protocols describe transport. Commands, world storage, item components, registries, permissions and entity behavior are additional systems. |
| A shared protocol ID guarantees identical features | Preserve the exact game version and environment fingerprint. Reuse only explicitly evidenced data. |
| Any feature can work on every version | Features absent from the target version must be unavailable or require an explicitly chosen substitute. A vanilla legacy game cannot gain a modern entity through NBT alone. |
| A client can write arbitrary server NBT | Changes must be applied by the authoritative server or an authorized offline writer. Client-side packet construction cannot establish acceptance or persistence. |
| One bridge mod can support all releases | Treat bridge support as a per-version capability. Ancient and modern loader/API families need separate investigation. |
| A universal NBT tree is enough | Preserve binary types and unknown fields, but model semantic objects separately. File NBT, network NBT, SNBT and item component encoding are not interchangeable. |
| Text substitution can translate the attached command | Parse nested representations and use a structured scenario model. Record unsupported operations and semantic changes. |
| Every action can be undone | Snapshot restoration can reset a local preview. Commands with external or broad gameplay effects cannot promise general inverse operations. |
| All known releases are fully supported at launch | Keep the long-term all-release target, and report launch, embedding, parsing, command generation and live editing independently. |

## Design implications to carry into architecture

The application should preserve user intent as structured operations: entity creation, equipment assignment, conditions, tick delays, block operations, effects and text. Compilation selects an exact target version and emits appropriate commands, command-block layouts or supported function/data-pack output. Export routes have their own availability limits.

Each operation needs English metadata: purpose, arguments, constraints, version requirements, execution context, affected objects, expected result, and a way to inspect actual feedback. Raw editing remains available for expert use, while unknown data remains visibly unknown. A preflight report should distinguish valid syntax, expected gameplay behavior and observed test results.

A version profile should keep `gameVersion`, launcher metadata hash, network protocol identity, disk data identity, command grammar, registry snapshot, component/entity schemas, pack versions, Java runtime, native dependencies, loader/bridge support and embedding adapter separate. Capability checks must also consider the running server's configuration, loaded data packs and permissions.

NBT file editing should be performed against a stopped world or controlled copy, with original data retained. Live mutation must coordinate with the server's save and tick lifecycle. Reading a compressed NBT file does not imply that region files, separate entity storage or world upgrades are supported.

Use a supervised game process per instance; memory controls configure that process at startup. RAM allocation does not solve GPU, world-generation or server tick load. Launch resolution must handle libraries, asset indexes, native extraction and authentication rather than merely executing a JAR. [Prism Launcher](https://github.com/PrismLauncher/PrismLauncher) provides a reference implementation to examine during architecture selection.

The first release must embed the playable instance. A Windows native child-window approach is a candidate, not a proven solution. Microsoft documents window-style handling and DPI-related problems for `SetParent`; focus, cursor capture and renderer lifecycle still require experiments. [SetParent documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent).

## Research status

The all-release launcher inventory and currently indexed schema cache are complete for this snapshot. Sample review and major-boundary research are complete as static analyses. The exhaustive knowledge base, packet-level compatibility, per-field NBT schemas, executable launch/embedding and runtime validation remain unfinished. The next phase must address those gaps before making a universal compatibility promise.
