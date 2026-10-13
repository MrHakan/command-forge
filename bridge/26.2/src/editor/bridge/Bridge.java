package editor.bridge;

import com.google.gson.*;
import com.mojang.serialization.JsonOps;
import com.mojang.brigadier.exceptions.CommandSyntaxException;
import net.minecraft.commands.*;
import net.minecraft.core.*;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.core.particles.SimpleParticleType;
import net.minecraft.nbt.NbtOps;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.*;
import net.minecraft.server.permissions.LevelBasedPermissionSet;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.BaseCommandBlock;
import net.minecraft.world.level.block.*;
import net.minecraft.world.level.block.entity.CommandBlockEntity;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

// File IPC is restricted to this instance, and every mutation runs on the integrated server thread.
public final class Bridge {
    private static final Gson JSON = new GsonBuilder().setPrettyPrinting().create();
    private static MinecraftServer current;
    private static Path directory;
    private static String session, lastRequest = "", message = "Ready";
    private static String activeTimeline = "";
    private static boolean catalogWritten;
    private static boolean accepted;
    private static boolean armed;
    private static Integer commandResult;
    private static String ioError = "";
    private static long nextPoll;
    private static final List<BlockPos> positions = new ArrayList<>();
    private static final List<CommandBlockEntity> identities = new ArrayList<>();
    private static ServerLevel ownedLevel;
    // Rules requested by the client for a world it is about to create; applied once to that world only.
    private static volatile String setupLevel;
    private static volatile boolean[] setupRules;

    public static void requestNewWorldSetup(String levelName, boolean alwaysDay, boolean clearWeather, boolean noMobSpawning, boolean quietCommandBlocks) {
        setupRules = new boolean[] { alwaysDay, clearWeather, noMobSpawning, quietCommandBlocks }; setupLevel = levelName;
    }
    private static void applyNewWorldSetup(MinecraftServer server) {
        boolean[] rules = setupRules; String level = setupLevel;
        if (rules == null || level == null || server.getPlayerCount() < 1) return;
        setupRules = null; setupLevel = null;
        if (!level.equals(server.getWorldData().getLevelName())) return;
        List<String> commands = new ArrayList<>();
        if (rules[0]) { commands.add("gamerule advance_time false"); commands.add("time set noon"); }
        if (rules[1]) { commands.add("gamerule advance_weather false"); commands.add("weather clear"); }
        if (rules[2]) { commands.add("gamerule spawn_mobs false"); commands.add("gamerule spawn_monsters false"); }
        if (rules[3]) commands.add("gamerule command_block_output false");
        CommandSourceStack source = server.createCommandSourceStack().withPermission(LevelBasedPermissionSet.GAMEMASTER).withSuppressedOutput();
        List<String> failed = new ArrayList<>();
        for (String command : commands) {
            try {
                var parsed = server.getCommands().getDispatcher().parse(command, source);
                if (Commands.getParseException(parsed) != null || parsed.getContext().getNodes().isEmpty()) { failed.add(command); continue; }
                server.getCommands().performPrefixedCommand(source, command);
            } catch (Exception e) { failed.add(command); }
        }
        message = failed.isEmpty() ? "New world prepared (" + commands.size() + " setup commands)." : "New world prepared; could not apply: " + String.join(", ", failed);
    }

