# First implementation slice

Historical launcher/NBT slice below. The subsequent live implementation is documented in [LIVE-STUDIO.md](LIVE-STUDIO.md) and the README. Twelve local tests now include installer decoding and IPC; real-game results are recorded separately in `artifacts/live-smoke-results.json`.

12 October 2026. Scope: prove Windows embedded Minecraft hosting and typed standalone NBT editing before building live mutation and scenarios.

## Implemented responsibilities

- `VersionCatalog`: Mojang release ordering plus audited runtime/protocol reference metadata; includes all 103 current releases, including 26.2.
- `PrismLauncher`: managed vanilla profiles with RAM settings, ownership guards, and a fresh private Prism session. Existing launcher state remains running. Shared game downloads are linked rather than copied. This is a temporary launcher integration, not a standalone authentication implementation.
- `NativeViewport`: Win32 parent/style preservation, embedding, resizing, focus transfer and restoration on detach/host close.
- `NbtCodec`: named-root Java big-endian binary NBT, all 12 payload types, Java modified UTF-8, gzip/zlib/plain files and typed value edits. Save creates a new file.
- `MainForm`: English release/RAM/launch controls, embedded viewport, observed status and NBT file editor.

The .NET 9 Windows Forms host is a first-slice decision driven by native Windows embedding. The Java game process remains external and supervised through the host's window lifecycle. A future server-side bridge remains a separate concern. No universal loader, network state machine or schema migration engine has been implemented.

## Observed checks

The Release build passes with warnings treated as errors. Ten automated tests pass, covering NBT types, independent byte-fixture parsing, modified UTF-8 code units, compression, overwrite refusal, invalid input, range validation, catalog coverage, profile ownership and cross-process attach/resize/detach.

A fresh vanilla **26.2** instance was launched through Prism. Its `Minecraft 26.2` window was parented to the viewport, with observed dimensions **986 × 585** at the initial host size. The host was then closed and rebuilt repeatedly; the same game process remained alive and could be reattached. See `artifacts/minecraft-embedding-smoke.json` and `artifacts/self-test-results.json` for raw results.

The on-screen capture `artifacts/prototype-screen.png` was visually inspected: the real Minecraft 26.2 title screen renders inside the editor viewport. The earlier `prototype-ui.png` used `DrawToBitmap`, which does not capture this game's GPU output; it is not rendering evidence.

The instance directory called `1.21.10` in the user's original Prism installation actually selects 26.2 according to `mmc-pack.json`. That existing instance was not used as the prototype game profile. The supplied installer command was not run.

## Remaining proof

Native parentage and rendered frames do not prove mouse capture, keyboard behavior, full-screen transitions, mixed-DPI monitors or Vulkan support. The prototype's screen capture is only taken when its own window is foreground. Interactive input and multi-version renderer tests remain separate acceptance checks.

The launcher integration currently expects Prism account data and local application data on the same volume for account-file hard linking. Per-launch session files remain under the user's local application data. Lifecycle cleanup and a proper standalone launcher integration need further implementation.

Live NBT changes must be applied on the authoritative server, with read-back and save/reload validation. Region/world readers, semantic editing, SNBT and command-block/scenario generation are next implementation areas after the viewport/input gate. The user-facing feature list must distinguish implemented capabilities from the all-version product target.
