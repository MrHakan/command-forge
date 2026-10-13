using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UniversalEditor;

internal sealed class GameCatalog
{
    public MinecraftAssets Assets { get; } = new();
    private readonly Dictionary<string, string[]> entries = new()
    {
        ["items"] = ["minecraft:diamond_sword", "minecraft:netherite_sword", "minecraft:bow", "minecraft:stick", "minecraft:apple"],
        ["enchantments"] = ["minecraft:sharpness", "minecraft:unbreaking", "minecraft:mending", "minecraft:power", "minecraft:efficiency"],
        ["attributes"] = ["minecraft:attack_damage", "minecraft:attack_speed", "minecraft:max_health", "minecraft:movement_speed"],
        ["effects"] = ["minecraft:darkness", "minecraft:blindness", "minecraft:speed", "minecraft:glowing", "minecraft:night_vision"],
        ["entities"] = ["minecraft:pig", "minecraft:zombie", "minecraft:armor_stand", "minecraft:lightning_bolt"],
        ["particles"] = ["minecraft:end_rod", "minecraft:cloud", "minecraft:smoke", "minecraft:flame", "minecraft:happy_villager"],
        ["blocks"] = ["minecraft:stone", "minecraft:glass", "minecraft:crying_obsidian", "minecraft:air"],
        ["sounds"] = ["minecraft:block.note_block.chime", "minecraft:entity.enderman.teleport", "minecraft:ambient.cave"]
    };
    public bool Exact { get; private set; }
    public void LoadVisualChoices()
    {
        if(Exact||!Assets.Available)return;
        foreach(var (category,folder,extension) in new[]{("items","items",".json"),("blocks","blockstates",".json"),("effects","textures/mob_effect",".png")})
        {var values=Assets.ResourceIds(folder,extension);if(values.Length>0)entries[category]=values.Union(entries[category]).Order().ToArray();}
    }
    public string[] Get(string category) => entries.GetValueOrDefault(category, []);
    public void Load(string directory)
    {
        var path = Path.Combine(directory, ".universal-editor", "catalog.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 2*1024*1024) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var data = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(stream);
        if (data?["version"]?.GetValue<string>() != "26.2") return;
        foreach (var key in entries.Keys.ToArray()) if (data[key] is JsonArray values) entries[key] = values.Select(v => v!.GetValue<string>()).Order().ToArray();
        Exact = true;
    }
}
internal sealed class EnchantmentChoice { public string Id { get; set; } = "minecraft:sharpness"; public int Level { get; set; } = 5; }
internal sealed class AttributeChoice
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "minecraft:attack_damage";
    public decimal Amount { get; set; } = 5;
    public string Operation { get; set; } = "add_value";
    public string Slot { get; set; } = "mainhand";
}
internal sealed class ItemRecipe
{
    public string Id { get; set; } = "minecraft:diamond_sword";
    public int Count { get; set; } = 1;
    public string Name { get; set; } = "Editor blade";
    public string Color { get; set; } = "aqua";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string Lore { get; set; } = "Designed in Command Forge";
    public bool Unbreakable { get; set; }
    public int? Damage { get; set; }
    public bool? Glint { get; set; }
    public List<EnchantmentChoice> Enchantments { get; set; } = [new()];
    public List<AttributeChoice> Attributes { get; set; } = [];
    public JsonObject Preserved { get; set; } = new();
    public Dictionary<string,string> OriginalComponentSnbt { get; set; } = [];
    public JsonObject Components()
    {
        var result = (JsonObject)Preserved.DeepClone();
        JsonObject Text(string value) => new() { ["text"] = value, ["color"] = Color, ["bold"] = Bold, ["italic"] = Italic };
        if (Name.Length > 0) result["minecraft:custom_name"] = Text(Name);
        if (Lore.Length > 0) result["minecraft:lore"] = new JsonArray(Lore.Replace("\r", "").Split('\n').Select(s => (JsonNode)Text(s)).ToArray());
        if (Unbreakable) result["minecraft:unbreakable"] = new JsonObject();
        if (Damage is not null) result["minecraft:damage"] = Damage.Value;
        if (Glint is not null) result["minecraft:enchantment_glint_override"] = Glint.Value;
        if (Enchantments.Count > 0)
        {
            var levels = new JsonObject();
            foreach (var e in Enchantments)
            { Authoring.Id(e.Id); if (e.Level is <1 or >255) throw new ArgumentException("Enchantment levels must be 1–255."); if (levels.ContainsKey(e.Id)) throw new ArgumentException("An enchantment was added twice."); levels[e.Id] = e.Level; }
            result["minecraft:enchantments"] = levels;
        }
        if (Attributes.Count > 0)
        {
            var modifiers = new JsonArray(); int i = 0;
            foreach (var a in Attributes)
            {
                Authoring.Id(a.Type);
                if (!new[] { "add_value", "add_multiplied_base", "add_multiplied_total" }.Contains(a.Operation)) throw new ArgumentException("Unknown attribute operation.");
                if (!new[] { "any", "mainhand", "offhand", "hand", "head", "chest", "legs", "feet", "armor", "body" }.Contains(a.Slot)) throw new ArgumentException("Unknown equipment slot.");
                string identity = a.Id.Length==0 ? "universal_editor:modifier_" + i : a.Id; IdCheck(identity); i++;
                modifiers.Add(new JsonObject { ["id"] = identity, ["type"] = a.Type, ["amount"] = a.Amount, ["operation"] = a.Operation, ["slot"] = a.Slot });
            }
            result["minecraft:attribute_modifiers"] = modifiers;
        }
        return result;
    }
    private static void IdCheck(string id) => Authoring.Id(id);
    public string Argument()
    { Authoring.Id(Id); if (Count is <1 or >64) throw new ArgumentException("Item count must be 1–64."); var values = Components(); return Id + (values.Count == 0 ? "" : "[" + string.Join(',', values.Select(p => p.Key + "=" + (OriginalComponentSnbt.TryGetValue(p.Key,out var raw) && JsonNode.DeepEquals(p.Value,Preserved[p.Key]) ? raw : p.Value!.ToJsonString()))) + "]"); }
    public static ItemRecipe Read(JsonObject item, string? snbt = null)
    {
        var recipe = new ItemRecipe { Id = item["id"]!.GetValue<string>(), Count = item["count"]?.GetValue<int>() ?? 1,
            Name = "", Lore = "", Enchantments = [], Attributes = [], Preserved = (JsonObject)(item["components"]?.DeepClone() ?? new JsonObject()) };
        var data = recipe.Preserved;
        if(snbt is not null && SnbtSlices.Compound(snbt).TryGetValue("components",out var componentSnbt)) recipe.OriginalComponentSnbt=SnbtSlices.Compound(componentSnbt);
        if (data["minecraft:custom_name"] is JsonObject text && text.Count <= 5 && text["text"] is JsonValue && text.All(p => new[] { "text", "color", "bold", "italic" }.Contains(p.Key)))
        {
            recipe.Name = text["text"]!.GetValue<string>(); recipe.Color = text["color"]?.GetValue<string>() ?? "white";
            recipe.Bold = text["bold"]?.GetValue<bool>() ?? false; recipe.Italic = text["italic"]?.GetValue<bool>() ?? false; data.Remove("minecraft:custom_name");
        }
        if (data["minecraft:enchantments"] is JsonObject levels && levels.All(p => p.Value is JsonValue v && v.TryGetValue<int>(out var n) && n is >=1 and <=255))
        { recipe.Enchantments = levels.Select(p => new EnchantmentChoice { Id = p.Key, Level = p.Value!.GetValue<int>() }).ToList(); data.Remove("minecraft:enchantments"); }
        if (data["minecraft:unbreakable"] is JsonObject unbreakable && unbreakable.Count == 0) { recipe.Unbreakable = true; data.Remove("minecraft:unbreakable"); }
        if (data["minecraft:damage"] is JsonValue damage && damage.TryGetValue<int>(out var amount)) { recipe.Damage = amount; data.Remove("minecraft:damage"); }
        if (data["minecraft:enchantment_glint_override"] is JsonValue glint && glint.TryGetValue<bool>(out var enabled)) { recipe.Glint = enabled; data.Remove("minecraft:enchantment_glint_override"); }
        if(data["minecraft:lore"] is JsonArray lore && lore.All(v=>v is JsonObject line && line["text"] is JsonValue && line.All(p=>new[]{"text","color","bold","italic"}.Contains(p.Key)) && (line["color"]?.GetValue<string>()??recipe.Color)==recipe.Color && (line["bold"]?.GetValue<bool>()??false)==recipe.Bold && (line["italic"]?.GetValue<bool>()??false)==recipe.Italic))
        {recipe.Lore=string.Join('\n',lore.Select(v=>v!["text"]!.GetValue<string>()));data.Remove("minecraft:lore");}
        if(data["minecraft:attribute_modifiers"] is JsonArray modifiers && modifiers.All(v=>v is JsonObject m && m.All(p=>new[]{"id","type","amount","operation","slot"}.Contains(p.Key)) && m["amount"] is JsonValue amountNode && amountNode.TryGetValue<decimal>(out _)))
        {recipe.Attributes=modifiers.Select(v=>new AttributeChoice {Id=v!["id"]!.GetValue<string>(),Type=v["type"]!.GetValue<string>(),Amount=v["amount"]!.GetValue<decimal>(),Operation=v["operation"]!.GetValue<string>(),Slot=v["slot"]?.GetValue<string>()??"any"}).ToList();data.Remove("minecraft:attribute_modifiers");}
        // Complex text, lore, attributes and unknown components remain byte-for-value represented in the JSON tree until explicitly overridden.
        return recipe;
    }
}
internal enum ActionKind { Message, Particle, Effect, Summon, GiveItem, Teleport, Sound, SetBlock, Time, Weather, Variable, Tag }
internal enum TimingKind { OnStart, OnceAfterDelay, EveryTick, EveryInterval }
internal sealed class ActionSpec
{
    public ActionKind Kind { get; set; } = ActionKind.Message;
    public TimingKind Timing { get; set; } = TimingKind.OnStart;
    public decimal DelaySeconds { get; set; }
    public decimal IntervalSeconds { get; set; } = 1;
    public decimal? EndSeconds { get; set; }
    public string ConditionVariable { get; set; } = "";
    public string Comparison { get; set; } = "Equals";
    public int ConditionValue { get; set; }
    public string VariableName { get; set; } = "phase";
    public int VariableValue { get; set; }
    public string VariableOperation { get; set; } = "Set";
    public string Target { get; set; } = "All players";
    public string RequiredTag { get; set; } = "";
    public bool UnlessTag { get; set; }
    public string Text { get; set; } = "Hello from your scenario";
    public string Color { get; set; } = "aqua";
    public string Presentation { get; set; } = "Action bar";
    public string Resource { get; set; } = "minecraft:end_rod";
    public decimal X { get; set; }
    public decimal Y { get; set; } = 1;
    public decimal Z { get; set; }
    public int Count { get; set; } = 1;
    public int DurationSeconds { get; set; } = 5;
    public int EffectLevel { get; set; } = 1;
    public bool HideParticles { get; set; }
    public string Option { get; set; } = "day";
    public string MobName { get; set; } = "";
    public bool NoAi { get; set; }
    public bool Invulnerable { get; set; }
    public ItemRecipe Item { get; set; } = new();
    public override string ToString() => $"{Kind} · {When()}";
    public string When() => Timing switch { TimingKind.OnStart => "On start", TimingKind.OnceAfterDelay => $"After {DelaySeconds:0.##} seconds", TimingKind.EveryTick => $"Every tick from {DelaySeconds:0.##}s", _ => $"Every {IntervalSeconds:0.##}s from {DelaySeconds:0.##}s" };
    public string Explain() => $"{Kind} for {Target.ToLowerInvariant()}. {When()}. Timing uses game ticks; pausing the world pauses the sequence.";
}
internal sealed class DesignerProject
{
    public string Format { get; set; } = "universal-editor-designer-1";
    public string Target { get; set; } = "26.2";
    public string Objective { get; set; } = "ue_" + Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Countdown reveal";
    public List<ActionSpec> Actions { get; set; } = [];
}
internal static class Authoring
{
    public static string[] Colors => ["aqua", "white", "gold", "red", "green", "blue", "yellow", "light_purple", "dark_red", "dark_green", "dark_blue", "dark_aqua", "dark_purple", "gray", "dark_gray", "black"];
    public static void Id(string value) { if (!Regex.IsMatch(value, @"\A[a-z0-9_.-]+:[a-z0-9_./-]+\z")) throw new ArgumentException("Choose a valid Minecraft resource."); }
    public static int Ticks(decimal seconds) { if (seconds < 0 || seconds > 50000) throw new ArgumentException("Time must be 0–50000 seconds."); return (int)Math.Round(seconds * 20, MidpointRounding.AwayFromZero); }
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Relative(decimal value) => value == 0 ? "~" : "~" + Number(value);
    public static string Variable(string name)
    { if (!Regex.IsMatch(name, @"\A[a-zA-Z][a-zA-Z0-9_]{0,15}\z")) throw new ArgumentException("Variable names use 1–16 letters, digits or underscores and start with a letter."); return "#v_" + name; }
    public static string Command(ActionSpec action, string? objective = null)
    {
        string position = $"{Relative(action.X)} {Relative(action.Y)} {Relative(action.Z)}";
        string Text() => new JsonObject { ["text"] = action.Text, ["color"] = action.Color }.ToJsonString();
        switch (action.Kind)
        {
            case ActionKind.Variable:
                if (objective is null) throw new ArgumentException("Variables require a scenario timeline.");
                string operation = action.VariableOperation switch { "Set" => "set", "Add" => "add", "Subtract" => "remove", _ => throw new ArgumentException("Unknown variable operation.") };
                if (operation != "set" && action.VariableValue < 0) throw new ArgumentException("Add/subtract amounts must be nonnegative.");
                return $"scoreboard players {operation} {Variable(action.VariableName)} {objective} {action.VariableValue}";
            case ActionKind.Tag:
                if (!Regex.IsMatch(action.Text, @"\A[a-zA-Z0-9_.+-]+\z") || !new[] { "add", "remove" }.Contains(action.Option)) throw new ArgumentException("Choose a valid tag and operation.");
                return $"tag @s {action.Option} {action.Text}";
            case ActionKind.Message: return action.Presentation switch { "Chat" => "tellraw @s " + Text(), "Title" => "title @s title " + Text(), _ => "title @s actionbar " + Text() };
            case ActionKind.Particle: Id(action.Resource); if (action.Count is <1 or >1000) throw new ArgumentException("Particle count must be 1–1000."); return $"particle {action.Resource} {position} 0.2 0.2 0.2 0.01 {action.Count}";
            case ActionKind.Effect: Id(action.Resource); if (action.EffectLevel is <1 or >256 || action.DurationSeconds is <1 or >1000000) throw new ArgumentException("Invalid effect duration or level."); return $"effect give @s {action.Resource} {action.DurationSeconds} {action.EffectLevel-1} {action.HideParticles.ToString().ToLowerInvariant()}";
            case ActionKind.Summon:
                Id(action.Resource); var data = new JsonObject(); if (action.MobName.Length > 0) data["CustomName"] = new JsonObject { ["text"] = action.MobName }; if (action.NoAi) data["NoAI"] = true; if (action.Invulnerable) data["Invulnerable"] = true;
                return $"summon {action.Resource} {position} {data.ToJsonString()}";
            case ActionKind.GiveItem: return $"give @s {action.Item.Argument()} {action.Item.Count}";
            case ActionKind.Teleport: return $"tp @s {Number(action.X)} {Number(action.Y)} {Number(action.Z)}";
            case ActionKind.Sound: Id(action.Resource); return $"playsound {action.Resource} master @s {position} 1 1";
            case ActionKind.SetBlock: Id(action.Resource); return $"setblock {position} {action.Resource}";
            case ActionKind.Time: if (!new[] { "day", "noon", "night", "midnight" }.Contains(action.Option)) throw new ArgumentException("Choose a time preset."); return "time set " + action.Option;
            case ActionKind.Weather: if (!new[] { "clear", "rain", "thunder" }.Contains(action.Option)) throw new ArgumentException("Choose a weather preset."); return "weather " + action.Option;
            default: throw new ArgumentException("Unknown action.");
        }
    }
    public static List<ScenarioNode> Compile(DesignerProject project)
    {
        if (project.Target != "26.2" || project.Format != "universal-editor-designer-1") throw new ArgumentException("This designer supports Java 26.2.");
        if (!Regex.IsMatch(project.Objective, @"\Aue_[a-z0-9]{8}\z")) throw new ArgumentException("Invalid timeline identity.");
        if (project.Actions.Count is <1 or >100) throw new ArgumentException("Use 1–100 action cards.");
        string clock = project.Objective;
        var nodes = new List<ScenarioNode> { new() { Name = "Timeline clock", Command = $"scoreboard players add #clock {clock} 1", Explanation = "Advances the project clock by one tick. Run/restart resets it; live edits preserve it." } };
        for (int index = 0; index < project.Actions.Count; index++)
        {
            var a = project.Actions[index]; int start = Ticks(a.DelaySeconds) + 1;
            int? end = a.EndSeconds is null ? null : Ticks(a.EndSeconds.Value) + 1;
            if (end < start) throw new ArgumentException("End time must be at or after the start time.");
            string timing = a.Timing is TimingKind.OnStart ? "1" : a.Timing == TimingKind.OnceAfterDelay ? start.ToString(CultureInfo.InvariantCulture) : start + ".." + end?.ToString(CultureInfo.InvariantCulture);
            string condition = $"if score #clock {clock} matches {timing}";
            if (a.Timing == TimingKind.EveryInterval)
            {
                int period = Ticks(a.IntervalSeconds); if (period <1) throw new ArgumentException("Interval must be at least one game tick (0.05 seconds).");
                nodes.Add(new() { Name = "Interval constant", Command = $"scoreboard players set #period{index} {clock} {period}" });
                nodes.Add(new() { Name = "Interval phase", Command = $"scoreboard players operation #phase{index} {clock} = #clock {clock}" });
                nodes.Add(new() { Name = "Offset interval", Command = $"scoreboard players remove #phase{index} {clock} {start}" });
                nodes.Add(new() { Name = "Interval remainder", Command = $"scoreboard players operation #phase{index} {clock} %= #period{index} {clock}" });
                condition += $" if score #phase{index} {clock} matches 0";
            }
            if (a.ConditionVariable.Length > 0)
            {
                string value = a.ConditionValue.ToString(CultureInfo.InvariantCulture);
                string match = a.Comparison switch { "Equals" or "Not equal" => value, "At least" => value + "..", "At most" => ".." + value, _ => throw new ArgumentException("Unknown variable comparison.") };
                condition += $" {(a.Comparison == "Not equal" ? "unless" : "if")} score {Variable(a.ConditionVariable)} {clock} matches {match}";
            }
            string selector = a.Target switch { "Nearest player" => "@p", "All players" => "@a", _ => throw new ArgumentException("Unknown target.") };
            if (a.RequiredTag.Length > 0)
            { if (!Regex.IsMatch(a.RequiredTag, @"\A[a-zA-Z0-9_.+-]+\z")) throw new ArgumentException("Tag must contain letters, digits, dots, underscores or hyphens."); selector += "[tag=" + (a.UnlessTag ? "!" : "") + a.RequiredTag + "]"; }
            string execution = a.Kind == ActionKind.Variable ? $"execute {condition} run {Command(a,clock)}" : $"execute {condition} as {selector} at @s run {Command(a,clock)}";
            nodes.Add(new() { Name = a.Kind + " · " + a.When(), Command = execution, Explanation = a.Explain() });
        }
        if (nodes.Count >128) throw new ArgumentException($"This design compiles to {nodes.Count} blocks; the current bridge limit is 128. Reduce actions or interval cards.");
        return nodes;
    }
    public static DesignerProject Template(string name) => new() { Name = name, Actions = name switch
    {
        "Fog introduction" => [new() { Text = "Something is approaching…", Presentation = "Title", Color = "gray" }, new() { Kind = ActionKind.Sound, Resource = "minecraft:ambient.cave", Timing = TimingKind.OnceAfterDelay, DelaySeconds = 2 }, new() { Kind = ActionKind.Effect, Resource = "minecraft:darkness", DurationSeconds = 6, Timing = TimingKind.OnceAfterDelay, DelaySeconds = 3 }, new() { Kind = ActionKind.Particle, Resource = "minecraft:smoke", Count = 8, Timing = TimingKind.EveryInterval, DelaySeconds = 3, IntervalSeconds = 1 }, new() { Text = "The fog is here", Timing = TimingKind.OnceAfterDelay, DelaySeconds = 5 }],
        "Particle loop" => [new() { Text = "Particle loop started" }, new() { Kind = ActionKind.Particle, Resource = "minecraft:end_rod", Timing = TimingKind.EveryInterval, IntervalSeconds = 1, Count = 3 }],
        "Mob showcase" => [new() { Text = "Meet your creation", Presentation = "Title" }, new() { Kind = ActionKind.Summon, Resource = "minecraft:pig", MobName = "Showcase", NoAi = true, X = 3, Y = 0, Timing = TimingKind.OnceAfterDelay, DelaySeconds = 2 }],
        _ => [new() { Text = "3", Presentation = "Title" }, new() { Text = "2", Presentation = "Title", Timing = TimingKind.OnceAfterDelay, DelaySeconds = 1 }, new() { Text = "1", Presentation = "Title", Timing = TimingKind.OnceAfterDelay, DelaySeconds = 2 }, new() { Text = "Go!", Presentation = "Title", Color = "green", Timing = TimingKind.OnceAfterDelay, DelaySeconds = 3 }, new() { Kind = ActionKind.Particle, Resource = "minecraft:happy_villager", Count = 10, Timing = TimingKind.OnceAfterDelay, DelaySeconds = 3 }]
    } };
}
