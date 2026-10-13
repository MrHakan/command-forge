using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UniversalEditor;

internal sealed class ScenarioNode
{
    public string Name { get; set; } = "Step";
    public string Command { get; set; } = "say Hello";
    public bool Conditional { get; set; }
    public string Explanation { get; set; } = "Runs after the previous block in the same game tick.";
    public override string ToString() => Name;
}
internal sealed record ScenarioImport(List<ScenarioNode> Nodes, List<string> InstallerCommands, string Notice);
internal static class Scenario
{
    public static List<ScenarioNode> Demo() =>
    [
        new() { Name = "01 · Repeating / heartbeat", Command = "title @a actionbar {text:\"Live editor connected\",color:\"aqua\"}", Explanation = "Updates the action bar every game tick while this scenario is running. At 20 TPS that is 20 executions per second." },
        new() { Name = "02 · Chain / particle", Command = "execute at @a run particle minecraft:end_rod ~ ~1 ~ 0.2 0.2 0.2 0.01 1", Explanation = "Creates one particle at each player after the repeating block. Stop clears the installed commands; emitted particles fade naturally." }
    ];
    // Read string literals using SNBT escaping, rather than executing the nested installer.
    public static ScenarioImport Import(string source)
    {
        if (source.Length > 1024 * 1024) throw new InvalidDataException("Installer exceeds 1 MB.");
        var commands = ExtractCommands(source);
        var nodes = new List<ScenarioNode>(); var setup = new List<string>();
        foreach (var command in commands)
        {
            var match = Regex.Match(command, @"\Asetblock\s+\S+\s+\S+\s+\S+\s+(?:minecraft:)?(?<kind>repeating|chain|impulse)?_?command_block(?:\[[^\]]*\])?\s*\{", RegexOptions.CultureInvariant);
            if (!match.Success) { setup.Add(command); continue; }
            var nested = ExtractCommands(command[(match.Index + match.Length - 1)..]);
            if (nested.Count != 1) throw new InvalidDataException("A placed command block could not be decoded unambiguously.");
            nodes.Add(new ScenarioNode { Name = $"{nodes.Count+1:00} · {match.Groups["kind"].Value} / imported", Command = nested[0],
                Conditional = command.Contains("conditional=true", StringComparison.Ordinal),
                Explanation = "Imported command. Original installer coordinates, redstone and setup are preserved in the review text. Rebuilding this as a local chain changes the command block's execution position." });
        }
        if (nodes.Count == 0) throw new InvalidDataException("No quoted setblock command-block entries were found. This importer supports the supplied minecart installer format.");
        return new(nodes, setup, $"Decoded {nodes.Count} placed blocks and {setup.Count} other installer commands. Review setup, legacy item fields, timer logic and execution positions before applying. This is a linear editing draft, not an equivalent automatic conversion.");
    }
    internal static List<string> ExtractCommands(string text)
    {
        var result = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            // Skip other strings so a literal containing the word Command cannot become a field.
            if (text[i] is '\'' or '"') { ReadQuoted(text, ref i); continue; }
            if (!text.AsSpan(i).StartsWith("Command", StringComparison.Ordinal)) continue;
            if (i > 0 && (char.IsLetterOrDigit(text[i-1]) || text[i-1] == '_')) continue;
            int cursor = i + 7;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor >= text.Length || text[cursor++] != ':') continue;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor >= text.Length || text[cursor] is not ('\'' or '"')) throw new InvalidDataException("Command fields must contain a quoted string.");
            result.Add(ReadQuoted(text, ref cursor)); i = cursor;
        }
        return result;
    }
    private static string ReadQuoted(string text, ref int cursor)
    {
        char quote = text[cursor]; var value = new StringBuilder();
        while (++cursor < text.Length)
        {
            char c = text[cursor]; if (c == quote) return value.ToString();
            if (c == '\\')
            {
                if (++cursor >= text.Length) break;
                c = text[cursor]; if (c != quote && c != '\\') throw new InvalidDataException("Invalid SNBT string escape.");
            }
            value.Append(c);
        }
        throw new InvalidDataException("Unterminated quoted command.");
    }
    public static string Export(List<ScenarioNode> nodes) => JsonSerializer.Serialize(new { format = "universal-editor-scenario-1", target = "26.2", nodes }, new JsonSerializerOptions { WriteIndented = true });
}