    public static void tick(MinecraftServer server) {
        if (!server.isSingleplayer()) return;
        if (current != server) {
            current = server; directory = server.getServerDirectory().resolve(".universal-editor");
            session = UUID.randomUUID().toString(); lastRequest = ""; message = "Ready";
            positions.clear(); identities.clear(); ownedLevel = null; nextPoll = 0; armed = false; activeTimeline = ""; catalogWritten = false;
        }
        long now = System.currentTimeMillis();
        if (now < nextPoll) return;
        nextPoll = now + 200;
        try {
            Files.createDirectories(directory);
            if (!catalogWritten) { writeCatalog(server); catalogWritten = true; }
            applyNewWorldSetup(server);
            if (!positions.isEmpty() && identities.stream().anyMatch(CommandBlockEntity::isAutomatic) && !hostAlive(now)) {
                armed = false;
                for (int i = 0; i < positions.size(); i++) if (ownedLevel.getBlockEntity(positions.get(i)) == identities.get(i)) {
                    identities.get(i).setAutomatic(false); identities.get(i).getCommandBlock().setCommand(""); identities.get(i).setChanged();
                }
                message = "Stopped: editor heartbeat expired. Previous command effects are not undone.";
            }
            Path request = directory.resolve("request.json");
            if (Files.exists(request)) {
                if (Files.size(request) > 1024 * 1024) throw new IllegalArgumentException("Request exceeds 1 MB");
                JsonObject data = JsonParser.parseString(Files.readString(request)).getAsJsonObject();
                String id = data.get("requestId").getAsString();
                if (!id.equals(lastRequest) && session.equals(data.get("session").getAsString())) {
                    lastRequest = id; accepted = false; commandResult = null;
                    try { apply(server, data); accepted = true; }
                    catch (Exception e) { message = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage(); }
                }
            }
            writeState(server, true); ioError = "";
        } catch (Exception e) {
            // IPC failure does not interrupt the game loop. The host detects stale heartbeats.
            ioError = "Bridge I/O: " + e.getMessage();
        }
    }
    private static ServerPlayer player(MinecraftServer server, JsonObject data) {
        UUID id = UUID.fromString(data.get("playerId").getAsString());
        ServerPlayer player = server.getPlayerList().getPlayer(id);
        if (player == null) throw new IllegalArgumentException("Selected player is no longer in this world");
        if (server.getPlayerCount() != 1) throw new IllegalArgumentException("This prototype requires a private singleplayer session (one player)");
        return player;
    }
    private static CommandSourceStack source(ServerPlayer p, Feedback feedback) {
        return p.createCommandSourceStack().withSource(feedback).withPermission(LevelBasedPermissionSet.GAMEMASTER)
            .withCallback((successful, result) -> { feedback.completed = true; feedback.successful = successful; commandResult = result; });
    }
    private static void validate(MinecraftServer server, CommandSourceStack source, String command) throws Exception {
        if (command.isBlank() || command.length() > 32500 || command.contains("\n") || command.contains("\r"))
            throw new IllegalArgumentException("Commands must be one nonempty line, at most 32500 characters");
        var parsed = server.getCommands().getDispatcher().parse(command, source);
        CommandSyntaxException error = Commands.getParseException(parsed);
        if (error != null) throw error;
        if (parsed.getContext().getNodes().isEmpty()) throw new IllegalArgumentException("Unknown command");
    }
    private static void verifyOwnership() {
        for (int i = 0; i < positions.size(); i++)
            if (ownedLevel.getBlockEntity(positions.get(i)) != identities.get(i))
                throw new IllegalArgumentException("An installed block was replaced in game. Stop and reconnect before reallocating; no foreign block was changed.");
    }
    private static void apply(MinecraftServer server, JsonObject data) throws Exception {
        String operation = data.get("operation").getAsString();
        if (operation.equals("stop")) {
            armed = false;
            // Stop surviving owned objects even when another block was replaced by the player.
            int count = 0;
            for (int i = 0; i < positions.size(); i++) if (ownedLevel.getBlockEntity(positions.get(i)) == identities.get(i)) {
                var entity = identities.get(i); entity.setAutomatic(false);
                entity.getCommandBlock().setCommand(""); entity.setChanged(); count++;
            }
            message = "Stopped " + count + " owned blocks. Previous command effects are not undone."; return;
        }
        ServerPlayer p = player(server, data);
        Feedback feedback = new Feedback(); CommandSourceStack source = source(p, feedback);
        if (operation.equals("execute")) {
            String command = data.get("command").getAsString(); validate(server, source, command);
            server.getCommands().performPrefixedCommand(source, command);
            if (feedback.failed || (feedback.completed && !feedback.successful)) throw new IllegalArgumentException("Command returned failure. " + feedback.text);
            message = "Executed once. " + feedback.text; return;
        }
        if (operation.equals("item")) {
            String argument = data.get("item").getAsString(); int count = data.get("count").getAsInt();
            if (count < 1 || count > 64 || argument.length() > 32000 || argument.contains("\n")) throw new IllegalArgumentException("Invalid item argument or count");
            String command = "item replace entity " + p.getUUID() + " weapon.mainhand with " + argument + " " + count;
            validate(server, source, command);
            server.getCommands().performPrefixedCommand(source, command);
            if (feedback.failed) throw new IllegalArgumentException(feedback.text.toString());
            message = "Main-hand item updated. " + feedback.text; return;
        }
        if (!operation.equals("install")) throw new IllegalArgumentException("Unknown operation");
        JsonArray nodes = data.getAsJsonArray("nodes");
        String timeline = data.has("timelineObjective") ? data.get("timelineObjective").getAsString() : "";
        if (!timeline.isEmpty() && !timeline.matches("ue_[a-z0-9]{8}")) throw new IllegalArgumentException("Invalid timeline identity");
        if (data.get("running").getAsBoolean() && !hostAlive(System.currentTimeMillis()))
            throw new IllegalArgumentException("Editor heartbeat is required before starting a repeating chain");
        if (nodes.isEmpty() || nodes.size() > 128) throw new IllegalArgumentException("Use 1–128 blocks");
        ServerLevel level = (ServerLevel)p.level();
        BlockPos anchor = new BlockPos(data.get("x").getAsInt(), data.get("y").getAsInt(), data.get("z").getAsInt());
        if (anchor.distSqr(p.blockPosition()) > 64 * 64) throw new IllegalArgumentException("Anchor must be within 64 blocks of the selected player");
        if (!positions.isEmpty()) {
            verifyOwnership();
            if (ownedLevel != level || !positions.getFirst().equals(anchor)) throw new IllegalArgumentException("Anchor is locked for this world session; reopen the world to allocate elsewhere");
        }
        List<BlockPos> proposed = new ArrayList<>();
        for (int i = 0; i < nodes.size(); i++) {
            JsonObject node = nodes.get(i).getAsJsonObject(); BlockPos pos = anchor.offset(0, 0, i);
            if (!level.isInsideBuildHeight(pos) || !level.getWorldBorder().isWithinBounds(pos) || !level.isLoaded(pos))
                throw new IllegalArgumentException("Block " + (i+1) + ": position is outside the loaded/buildable region");
            if (!positions.contains(pos) && !level.getBlockState(pos).isAir())
                throw new IllegalArgumentException("Block " + (i+1) + ": allocate in air; existing blocks are preserved");
            validate(server, source.withPosition(net.minecraft.world.phys.Vec3.atCenterOf(pos)), node.get("command").getAsString());
            proposed.add(pos);
        }
        // All syntax, occupancy and ownership checks precede mutation. Deactivate before replacing.
        if (data.get("running").getAsBoolean() && !timeline.isEmpty()) {
            if (server.getScoreboard().getObjective(timeline) == null)
                server.getCommands().performPrefixedCommand(source, "scoreboard objectives add " + timeline + " dummy");
            if (!timeline.equals(activeTimeline) || (data.has("resetTimeline") && data.get("resetTimeline").getAsBoolean()))
                server.getCommands().performPrefixedCommand(source, "scoreboard players set #clock " + timeline + " 0");
            if (feedback.failed) throw new IllegalArgumentException("Timeline setup failed: " + feedback.text);
            activeTimeline = timeline;
        }
        armed = false;
        for (var entity : identities) { entity.setAutomatic(false); entity.getCommandBlock().setCommand(""); }
        for (var pos : positions) level.setBlock(pos, Blocks.AIR.defaultBlockState(), 3);
        identities.clear(); positions.clear(); ownedLevel = level;
        try {
            for (int i = 0; i < nodes.size(); i++) {
                var node = nodes.get(i).getAsJsonObject(); var pos = proposed.get(i);
                var block = i == 0 ? Blocks.REPEATING_COMMAND_BLOCK : Blocks.CHAIN_COMMAND_BLOCK;
                boolean conditional = i != 0 && node.get("conditional").getAsBoolean();
                level.setBlock(pos, block.defaultBlockState().setValue(CommandBlock.FACING, Direction.SOUTH).setValue(CommandBlock.CONDITIONAL, conditional), 3);
                var entity = (CommandBlockEntity)level.getBlockEntity(pos);
                if (entity == null) throw new IllegalStateException("Command block creation failed");
                positions.add(pos); identities.add(entity);
                entity.getCommandBlock().setTrackOutput(true); entity.getCommandBlock().setCustomName(Component.literal("UniversalEditor:" + session + ":" + i));
                entity.getCommandBlock().setCommand(node.get("command").getAsString());
                entity.setChanged();
            }
            boolean run = data.get("running").getAsBoolean();
            armed = run;
            for (int i = identities.size()-1; i >= 0; i--) {
                var entity = identities.get(i); entity.setAutomatic(run); entity.setChanged();
                var state = level.getBlockState(positions.get(i)); level.sendBlockUpdated(positions.get(i), state, state, 3);
            }
            message = "Applied " + nodes.size() + " blocks; " + (run ? "running every game tick" : "staged, inactive");
        } catch (Exception e) {
            armed = false;
            for (var entity : identities) { entity.setAutomatic(false); entity.getCommandBlock().setCommand(""); entity.setChanged(); }
            throw e;
        }
    }
    private static void writeState(MinecraftServer server, boolean connected) throws Exception {
        JsonObject state = new JsonObject(); state.addProperty("session", session); state.addProperty("version", server.getServerVersion());
        state.addProperty("connected", connected); state.addProperty("heartbeat", System.currentTimeMillis());
        state.addProperty("requestId", lastRequest); state.addProperty("accepted", accepted); state.addProperty("message", message);
        if (commandResult != null) state.addProperty("commandResult", commandResult);
        state.addProperty("ioError", ioError);
        state.addProperty("paused", server.isPaused());
        state.addProperty("armed", armed);
        state.addProperty("world", server.getWorldData().getLevelName());
        JsonArray players = new JsonArray();
        for (var p : server.getPlayerList().getPlayers()) {
            JsonObject player = new JsonObject(); player.addProperty("id", p.getUUID().toString());
            player.addProperty("name", p.getDisplayName().getString()); player.addProperty("x", p.blockPosition().getX());
            player.addProperty("y", p.blockPosition().getY()); player.addProperty("z", p.blockPosition().getZ());
            player.addProperty("dimension", p.level().dimension().identifier().toString());
            var item = p.getMainHandItem(); player.addProperty("itemId", BuiltInRegistries.ITEM.getKey(item.getItem()).toString()); player.addProperty("count", item.getCount());
            var encoded = ItemStack.CODEC.encodeStart(server.registryAccess().createSerializationContext(NbtOps.INSTANCE), item);
            player.addProperty("itemSnbt", encoded.result().map(Object::toString).orElse("{}")); players.add(player);
            var itemJson = ItemStack.CODEC.encodeStart(server.registryAccess().createSerializationContext(JsonOps.INSTANCE), item);
            itemJson.result().ifPresent(value -> player.add("itemJson", value));
        }
        state.add("players", players); JsonArray blocks = new JsonArray();
        for (int i = 0; i < positions.size(); i++) {
            var pos = positions.get(i); var e = ownedLevel.getBlockEntity(pos);
            JsonObject block = new JsonObject(); block.addProperty("x", pos.getX()); block.addProperty("y", pos.getY()); block.addProperty("z", pos.getZ());
            boolean owned = e == identities.get(i); block.addProperty("owned", owned);
            if (owned) { var cb = identities.get(i); block.addProperty("command", cb.getCommandBlock().getCommand());
                block.addProperty("conditional", cb.getBlockState().getValue(CommandBlock.CONDITIONAL));
                block.addProperty("automatic", cb.isAutomatic()); block.addProperty("success", cb.getCommandBlock().getSuccessCount());
                var output = cb.getCommandBlock().getLastOutput(); block.addProperty("output", output == null ? "" : output.getString()); }
            blocks.add(block);
        }
        state.add("blocks", blocks); atomic(directory.resolve("state.json"), JSON.toJson(state));
    }
    public static void close(MinecraftServer server) {
        if (server != current) return;
        armed = false;
        // Persist installed blocks in a stopped state, never silently resume a saved repeating scenario.
        for (int i = 0; i < positions.size(); i++) if (ownedLevel.getBlockEntity(positions.get(i)) == identities.get(i)) {
            identities.get(i).setAutomatic(false); identities.get(i).getCommandBlock().setCommand(""); identities.get(i).setChanged();
        }
        try { writeState(server, false); } catch (Exception ignored) { }
        current = null;
    }
    public static boolean mayExecute(BaseCommandBlock command) {
        for (var entity : identities) if (entity.getCommandBlock() == command) return armed;
        // Persisted editor blocks remain inert after a crash or world reload until explicitly rebound.
        return !command.getName().getString().startsWith("UniversalEditor:");
    }
    private static void atomic(Path path, String value) throws Exception {
        Path temporary = path.resolveSibling(path.getFileName() + ".tmp");
        Files.writeString(temporary, value, StandardCharsets.UTF_8);
        // A Windows reader/antivirus may briefly deny the rename. Never publish partial JSON.
        for (int attempt = 0; ; attempt++) {
            try { Files.move(temporary, path, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING); break; }
            catch (java.nio.file.FileSystemException e) { if (attempt >= 2) throw e; Thread.sleep(2); }
        }
    }
    private static boolean hostAlive(long now) {
        try {
            Path path = directory.resolve("editor-heartbeat.json");
            if (!Files.isRegularFile(path) || Files.size(path) > 4096) return false;
            JsonObject lease = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
            long age = now - lease.get("heartbeat").getAsLong();
            return session.equals(lease.get("session").getAsString()) && age >= -1000 && age < 4000;
        } catch (Exception e) { return false; }
    }
    private static void writeCatalog(MinecraftServer server) throws Exception {
        JsonObject catalog = new JsonObject(); catalog.addProperty("version", server.getServerVersion()); catalog.addProperty("session", session);
        addRegistry(catalog, "items", BuiltInRegistries.ITEM); addRegistry(catalog, "blocks", BuiltInRegistries.BLOCK);
        addRegistry(catalog, "entities", BuiltInRegistries.ENTITY_TYPE); addRegistry(catalog, "effects", BuiltInRegistries.MOB_EFFECT);
        addRegistry(catalog, "attributes", BuiltInRegistries.ATTRIBUTE); addRegistry(catalog, "sounds", BuiltInRegistries.SOUND_EVENT);
        addRegistry(catalog, "enchantments", server.registryAccess().lookupOrThrow(Registries.ENCHANTMENT));
        JsonArray particles = new JsonArray();
        for (var id : BuiltInRegistries.PARTICLE_TYPE.keySet())
            if (BuiltInRegistries.PARTICLE_TYPE.getValue(id) instanceof SimpleParticleType) particles.add(id.toString());
        catalog.add("particles", particles); atomic(directory.resolve("catalog.json"), JSON.toJson(catalog));
    }
    private static void addRegistry(JsonObject catalog, String name, Registry<?> registry) {
        JsonArray values = new JsonArray(); registry.keySet().stream().map(Object::toString).sorted().forEach(values::add); catalog.add(name, values);
    }
    private static final class Feedback implements CommandSource {
        final StringBuilder text = new StringBuilder(); boolean failed, completed, successful;
        public void sendSystemMessage(Component value) { text.append(value.getString()).append(' '); }
        public boolean acceptsSuccess() { return true; }
        public boolean acceptsFailure() { failed = true; return true; }
        public boolean shouldInformAdmins() { return false; }
    }
}
