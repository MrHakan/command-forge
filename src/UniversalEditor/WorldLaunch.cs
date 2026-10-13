using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UniversalEditor;

internal enum WorldStartup { OpenOrCreate, CreateNew, OpenExisting, MainMenu }

// Prepares the one-shot world request that the 26.2 bridge reads on the title screen
// (<instance>/minecraft/.universal-editor/launch-world.json). Only the editor-owned instance is written.
internal static class WorldLaunch
{
    public const string RequestFormat = "command-forge-launch-1";
    public static readonly string[] Modes = ["Open my editor world (create it the first time)", "Create a new world every time", "Open a specific world", "Stay on the main menu"];
    public static string RequestPath(string gameDirectory) => Path.Combine(gameDirectory, ".universal-editor", "launch-world.json");

    public static string[] ExistingWorlds(string gameDirectory)
    {
        var saves = Path.Combine(gameDirectory, "saves");
        if (!Directory.Exists(saves)) return [];
        return new DirectoryInfo(saves).EnumerateDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "level.dat")))
            .OrderByDescending(d => d.LastWriteTimeUtc).Select(d => d.Name).Where(ValidFolder).ToArray();
    }

    // Same character set the bridge accepts for level folders.
    public static bool ValidFolder(string folder) =>
        Regex.IsMatch(folder, @"\A[A-Za-z0-9 _().-]{1,64}\z") && !folder.Contains("..") && !folder.EndsWith('.') && !folder.EndsWith(' ');

    public static string FolderFor(string name)
    {
        var folder = Regex.Replace(name.Trim(), @"[^A-Za-z0-9 _().-]", "_");
        folder = Regex.Replace(folder, @"\.{2,}", ".").Trim().TrimEnd('.', ' ');
        if (folder.Length > 48) folder = folder[..48].TrimEnd('.', ' ');
        return ValidFolder(folder) ? folder : "Command Forge World";
    }

    public static string UniqueFolder(string gameDirectory, string name)
    {
        string baseName = FolderFor(name), folder = baseName;
        for (int i = 2; Directory.Exists(Path.Combine(gameDirectory, "saves", folder)); i++) folder = $"{baseName} ({i})";
        return folder;
    }

    // Writes or clears the request. Returns a log line, or null when the game should stay on the main menu.
    // May update settings.EditorWorld, which the caller should persist.
    public static string? Prepare(UserSettings s, string gameDirectory)
    {
        var request = RequestPath(gameDirectory);
        if (File.Exists(request)) File.Delete(request);
        string action, folder;
        switch (s.WorldStartup)
        {
            case WorldStartup.MainMenu: return null;
            case WorldStartup.OpenExisting:
                if (s.OpenWorld is not { } chosen || !ValidFolder(chosen) || !File.Exists(Path.Combine(gameDirectory, "saves", chosen, "level.dat")))
                    return "The chosen world was not found; Minecraft will stay on the main menu.";
                action = "open"; folder = chosen; break;
            case WorldStartup.OpenOrCreate when s.EditorWorld is { } existing && ValidFolder(existing) && File.Exists(Path.Combine(gameDirectory, "saves", existing, "level.dat")):
                action = "open"; folder = existing; break;
            default:
                action = "create"; folder = UniqueFolder(gameDirectory, s.NewWorldName);
                if (s.WorldStartup == WorldStartup.OpenOrCreate) s.EditorWorld = folder;
                break;
        }
        var plan = new JsonObject
        {
            ["format"] = RequestFormat, ["action"] = action, ["folder"] = folder,
            ["name"] = string.IsNullOrWhiteSpace(s.NewWorldName) ? folder : s.NewWorldName.Trim(),
            ["gameMode"] = s.NewWorldGameMode, ["difficulty"] = s.NewWorldDifficulty, ["preset"] = s.NewWorldPreset,
            ["seed"] = s.NewWorldSeed.Trim(), ["structures"] = s.NewWorldStructures, ["keepRunning"] = s.KeepWorldRunning,
            ["rules"] = new JsonObject { ["alwaysDay"] = s.AlwaysDay, ["clearWeather"] = s.ClearWeather, ["noMobSpawning"] = s.NoMobSpawning, ["quietCommandBlocks"] = s.QuietCommandBlocks }
        };
        UserSettings.AtomicWrite(request, plan.ToJsonString());
        if (!File.Exists(request)) throw new IOException("Could not write the world request into the managed instance.");
        return action == "open" ? $"Minecraft will open the world “{folder}”." : $"Minecraft will create the world “{folder}”.";
    }
}
