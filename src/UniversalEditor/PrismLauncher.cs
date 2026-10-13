using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Security.Cryptography;

namespace UniversalEditor;

internal sealed class PrismLauncher
{
    public string DataDirectory { get; }
    public string Executable { get; }
    public PrismLauncher(string dataDirectory, string executable)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        Executable = Path.GetFullPath(executable);
    }
    public static PrismLauncher Discover() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PrismLauncher", "prismlauncher.exe"));

    public string Prepare(GameVersion version, int minMemory, int maxMemory, string? bridgeJar = null)
    {
        if (!Regex.IsMatch(version.Id, @"\A[A-Za-z0-9][A-Za-z0-9.-]*\z")) throw new ArgumentException("Invalid version ID.");
        if (minMemory < 512 || maxMemory < minMemory || maxMemory > 65536) throw new ArgumentException("RAM must satisfy 512 MB ≤ minimum ≤ maximum ≤ 65536 MB.");
        if (!Directory.Exists(DataDirectory) || !File.Exists(Executable)) throw new FileNotFoundException("Prism Launcher was not found. Set its executable and data directory under Settings.");
        var id = "universal-editor-" + version.Id;
        var path = Path.Combine(DataDirectory, "instances", id);
        var marker = Path.Combine(path, ".universal-editor-managed");
        if (Directory.Exists(path) && (!File.Exists(marker) || File.ReadAllText(marker) != version.Id))
            throw new IOException("The target instance exists but is not owned by Command Forge. Choose another Prism data directory.");
        if ((File.GetAttributes(DataDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Use a physical Prism data directory for this prototype.");
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Managed instance directories cannot be links.");
        Directory.CreateDirectory(path);
        File.WriteAllText(marker, version.Id);
        if (bridgeJar is not null && (version.Id != "26.2" || !File.Exists(bridgeJar)))
            throw new IOException("A built bridge is available only for Java 26.2.");
        // Prism resolves exact dependencies and keeps account tokens in its own flow.
        var components = new List<object> { new { uid = "net.minecraft", version = version.Id, important = true } };
        if (bridgeJar is not null)
        {
            components.Add(new { uid = "net.fabricmc.fabric-loader", version = "0.19.5", important = true });
            var mods = Path.Combine(path, "minecraft", "mods"); Directory.CreateDirectory(mods);
            DeployBridge(bridgeJar, Path.Combine(mods, "universal-editor-bridge-26.2.jar"));
        }
        var pack = new { formatVersion = 1, components };
        File.WriteAllText(Path.Combine(path, "mmc-pack.json"), JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(path, "instance.cfg"), BuildConfig(version.Id, minMemory, maxMemory));
        return id;
    }

    internal static string BuildConfig(string version, int minMemory, int maxMemory) => $"""
        [General]
        ConfigVersion=1.3
        InstanceType=OneSix
        name=Command Forge - {version}
        iconKey=default
        AutomaticJava=true
        OverrideJavaLocation=false
        OverrideMemory=true
        MinMemAlloc={minMemory}
        MaxMemAlloc={maxMemory}
        OverrideWindow=true
        LaunchMaximized=false
        MinecraftWinWidth=960
        MinecraftWinHeight=540
        OverrideCommands=true
        PreLaunchCommand=
        PostExitCommand=
        WrapperCommand=
        OverrideConsole=true
        ShowConsole=false
        ShowConsoleOnError=true
        CloseAfterLaunch=false
        JoinServerOnLaunch=false
        JoinWorldOnLaunch=
        """ + Environment.NewLine;

    internal static void DeployBridge(string source, string target)
    {
        static byte[] ReadArchive(string path) { using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read); using var memory = new MemoryStream(); input.CopyTo(memory); return memory.ToArray(); }
        byte[] bytes;
        try
        {
            bytes = ReadArchive(source);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.GetEntry("fabric.mod.json") is null || zip.GetEntry("editor/bridge/Bridge.class") is null) throw new InvalidDataException("Bridge archive is incomplete.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        { throw new IOException("The bridge JAR cannot be read. Rebuild with bridge/build.ps1 before launching. " + e.Message,e); }
        if (File.Exists(target))
        {
            try { if (SHA256.HashData(ReadArchive(target)).SequenceEqual(SHA256.HashData(bytes))) return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Replace only this owned mod through a verified new archive below. */ }
        }
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write)) { output.Write(bytes); }
            using (var check = ZipFile.OpenRead(temporary)) { if (check.GetEntry("fabric.mod.json") is null) throw new InvalidDataException("Bridge deployment failed validation."); }
            File.Move(temporary, target, true);
            using var deployed = ZipFile.OpenRead(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new IOException("Cannot replace/read the editor bridge. Close the editor-managed Minecraft instance and retry. " + e.Message,e); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Launch(string instanceId)
    {
        // Prism caches its instance list. A fresh private launcher session reads the new profile
        // without restarting or changing the user's running launcher.
        var sessions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniversalEditor", "launcher-sessions");
        Directory.CreateDirectory(sessions);
        var previousCache = Directory.EnumerateDirectories(sessions)
            .Where(p => File.Exists(Path.Combine(p,".universal-editor-launcher")) && File.ReadAllText(Path.Combine(p,".universal-editor-launcher")) == DataDirectory)
            .Select(p => new FileInfo(Path.Combine(p,"metacache"))).Where(f=>f.Exists).OrderByDescending(f=>f.LastWriteTimeUtc).FirstOrDefault();
        var sessionRoot = Path.Combine(sessions, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        // Retain Prism's HTTP validation metadata as well as the shared content caches.
        // A new launcher root should not make every existing non-checksummed library look uncached.
        var metacache = previousCache?.FullName ?? Path.Combine(DataDirectory, "metacache");
        if (File.Exists(metacache)) File.Copy(metacache, Path.Combine(sessionRoot, "metacache"));
        File.WriteAllText(Path.Combine(sessionRoot,".universal-editor-launcher"),DataDirectory);
        foreach (var folder in new[] { "assets", "libraries", "meta", "translations" })
        {
            var source = Path.Combine(DataDirectory, folder);
            Directory.CreateDirectory(source);
            CreateJunction(Path.Combine(sessionRoot, folder), source);
        }
        var sourceAccounts = Path.Combine(DataDirectory, "accounts.json");
        if (File.Exists(sourceAccounts) && !CreateHardLinkW(Path.Combine(sessionRoot, "accounts.json"), sourceAccounts, IntPtr.Zero))
            throw new IOException("Could not share the local Prism account state with this launcher session. Keep Prism data on the same volume as your local application data.");
        static string ConfigPath(string path) => path.Replace('\\', '/');
        File.WriteAllText(Path.Combine(sessionRoot, "prismlauncher.cfg"), $"""
            [General]
            InstanceDir={ConfigPath(Path.Combine(DataDirectory, "instances"))}
            JavaDir={ConfigPath(Path.Combine(DataDirectory, "java"))}
            AutomaticJavaSwitch=true
            AutomaticJavaDownload=true
            UserAskedAboutAutomaticJavaDownload=true
            IgnoreJavaWizard=true
            ApplicationTheme=dark
            IconTheme=multimc
            Language=en_US
            PastebinURL=
            ShowConsole=false
            ShowConsoleOnError=true
            CloseAfterLaunch=false
            QuitAfterGameStop=true
            """);
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = DataDirectory };
        info.ArgumentList.Add("--dir"); info.ArgumentList.Add(sessionRoot);
        info.ArgumentList.Add("--launch"); info.ArgumentList.Add(instanceId);
        using var process = Process.Start(info) ?? throw new IOException("Prism could not start.");
    }

    private static void CreateJunction(string link, string target)
    {
        static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path " + Quote(link) + " -Target " + Quote(target) + " | Out-Null");
        using var process = Process.Start(info) ?? throw new IOException("Could not create a launcher cache link.");
        var errors = process.StandardError.ReadToEnd(); process.StandardOutput.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not link the Prism download cache: " + errors);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string link, string existing, IntPtr security);
}
