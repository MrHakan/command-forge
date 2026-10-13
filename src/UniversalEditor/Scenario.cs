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
