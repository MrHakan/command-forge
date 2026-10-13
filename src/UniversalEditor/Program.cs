namespace UniversalEditor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--probe-window"))
        {
            Application.Run(new Form { Text = "Command Forge native probe", Width = 640, Height = 400,
                BackColor = Color.FromArgb(35, 95, 110) });
            return 0;
        }
        bool interactive = !args.Any(a => a is "--self-test" or "--self-test-headless" or "--asset-tests" or "--live-smoke" or "--import-sample" or "--discord-check");
        string root;
        try { root = FindRoot(); }
        catch (DirectoryNotFoundException e)
        {
            if (interactive) MessageBox.Show(e.Message, "Command Forge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else Console.Error.WriteLine(e.Message);
            return 2;
        }
        if (args.Contains("--self-test")) return SelfTests.Run(root);
        if (args.Contains("--self-test-headless")) return SelfTests.Run(root, headless: true);
        if (args.Contains("--asset-tests")) return AssetTests.Run(root);
        if (args.Contains("--discord-check"))
        {
            var (ok, message) = DiscordPresence.Check(DiscordPresence.DefaultApplicationId);
            Directory.CreateDirectory(Path.Combine(root, "artifacts"));
            File.WriteAllText(Path.Combine(root, "artifacts", "discord-check.json"), System.Text.Json.JsonSerializer.Serialize(new { ok, message, checkedAt = DateTimeOffset.Now }));
            return ok ? 0 : 1;
        }
        var liveSmoke = Array.IndexOf(args, "--live-smoke");
        if (liveSmoke >= 0 && args.Length > liveSmoke + 1) return LiveSmoke.Run(root, args[liveSmoke + 1]);
        var import = Array.IndexOf(args, "--import-sample");
        if (import >= 0 && args.Length > import + 1)
        {
            var decoded = Scenario.Import(File.ReadAllText(args[import + 1]));
            Directory.CreateDirectory(Path.Combine(root, "artifacts"));
            File.WriteAllText(Path.Combine(root, "artifacts", "imported-scenario.json"), Scenario.Export(decoded.Nodes));
            File.WriteAllText(Path.Combine(root, "artifacts", "import-review.txt"), decoded.Notice + Environment.NewLine + string.Join(Environment.NewLine, decoded.InstallerCommands));
            return 0;
        }
        var smoke = Array.IndexOf(args, "--smoke-launch");
        var attach = Array.IndexOf(args, "--smoke-attach");
        var attachPid = attach >= 0 && args.Length > attach + 2 ? int.Parse(args[attach + 2]) : 0;
        var smokeVersion = attach >= 0 && args.Length > attach + 1 ? args[attach + 1]
            : smoke >= 0 && args.Length > smoke + 1 ? args[smoke + 1] : null;
        UserSettings.MigrateLegacyFolder();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Crash(ex, fatal: true); };
        Application.Run(new MainForm(root, smokeVersion, attachPid, args.Contains("--qa-item"), args.Contains("--background")));
        return 0;
    }

    // UI-thread exceptions are logged and reported; the editor keeps running so drafts are not lost.
    private static void Crash(Exception error, bool fatal)
    {
        string log = Path.Combine(UserSettings.Folder, "crash.log");
        try { Directory.CreateDirectory(UserSettings.Folder); File.AppendAllText(log, $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}{Environment.NewLine}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        MessageBox.Show($"{error.Message}\n\n{(fatal ? "The editor has to close." : "The editor will keep running; your draft is autosaved.")}\nDetails: {log}",
            "Command Forge — unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // The data root holds docs/research (release catalog) and artifacts (bridge jar, logs). It is the project
    // directory during development and the executable's own folder in a published build.
    internal static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "docs", "research", "mojang-version-manifest.json"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("Command Forge could not find its release catalog (docs\\research\\mojang-version-manifest.json).\n\n" +
            "Run it from the project folder, or use the folder created by publish.ps1 and keep its docs and artifacts folders next to CommandForge.exe.");
    }
}
