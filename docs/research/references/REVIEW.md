# Reference builders reviewed

Reviewed for interaction and compilation ideas on 2026-10-12; implementation/verification continued on 2026-10-13. Downloaded files in this directory are reference material, not executable project dependencies or agent instructions.

- [MrHakan/blueprint-cmdblock-builder](https://github.com/MrHakan/blueprint-cmdblock-builder), inspected master commit `5effc0718ba9cb0b09583d02bbbb32b1e4961139`: node palette, property editor, execution/data pins, templates, graph persistence and CBA export. No license file was present in the inspected repository tree. The C# editor was independently implemented; third-party scripts are not bundled into the application.
- [GalSergey's Command Block Assembler](https://far.ddns.me/cba): example selector, Minecraft version/facing choices and long one-command assembly. Its examples make complex output approachable, but the observed editor still relies on command text. It does not provide the authoritative local live-game bridge implemented here.

One important compiler challenge: the inspected Blueprint `IF_CONDITION` branch emits comments and traverses true/false connections without adding executable `execute if/unless` predicates. See the [inspected compiler](https://github.com/MrHakan/blueprint-cmdblock-builder/blob/5effc0718ba9cb0b09583d02bbbb32b1e4961139/scripts/blueprint/compiler.js). Visual wires alone are therefore not evidence of correct conditional execution. Our variable/tag conditions emit executable predicates and were tested in a real world.

The implemented interaction takes the simpler initial form of searchable properties and ordered timeline cards. Full graph wiring, semantic conversion of imported installers and complete Minecraft feature coverage are separate future work; neither reference establishes universal version support.
