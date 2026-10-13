package editor.bridge.mixin;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import editor.bridge.Bridge;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.*;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPresets;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import java.nio.file.*;
import java.util.UUID;

// Opens or creates a world from the title screen when the editor left a one-shot request in this instance's
// .universal-editor folder. Also serves the explicit developer smoke fixture. Without a request file, launches
// stay on the main menu as before.
@Mixin(Minecraft.class)
public abstract class TestWorldMixin {
    @Unique private boolean editorOpening, editorKeepRunning;
    @Unique private long editorOpeningSince;

    @Inject(method = "tick", at = @At("HEAD"))
    private void editorTestWorld(CallbackInfo ci) {
        Minecraft game = (Minecraft)(Object)this;
        if (editorOpening && game.player != null) {
            editorOpening = false;
            if (editorKeepRunning) game.options.pauseOnLostFocus = false;
            game.gui.setScreen(null);
        }
        if (editorOpening && System.currentTimeMillis() - editorOpeningSince > 120_000) editorOpening = false;
        if (!(game.gui.screen() instanceof TitleScreen)) return;
        Path folder = game.gameDirectory.toPath().resolve(".universal-editor");
        Path smoke = folder.resolve("create-test-world"), request = folder.resolve("launch-world.json");
        try {
            if (Files.isRegularFile(smoke)) {
                if (!Files.readString(smoke).equals("universal-editor-disposable-smoke")) return;
                Files.delete(smoke);
                String id = "UniversalEditor-Smoke-" + UUID.randomUUID().toString().substring(0, 8);
                editorKeepRunning = true; begin();
                var settings = new LevelSettings(id, GameType.CREATIVE,
                    new LevelSettings.DifficultySettings(Difficulty.PEACEFUL, false, false), true, WorldDataConfiguration.DEFAULT);
                game.createWorldOpenFlows().createFreshLevel(id, settings, new WorldOptions(42L, false, false),
                    WorldPresets::createTestWorldDimensions, game.gui.screen());
                return;
            }
            if (!Files.isRegularFile(request)) return;
            if (Files.size(request) > 16 * 1024) { Files.delete(request); return; }
            JsonObject plan = JsonParser.parseString(Files.readString(request)).getAsJsonObject();
            Files.delete(request); // one shot: a failed or cancelled open never loops
            if (!"command-forge-launch-1".equals(text(plan, "format", ""))) return;
            String levelId = text(plan, "folder", "");
            if (!levelId.matches("[A-Za-z0-9 _().-]{1,64}") || levelId.contains("..") || levelId.endsWith(".") || levelId.endsWith(" ")) return;
            editorKeepRunning = !plan.has("keepRunning") || plan.get("keepRunning").getAsBoolean();
            var storage = game.getLevelSource();
            if ("open".equals(text(plan, "action", "")) && storage.levelExists(levelId)) {
                begin(); game.createWorldOpenFlows().openWorld(levelId, () -> game.gui.setScreen(new TitleScreen()));
                return;
            }
            // Create: never reuse an existing folder.
            String unique = levelId;
            for (int i = 2; storage.levelExists(unique) || !storage.isNewLevelIdAcceptable(unique); i++) {
                if (i > 99) return;
                unique = levelId + " (" + i + ")";
            }
            String name = text(plan, "name", unique);
            if (name.isBlank() || name.length() > 64) name = unique;
            GameType mode = switch (text(plan, "gameMode", "creative")) { case "survival" -> GameType.SURVIVAL; case "adventure" -> GameType.ADVENTURE; default -> GameType.CREATIVE; };
            Difficulty difficulty = switch (text(plan, "difficulty", "peaceful")) { case "easy" -> Difficulty.EASY; case "normal" -> Difficulty.NORMAL; case "hard" -> Difficulty.HARD; default -> Difficulty.PEACEFUL; };
            boolean flat = !"normal".equals(text(plan, "preset", "flat"));
            boolean structures = plan.has("structures") && plan.get("structures").getAsBoolean();
            var seed = WorldOptions.parseSeed(text(plan, "seed", ""));
            var options = new WorldOptions(seed.isPresent() ? seed.getAsLong() : WorldOptions.randomSeed(), structures, false);
            var settings = new LevelSettings(name, mode, new LevelSettings.DifficultySettings(difficulty, false, false), true, WorldDataConfiguration.DEFAULT);
            JsonObject rules = plan.has("rules") && plan.get("rules").isJsonObject() ? plan.getAsJsonObject("rules") : new JsonObject();
            Bridge.requestNewWorldSetup(name, flag(rules, "alwaysDay"), flag(rules, "clearWeather"), flag(rules, "noMobSpawning"), flag(rules, "quietCommandBlocks"));
            begin();
            game.createWorldOpenFlows().createFreshLevel(unique, settings, options,
                flat ? WorldPresets::createTestWorldDimensions : WorldPresets::createNormalWorldDimensions, game.gui.screen());
        } catch (Exception error) {
            // A malformed request must not crash the game; the user stays on the title screen.
            editorOpening = false;
        }
    }
    @Unique private void begin() { editorOpening = true; editorOpeningSince = System.currentTimeMillis(); }
    @Unique private static String text(JsonObject o, String key, String fallback) {
        return o.has(key) && o.get(key).isJsonPrimitive() ? o.get(key).getAsString() : fallback;
    }
    @Unique private static boolean flag(JsonObject o, String key) { return o.has(key) && o.get(key).isJsonPrimitive() && o.get(key).getAsBoolean(); }
}