// Rewrites particle arguments from the pre-1.20.5 positional syntax to the component syntax used by 26.2.
// Only exact, well-known patterns are changed; anything else is left for the game parser to report.
internal static class LegacySyntax
{
    private const string Id = @"(?:[a-z0-9_.-]+:)?[a-z0-9_./-]+";
    private const string Number = @"-?(?:\d+\.?\d*|\.\d+)";
    private static readonly Regex BlockParticle = new(@"(?<=\bparticle\s+)(?:minecraft:)?(?<type>block|block_marker|falling_dust|dust_pillar|block_crumble)\s+(?<block>" + Id + @")(?:\[(?<props>[^\]]*)\])?(?=\s|$)", RegexOptions.CultureInvariant);
    private static readonly Regex ItemParticle = new(@"(?<=\bparticle\s+)(?:minecraft:)?item\s+(?<item>" + Id + @")(?=\s|$)", RegexOptions.CultureInvariant);
    private static readonly Regex DustTransition = new(@"(?<=\bparticle\s+)(?:minecraft:)?dust_color_transition\s+(?<r>" + Number + @")\s+(?<g>" + Number + @")\s+(?<b>" + Number + @")\s+(?<s>" + Number + @")\s+(?<r2>" + Number + @")\s+(?<g2>" + Number + @")\s+(?<b2>" + Number + @")(?=\s|$)", RegexOptions.CultureInvariant);
    private static readonly Regex Dust = new(@"(?<=\bparticle\s+)(?:minecraft:)?dust\s+(?<r>" + Number + @")\s+(?<g>" + Number + @")\s+(?<b>" + Number + @")\s+(?<s>" + Number + @")(?=\s|$)", RegexOptions.CultureInvariant);
    private static string Full(string id) => id.Contains(':') ? id : "minecraft:" + id;
    public static string Upgrade(string command)
    {
        command = BlockParticle.Replace(command, m =>
        {
            string block = Full(m.Groups["block"].Value);
            if (!m.Groups["props"].Success || m.Groups["props"].Value.Trim().Length == 0) return $"minecraft:{m.Groups["type"].Value}{{block_state:\"{block}\"}}";
            var properties = new List<string>();
            foreach (var pair in m.Groups["props"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !Regex.IsMatch(parts[0], @"\A[a-z0-9_]+\z") || !Regex.IsMatch(parts[1], @"\A[a-z0-9_]+\z")) return m.Value;
                properties.Add($"{parts[0]}:\"{parts[1]}\"");
            }
            return $"minecraft:{m.Groups["type"].Value}{{block_state:{{Name:\"{block}\",Properties:{{{string.Join(',', properties)}}}}}}}";
        });
        command = ItemParticle.Replace(command, m => $"minecraft:item{{item:{{id:\"{Full(m.Groups["item"].Value)}\"}}}}");
        command = DustTransition.Replace(command, m => $"minecraft:dust_color_transition{{from_color:[{m.Groups["r"].Value},{m.Groups["g"].Value},{m.Groups["b"].Value}],scale:{m.Groups["s"].Value},to_color:[{m.Groups["r2"].Value},{m.Groups["g2"].Value},{m.Groups["b2"].Value}]}}");
        command = Dust.Replace(command, m => $"minecraft:dust{{color:[{m.Groups["r"].Value},{m.Groups["g"].Value},{m.Groups["b"].Value}],scale:{m.Groups["s"].Value}}}");
        return command;
    }
}
