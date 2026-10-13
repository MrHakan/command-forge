using System.Diagnostics;
using System.Text.Json;

namespace UniversalEditor;

internal sealed class MainForm : Form
{
    private readonly string projectRoot;
    private readonly string? smokeVersion;
    private DateTime? captureAt;
    private DateTime? itemProofAt;
    private readonly bool itemProof;
    private readonly bool backgroundStart;
    private readonly bool persist;
    protected override bool ShowWithoutActivation => backgroundStart;
    private readonly UserSettings settings;
    private readonly DarkCombo versions = new() { Width = Ui.S(170), FormattingEnabled = true };
    private readonly NumericUpDown minRam = Ui.Number(512, 65536, 1024);
    private readonly NumericUpDown maxRam = Ui.Number(512, 65536, 4096);
    private readonly TextBox prismExe = Ui.TextInput();
    private readonly TextBox prismData = Ui.TextInput();
    private readonly Label versionInfo = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, Ui.S(10)), UseMnemonic = false };
    private readonly Label statusText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Small, ForeColor = Ui.Muted, AutoEllipsis = true, UseMnemonic = false, Padding = new Padding(Ui.S(12), 0, 0, 0) };
    private readonly TextBox log = Ui.TextInput("", true, true);
    private readonly NativeViewport viewport = new() { Dock = DockStyle.Fill };
    private readonly Panel placeholder = new() { Dock = DockStyle.Fill, BackColor = Ui.Canvas };
    private readonly Button launch, placeholderLaunch, focus, detach, closeGame, activityToggle, gameToggle;
    private readonly StatusPill gamePill = new(), livePill = new();
    private readonly Banner banner = new();
    private readonly NavRail nav = new();
    private readonly Label projectTitle = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(14), Ui.S(19), 0, 0), UseMnemonic = false };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, BackColor = Ui.Canvas };
    private readonly Control nbtPage, settingsPage;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private HashSet<int> baseline = [];
    private GameVersion? awaitingVersion;
    private DateTime launchStarted;
    private int logCount;
    private NbtDocument? document;
    private readonly TreeView nbtTree = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Ui.Canvas, ForeColor = Ui.Text, Font = Ui.Mono, ItemHeight = Ui.S(24), FullRowSelect = true, ShowLines = false };
    private readonly TextBox nbtValue = Ui.TextInput("", true, true);
    private readonly Label nbtInfo = new() { Dock = DockStyle.Top, Height = Ui.S(70), Font = Ui.Body, ForeColor = Ui.Muted, UseMnemonic = false };
    private readonly Label nbtFile = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(8), Ui.S(8), 0, 0), UseMnemonic = false };
    private readonly Button applyValue;
    private Button? saveNbt;
    private readonly LiveStudio studio;
    private DiscordPresence? discord;
    private string? embeddedVersion, nbtPath;
    private readonly Label discordStatus = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, Ui.S(10)), UseMnemonic = false };

    public MainForm(string root, string? smokeVersion, int attachPid = 0, bool itemProof = false, bool backgroundStart = false, bool? persistState = null)
    {
        projectRoot = root; this.smokeVersion = smokeVersion; this.itemProof = itemProof; this.backgroundStart = backgroundStart;
        persist = persistState ?? (smokeVersion is null && attachPid == 0 && !itemProof);
        settings = persist ? UserSettings.Load() : new UserSettings();
        Text = "Command Forge"; Width = 1680; Height = 980; MinimumSize = new Size(Ui.S(1200), Ui.S(760));
        StartPosition = FormStartPosition.CenterScreen;
        if (backgroundStart) WindowState = FormWindowState.Minimized;
        Font = Ui.Body; BackColor = Ui.Canvas; ForeColor = Ui.Text; KeyPreview = false; Ui.DarkTitleBar(this);
        try { if (Environment.ProcessPath is string exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is System.Drawing.Icon appIcon) this.Icon = appIcon; } catch (Exception e) when (e is IOException or ArgumentException) { }

        studio = new LiveStudio(() => Path.Combine(prismData.Text, "instances", "universal-editor-26.2", "minecraft"), persist);
        launch = Ui.Button("▶  Launch", Tone.Primary, "Start the selected release through Prism and embed its window here.", LaunchGame);
        placeholderLaunch = Ui.Button("▶  Launch Minecraft here", Tone.Primary, null, LaunchGame);
        focus = Ui.Button("Focus game", Tone.Secondary, "Send keyboard and mouse input to the game.", () => viewport.FocusGame());
        detach = Ui.Button("Detach", Tone.Ghost, "Move the game back into its own window. It keeps running.", () => { viewport.Detach(); ResetGameControls(); Record("Detached. Minecraft continues in its own window."); SetGame("Game · detached", Tone.Muted); });
        closeGame = Ui.Button("Close game", Tone.Danger, "Ask Minecraft to quit normally.", () => { if (viewport.IsAttached) NativeMethods.PostMessageW(viewport.GameHandle, 0x0010, IntPtr.Zero, IntPtr.Zero); });
        activityToggle = Ui.Button("Activity", Tone.Ghost, "Show or hide the activity log under the game.", () => SetActivity(!log.Visible));
        gameToggle = Ui.Button("Hide game panel", Tone.Ghost, "Give the editor the full width while no game is embedded.", ToggleGamePanel);
        applyValue = Ui.Button("Apply value", Tone.Primary, "Store this value in memory. Use Save as new copy to write a file.", ApplyNbtValue);
        nbtPage = BuildNbtPage(); settingsPage = BuildSettingsPage();

        // Header: brand, project, status pills.
        var header = new Panel { Dock = DockStyle.Top, Height = Ui.S(58), BackColor = Ui.Surface, Padding = new Padding(Ui.S(16), 0, Ui.S(12), 0) };
        var brand = new Label { Dock = DockStyle.Left, Width = Ui.S(230), Text = "" };
        brand.Paint += (_, e) =>
        {
            MinecraftAssets.Draw(e.Graphics, studio.Assets.Icon("blocks", "minecraft:command_block"), new Rectangle(0, Ui.S(13), Ui.S(32), Ui.S(32)));
            TextRenderer.DrawText(e.Graphics, "Command Forge", Ui.Heading, new Point(Ui.S(42), Ui.S(10)), Ui.Text);
            TextRenderer.DrawText(e.Graphics, "Java Edition authoring", Ui.Small, new Point(Ui.S(43), Ui.S(31)), Ui.Subtle);
        };
        var pills = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        gameToggle.Margin = new Padding(Ui.S(8), Ui.S(13), 0, 0);
        pills.Controls.AddRange([livePill, gamePill, gameToggle]);
        var middle = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        middle.Controls.Add(projectTitle);
        header.Controls.Add(middle); header.Controls.Add(pills); header.Controls.Add(brand);
        header.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };

        // Status bar.
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(30), BackColor = Ui.Surface };
        var hints = new Label { Dock = DockStyle.Right, AutoSize = true, Font = Ui.Small, ForeColor = Ui.Subtle, Padding = new Padding(0, Ui.S(8), Ui.S(12), 0), Text = "F5 Run · F6 Stage · Shift+F5 STOP · Ctrl+S Save · Ctrl+1–6 Pages" };
        activityToggle.Dock = DockStyle.Right; activityToggle.Margin = Padding.Empty; activityToggle.MinimumSize = new Size(Ui.S(90), 0);
        statusBar.Controls.Add(statusText); statusBar.Controls.Add(hints); statusBar.Controls.Add(activityToggle);
        statusBar.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0); };

        // Navigation.
        Func<Image?> Icon(string category, string id) => () => studio.Assets.Icon(category, id);
        nav.Add(new("Scenario", "Visual timeline", "Ctrl+1", Icon("blocks", "minecraft:command_block")));
        nav.Add(new("Item", "Items & components", "Ctrl+2", Icon("items", "minecraft:diamond_sword")));
        nav.Add(new("Commands", "Command block chain", "Ctrl+3", Icon("blocks", "minecraft:chain_command_block")));
        nav.Add(new("Import", "command.txt & setup", "Ctrl+4", Icon("items", "minecraft:writable_book")));
        nav.Add(new("NBT file", "Binary .dat / .nbt", "Ctrl+5", Icon("blocks", "minecraft:barrel")));
        nav.Add(new("Settings", "Release, RAM, Prism", "Ctrl+6", Icon("items", "minecraft:comparator")));
        nav.SelectedChanged += ShowPage;

        // Workspace (left) and game panel (right).
        var pagesHost = new Panel { Dock = DockStyle.Fill };
        foreach (var page in new[] { (Control)studio, nbtPage, settingsPage }) { page.Dock = DockStyle.Fill; pagesHost.Controls.Add(page); }
        split.Panel1.Controls.Add(pagesHost); split.Panel1.Controls.Add(banner);
        split.Panel1.BackColor = Ui.Surface;
        var gameHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Ui.Surface, Padding = new Padding(Ui.S(10), Ui.S(10), Ui.S(6), Ui.S(4)) };
        gameHeader.Controls.Add(new Label { Text = "Release", AutoSize = true, Font = Ui.Label, ForeColor = Ui.Muted, Margin = new Padding(0, Ui.S(8), Ui.S(6), 0) });
        versions.Margin = new Padding(0, Ui.S(3), Ui.S(8), Ui.S(6));
        gameHeader.Controls.AddRange([versions, launch, focus, detach, closeGame]);
        log.ReadOnly = true; log.Dock = DockStyle.Bottom; log.Height = Ui.S(150); log.BorderStyle = BorderStyle.None; log.BackColor = Ui.Surface; log.Font = Ui.Small;
        viewport.Controls.Add(placeholder);
        split.Panel2.Controls.Add(viewport); split.Panel2.Controls.Add(log); split.Panel2.Controls.Add(gameHeader);
        BuildPlaceholder();

        Controls.Add(split); Controls.Add(nav); Controls.Add(statusBar); Controls.Add(header);
        Ui.Apply(this);

        // Data.
        var prism = PrismLauncher.Discover();
        prismExe.Text = string.IsNullOrWhiteSpace(settings.PrismExecutable) ? prism.Executable : settings.PrismExecutable;
        prismData.Text = string.IsNullOrWhiteSpace(settings.PrismData) ? prism.DataDirectory : settings.PrismData;
        minRam.Value = Math.Clamp(settings.MinRam, 512, 65536); maxRam.Value = Math.Clamp(settings.MaxRam, (int)minRam.Value, 65536);
        minRam.ValueChanged += (_, _) => { if (minRam.Value > maxRam.Value) maxRam.Value = minRam.Value; };
        maxRam.ValueChanged += (_, _) => { if (maxRam.Value < minRam.Value) minRam.Value = maxRam.Value; };
        versions.Format += (_, e) => { if (e.ListItem is GameVersion v) e.Value = v.Id == "26.2" ? "26.2  ·  live" : v.Id; };
        foreach (var version in VersionCatalog.Load(root)) versions.Items.Add(version);
        versions.SelectedItem = versions.Items.Cast<GameVersion>().FirstOrDefault(v => v.Id == (smokeVersion ?? settings.Release ?? "26.2")) ?? versions.Items[0];
        versions.SelectedIndexChanged += (_, _) => ShowVersion(); ShowVersion();

        studio.StatusChanged += s => livePill.Set(s.Title, s.Online ? Tone.Success : s.Title.EndsWith("error", StringComparison.Ordinal) ? Tone.Danger : Tone.Muted, s.Detail);
        studio.Notify += Notify;
        studio.TitleChanged += UpdateTitle;
        livePill.Set("Live · offline", Tone.Muted, "Live editing needs the bridge-enabled Java 26.2 instance with a singleplayer world open.");
        SetGame("Game · not running", Tone.Muted);
        ResetGameControls(); SetActivity(settings.ActivityOpen);

        Ui.InitialSplit(split, width => settings.Splitter is int saved && saved > Ui.S(480) && saved < width - Ui.S(360) ? saved : Math.Clamp(width * 55 / 100, Ui.S(560), width - Ui.S(420)));
        timer.Tick += (_, _) => { PollGame(); UpdatePresence(); }; timer.Start();
        FormClosing += (_, _) => { studio.Shutdown(); timer.Stop(); viewport.Detach(); SaveSettings(); discord?.Dispose(); discord = null; };
        StartDiscord();
        Shown += (_, _) =>
        {
            studio.InitializeView();
            if (!backgroundStart) BeginInvoke(() => NativeMethods.ShowWindow(Handle, 5));
            if (attachPid > 0)
            {
                try
                {
                    var window = NativeViewport.FindGames().Single(w => w.ProcessId == attachPid);
                    viewport.Attach(window); OnEmbedded(smokeVersion ?? "?");
                    Record($"Reattached test Minecraft process {attachPid}."); SaveSmokeReport(smokeVersion!, window);
                }
                catch (Exception ex) { ShowError(ex); }
            }
            else if (smokeVersion is not null) LaunchGame();
        };
        nav.Selected = Math.Clamp(settings.Page, 0, 5); ShowPage(nav.Selected); UpdateTitle();
        Record($"Loaded {versions.Items.Count} Java releases. Live editing is available for 26.2 singleplayer.");
    }

    private void ShowPage(int index)
    {
        studio.Visible = index < 4; nbtPage.Visible = index == 4; settingsPage.Visible = index == 5;
        if (index < 4) studio.Navigate((StudioPage)index);
        UpdateTitle();
    }
    private void UpdateTitle()
    {
        string title = nav.Selected switch { 4 => "NBT file", 5 => "Settings", _ => studio.ProjectTitle + (studio.Unsaved ? "  •" : "") };
        projectTitle.Text = nav.Selected < 4 && studio.ProjectPath is not null ? $"{title}   —   {Path.GetFileName(studio.ProjectPath)}" : title;
        Text = $"{title} — Command Forge";
    }
    private void Notify(string message, Tone tone) { banner.Show(message, tone); if (tone is Tone.Danger or Tone.Warning) Record(message); else statusText.Text = message; }
    private void SetGame(string text, Tone tone, string? detail = null) => gamePill.Set(text, tone, detail);
    private void SetActivity(bool open) { log.Visible = open; activityToggle.Text = $"Activity {(open ? "▾" : "▴")}{(logCount > 0 ? $"  {logCount}" : "")}"; }
    private void ToggleGamePanel()
    {
        if (viewport.IsAttached || awaitingVersion is not null) { Notify("Detach or close the game before hiding its panel.", Tone.Info); return; }
        split.Panel2Collapsed = !split.Panel2Collapsed; gameToggle.Text = split.Panel2Collapsed ? "Show game panel" : "Hide game panel";
    }
    private void SaveSettings()
    {
        if (!persist) return;
        settings.MinRam = (int)minRam.Value; settings.MaxRam = (int)maxRam.Value; settings.PrismExecutable = prismExe.Text; settings.PrismData = prismData.Text;
        settings.Release = (versions.SelectedItem as GameVersion)?.Id; settings.Page = nav.Selected; settings.ActivityOpen = log.Visible;
        if (!split.Panel2Collapsed && WindowState == FormWindowState.Normal) settings.Splitter = split.SplitterDistance;
        settings.Save();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData >= (Keys.Control | Keys.D1) && keyData <= (Keys.Control | Keys.D6)) { nav.Selected = (int)(keyData & Keys.KeyCode) - (int)Keys.D1; return true; }
        if ((nav.Selected < 4 || keyData == (Keys.Shift | Keys.F5)) && studio.HandleShortcut(keyData, DeepActive() is TextBoxBase)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private Control? DeepActive() { Control? c = ActiveControl; while (c is ContainerControl { ActiveControl: { } inner }) c = inner; return c; }
    private void ShowVersion()
    {
        if (versions.SelectedItem is not GameVersion version) return;
        bool live = version.Id == "26.2";
        versionInfo.Text = $"Minecraft {version.Id} needs Java {version.JavaMajor} · protocol {version.ProtocolId?.ToString() ?? "unknown"}\n" +
            (live ? "Launches with the live bridge: live staging, read-back and item apply work in private singleplayer." : "Launches vanilla. Live editing is not available for this release yet; drafts and NBT files still work.") +
            "\nEmbedding the game window is experimental.";
        launch.Text = $"▶  Launch {version.Id}"; placeholderLaunch.Text = $"▶  Launch Minecraft {version.Id} here";
        Ui.Tips.SetToolTip(versions, live ? "26.2 has the live bridge." : "This release launches vanilla (no live editing).");
        placeholder.Invalidate();
    }
    private void BuildPlaceholder()
    {
        placeholder.Controls.Add(placeholderLaunch);
        placeholder.Resize += (_, _) => placeholderLaunch.Location = new Point((placeholder.Width - placeholderLaunch.Width) / 2, placeholder.Height / 2 + Ui.S(110));
        placeholder.Paint += (_, e) =>
        {
            var g = e.Graphics; int cx = placeholder.Width / 2, top = placeholder.Height / 2 - Ui.S(170);
            MinecraftAssets.Draw(g, studio.Assets.Icon("blocks", "minecraft:grass_block"), new Rectangle(cx - Ui.S(40), top, Ui.S(80), Ui.S(80)));
            TextRenderer.DrawText(g, "Minecraft runs here", Ui.Title, new Rectangle(0, top + Ui.S(92), placeholder.Width, Ui.S(30)), Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            string release = (versions.SelectedItem as GameVersion)?.Id ?? "26.2";
            string steps = $"1  Pick a release above (26.2 has live editing) and set RAM in Settings\n2  Launch — Prism handles downloads and your account\n3  26.2 opens your editor world by itself; the Live badge turns green";
            TextRenderer.DrawText(g, steps, Ui.Body, new Rectangle(Ui.S(20), top + Ui.S(132), placeholder.Width - Ui.S(40), Ui.S(80)), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, $"Selected: {release}", Ui.Small, new Rectangle(0, top + Ui.S(320), placeholder.Width, Ui.S(20)), Ui.Subtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        };
    }
    private void Record(string message)
    {
        statusText.Text = message; log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); logCount++; SetActivity(log.Visible);
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "artifacts"));
            File.AppendAllText(Path.Combine(projectRoot, "artifacts", "prototype.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    private void LaunchGame()
    {
        if (viewport.IsAttached || awaitingVersion is not null) return;
        try
        {
            var selected = (GameVersion)versions.SelectedItem!;
            baseline = [];
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try { if (process.ProcessName is "java" or "javaw") baseline.Add(process.Id); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            var launcher = new PrismLauncher(prismData.Text, prismExe.Text);
            var bridgePath = Path.Combine(projectRoot, "artifacts", "universal-editor-bridge-26.2.jar");
            bool bridged = selected.Id == "26.2" && File.Exists(bridgePath);
            var id = launcher.Prepare(selected, (int)minRam.Value, (int)maxRam.Value, bridged ? bridgePath : null);
            if (bridged && persist)
            {
                var plan = WorldLaunch.Prepare(settings, Path.Combine(prismData.Text, "instances", id, "minecraft"));
                if (plan is not null) Record(plan);
                SaveSettings();
            }
            if (split.Panel2Collapsed) ToggleGamePanel();
            awaitingVersion = selected; launchStarted = DateTime.UtcNow; launch.Enabled = placeholderLaunch.Enabled = versions.Enabled = false;
            launcher.Launch(id);
            SetGame($"Game · launching {selected.Id}…", Tone.Warning, "Waiting for the Minecraft window. Prism may ask for downloads or an account first.");
            Record($"Launching {selected.Id} with {(int)maxRam.Value} MB maximum RAM. Waiting for its game window…");
        }
        catch (Exception ex) { awaitingVersion = null; ResetGameControls(); ShowError(ex); }
    }
    private void OnEmbedded(string version)
    {
        placeholder.Visible = false; awaitingVersion = null; embeddedVersion = version;
        focus.Visible = detach.Visible = closeGame.Visible = true; launch.Visible = false; versions.Enabled = false;
        SetGame($"Game · {version} embedded", Tone.Success, "Click Focus game (or inside the game) to play.");
    }
    private void PollGame()
    {
        try
        {
            CaptureIfReady();
            if (awaitingVersion is not null)
            {
                var candidates = NativeViewport.FindGames().Where(w => !baseline.Contains(w.ProcessId)).ToArray();
                if (candidates.Length > 1) throw new IOException("Multiple new Minecraft windows appeared. Close the extra launch and try again; no window was attached.");
                if (candidates.Length == 1)
                {
                    var version = awaitingVersion;
                    viewport.Attach(candidates[0]); OnEmbedded(version.Id);
                    Record($"Embedded {version.Id}, process {candidates[0].ProcessId}. Click Focus game to play.");
                    if (smokeVersion is not null) SaveSmokeReport(version.Id, candidates[0]);
                }
                else if ((DateTime.UtcNow - launchStarted).TotalSeconds > 180)
                    throw new TimeoutException("No new Minecraft window appeared within three minutes. Check Prism for account selection, downloads or launch errors, then retry.");
            }
            else if (focus.Visible && !viewport.IsAttached) { viewport.Detach(); ResetGameControls(); Record("Minecraft has closed."); SetGame("Game · closed", Tone.Muted); }
        }
        catch (Exception ex) { awaitingVersion = null; ResetGameControls(); ShowError(ex); SetGame("Game · launch failed", Tone.Danger, ex.Message); }
    }
    private void CaptureIfReady()
    {
        if (itemProofAt is not null && DateTime.UtcNow >= itemProofAt) { itemProofAt = null; nav.Selected = 1; studio.ShowItemProof(); captureAt = DateTime.UtcNow.AddSeconds(2); }
        if (captureAt is null || DateTime.UtcNow < captureAt) return;
        captureAt = null;
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != Handle && (!viewport.IsAttached || foreground != viewport.GameHandle))
        { Record("Visual capture skipped because the prototype is not in the foreground."); return; }
        using var screenshot = new Bitmap(Width, Height);
        using var graphics = Graphics.FromImage(screenshot);
        graphics.CopyFromScreen(Location, Point.Empty, Size);
        screenshot.Save(Path.Combine(projectRoot, "artifacts", itemProof ? "item-designer-screen.png" : "prototype-screen.png"));
        Record("Captured the prototype window for visual verification.");
    }
    private void SaveSmokeReport(string version, GameWindow window)
    {
        NativeMethods.GetWindowRect(window.Handle, out var bounds);
        File.WriteAllText(Path.Combine(projectRoot, "artifacts", "minecraft-embedding-smoke.json"), JsonSerializer.Serialize(new
        { version, window.ProcessId, window.Title, parentMatches = NativeMethods.GetParent(window.Handle) == viewport.Handle,
            width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top, tested = "native parent and viewport dimensions; interactive input not yet verified" }, new JsonSerializerOptions { WriteIndented = true }));
        BeginInvoke(() =>
        {
            NativeMethods.SetForegroundWindow(Handle);
            captureAt = DateTime.UtcNow.AddSeconds(6);
            if (itemProof) { captureAt = null; itemProofAt = DateTime.UtcNow.AddSeconds(6); }
        });
    }
    private void ResetGameControls()
    {
        focus.Visible = detach.Visible = closeGame.Visible = false; launch.Visible = true; embeddedVersion = null;
        launch.Enabled = placeholderLaunch.Enabled = versions.Enabled = true; placeholder.Visible = true;
    }
    private void ShowError(Exception ex) { Record(ex.Message); banner.Show(ex.Message, Tone.Danger); }

    private static string AppVersion { get { var v = typeof(Program).Assembly.GetName().Version; return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}"; } }
    private void StartDiscord()
    {
        discord?.Dispose(); discord = null;
        string id = string.IsNullOrWhiteSpace(settings.DiscordApplicationId) ? DiscordPresence.DefaultApplicationId : settings.DiscordApplicationId.Trim();
        if (!persist || !settings.DiscordEnabled) { discordStatus.Text = "Discord status: off"; return; }
        if (!DiscordPresence.ValidApplicationId(id)) { discordStatus.Text = "Discord status: waiting for an application ID"; return; }
        discord = new DiscordPresence(id); UpdatePresence();
    }
    private void UpdatePresence()
    {
        if (discord is null) return;
        discordStatus.Text = "Discord status: " + discord.Status;
        bool names = settings.DiscordShowNames;
        var (details, state) = nav.Selected switch
        {
            4 => ("Inspecting an NBT file", names && nbtPath is not null ? Path.GetFileName(nbtPath) : document is null ? "No file open" : "Binary NBT data"),
            5 => ("Configuring the editor", $"Minecraft {(versions.SelectedItem as GameVersion)?.Id}"),
            _ => studio.PresenceText(names)
        };
        // Uploaded art: the command-chain picture was saved under the "nbt" key and there is no "commands" key yet.
        string large = nav.Selected switch { 0 => "scenario", 1 => "item", 2 => "nbt", _ => "logo" };
        var (small, smallText) = studio.LiveOnline ? ("live", $"Live editing · Minecraft {embeddedVersion ?? "26.2"}")
            : embeddedVersion is not null ? ("game", $"Minecraft {embeddedVersion} running")
            : awaitingVersion is not null ? ("game", $"Launching Minecraft {awaitingVersion.Id}")
            : ("idle", "Minecraft not running");
        discord.Update(new(details, state, large, $"Command Forge {AppVersion} · Java Edition authoring", small, smallText));
    }

    // ---------- Settings page ----------
    private Control BuildSettingsPage()
    {
        var f = new FormFields();
        f.Custom(new Label { Text = "Settings", AutoSize = true, Font = Ui.Title, ForeColor = Ui.Text, Margin = new Padding(0, Ui.S(10), 0, Ui.S(4)) });
        f.Note("Saved for your Windows account when the editor closes. Drafts are autosaved separately.");
        f.Section("Minecraft release");
        f.Custom(versionInfo);
        f.Note("Choose the release in the game panel header. The full catalog is listed for launching; a listing does not mean live editing support.");
        f.Section("Memory");
        minRam.Increment = maxRam.Increment = 256;
        f.Row("Minimum RAM (MB)", minRam);
        f.Row("Maximum RAM (MB)", maxRam, "Applies on the next launch. 512 MB ≤ minimum ≤ maximum ≤ 65536 MB.");
        f.Section("Prism Launcher");
        var browseExe = Ui.Button("Browse…", Tone.Secondary, null, () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Prism Launcher (prismlauncher.exe)|prismlauncher.exe|Programs (*.exe)|*.exe", FileName = prismExe.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) prismExe.Text = dialog.FileName;
        });
        var browseData = Ui.Button("Browse…", Tone.Secondary, null, () =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = prismData.Text, Description = "Prism Launcher data directory (contains instances and libraries)" };
            if (dialog.ShowDialog(this) == DialogResult.OK) prismData.Text = dialog.SelectedPath;
        });
        browseExe.AutoSize = browseData.AutoSize = false; browseExe.Width = browseData.Width = Ui.S(90);
        f.Line("Prism executable", prismExe, browseExe);
        f.Line("Prism data directory", prismData, browseData);
        f.Button("Use detected Prism paths", () => { var p = PrismLauncher.Discover(); prismExe.Text = p.Executable; prismData.Text = p.DataDirectory; }, Tone.Ghost);
        f.Note("Command Forge creates and owns a separate instance named universal-editor-<release> (name kept from earlier versions). Your own instances, worlds and mods are never modified. Accounts and downloads stay inside Prism.");
        BuildWorldSettings(f);
        f.Section("Discord");
        f.Toggle("Show what I am doing on my Discord profile", settings.DiscordEnabled, v => { settings.DiscordEnabled = v; StartDiscord(); });
        f.Toggle("Include scenario, item and file names", settings.DiscordShowNames, v => { settings.DiscordShowNames = v; UpdatePresence(); },
            "Off by default: only generic activity such as “Designing a scenario · 5 action cards” is shared.");
        var discordId = f.TextField("Discord application ID", settings.DiscordApplicationId ?? "", v => settings.DiscordApplicationId = v.Trim(),
            hint: "From the Discord Developer Portal (General Information → Application ID). Leave empty to use the built-in ID.", placeholder: DiscordPresence.DefaultApplicationId.Length > 0 ? "Built-in" : "17–20 digits");
        discordId.Leave += (_, _) => StartDiscord();
        f.Custom(discordStatus);
        f.Note("Talks to the Discord desktop app on this PC through its local connection. The editor reads no Discord login or token, and never shares player names, worlds or servers.");
        f.Section("Support scope");
        f.Note("Live editing: Java 26.2, private singleplayer, one player. Other releases launch vanilla. Dedicated servers, other version adapters, resource packs and special 3D models are not implemented yet.");
        return f;
    }

    private string ManagedGameDirectory => Path.Combine(prismData.Text, "instances", "universal-editor-26.2", "minecraft");
    private void BuildWorldSettings(FormFields f)
    {
        f.Section("World on launch (26.2)");
        var groups = new Dictionary<string, List<Control>> { ["existing"] = [], ["new"] = [] };
        void Group(string key, Action build) { int start = f.Controls.Count; build(); groups[key].AddRange(f.Controls.Cast<Control>().Skip(start)); }
        void Show()
        {
            foreach (var c in groups["existing"]) c.Visible = settings.WorldStartup == WorldStartup.OpenExisting;
            foreach (var c in groups["new"]) c.Visible = settings.WorldStartup is WorldStartup.OpenOrCreate or WorldStartup.CreateNew;
        }
        f.Choice("When Minecraft 26.2 starts", WorldLaunch.Modes, WorldLaunch.Modes[(int)settings.WorldStartup], v => { settings.WorldStartup = (WorldStartup)Array.IndexOf(WorldLaunch.Modes, v); Show(); },
            hint: "Handled by the live bridge on the title screen. Other releases always start on the main menu.");
        DarkCombo? worlds = null;
        Group("existing", () =>
        {
            worlds = f.Choice("World to open", WorldLaunch.ExistingWorlds(ManagedGameDirectory), settings.OpenWorld ?? "", v => settings.OpenWorld = v.Length > 0 ? v : null,
                display: v => v.Length == 0 ? "Choose a world…" : v, hint: "Worlds of the editor-managed 26.2 instance, newest first.");
            f.Button("Refresh world list", () =>
            {
                var list = WorldLaunch.ExistingWorlds(ManagedGameDirectory); var current = settings.OpenWorld ?? "";
                worlds!.Items.Clear(); worlds.Items.AddRange(list.Cast<object>().ToArray()); if (!worlds.Items.Contains(current)) worlds.Items.Insert(0, current); worlds.SelectedItem = current;
            }, Tone.Ghost);
        });
        Group("new", () =>
        {
            f.TextField("New world name", settings.NewWorldName, v => settings.NewWorldName = v, hint: "The folder name uses letters, digits, spaces and _ ( ) . - only; an existing world is never overwritten.");
            f.Choice("World type", ["flat", "normal"], settings.NewWorldPreset, v => settings.NewWorldPreset = v, display: v => v == "flat" ? "Superflat (best for building scenarios)" : "Default terrain");
            f.Choice("Game mode", ["creative", "survival", "adventure"], settings.NewWorldGameMode, v => settings.NewWorldGameMode = v, display: v => char.ToUpperInvariant(v[0]) + v[1..]);
            f.Choice("Difficulty", ["peaceful", "easy", "normal", "hard"], settings.NewWorldDifficulty, v => settings.NewWorldDifficulty = v, display: v => char.ToUpperInvariant(v[0]) + v[1..]);
            f.TextField("Seed", settings.NewWorldSeed, v => settings.NewWorldSeed = v, placeholder: "Random");
            f.Toggle("Generate structures", settings.NewWorldStructures, v => settings.NewWorldStructures = v);
            f.Toggle("Always day (stop the day cycle at noon)", settings.AlwaysDay, v => settings.AlwaysDay = v);
            f.Toggle("Clear weather (stop the weather cycle)", settings.ClearWeather, v => settings.ClearWeather = v);
            f.Toggle("No natural mob spawning", settings.NoMobSpawning, v => settings.NoMobSpawning = v);
            f.Toggle("Hide command block messages in chat", settings.QuietCommandBlocks, v => settings.QuietCommandBlocks = v);
            f.Note("Cheats are always enabled so the editor can run commands. Rules are applied once, when the world is created.");
        });
        f.Toggle("Keep the world running while the editor has focus", settings.KeepWorldRunning, v => settings.KeepWorldRunning = v,
            "Turns off Minecraft's pause-on-lost-focus for worlds the editor opens, so timelines keep running while you edit.");
        f.Note(settings.EditorWorld is null ? "No editor world yet; one is created on the next 26.2 launch." : $"Current editor world: {settings.EditorWorld}");
        Show();
    }

    // ---------- NBT page ----------
    private Control BuildNbtPage()
    {
        var page = new Panel();
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Ui.S(14), Ui.S(12), Ui.S(14), Ui.S(6)), BackColor = Ui.Surface };
        var open = Ui.Button("Open NBT file…", Tone.Primary, "Open a standalone Java .dat / .nbt file (plain, gzip or zlib).", OpenNbt);
        var save = saveNbt = Ui.Button("Save as new copy…", Tone.Secondary, "Write the edited tree to a new file. Existing files are never overwritten.", SaveNbt);
        tools.Controls.AddRange([open, save, nbtFile]);
        var nbtSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = Ui.S(6), BackColor = Ui.Canvas };
        nbtSplit.Panel1.Controls.Add(nbtTree); nbtSplit.Panel1.Padding = new Padding(Ui.S(6)); nbtSplit.Panel1.BackColor = Ui.Canvas;
        var editor = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), Ui.S(16)), BackColor = Ui.Surface };
        var applyLine = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, Ui.S(8), 0, 0) }; applyLine.Controls.Add(applyValue);
        nbtValue.Dock = DockStyle.Fill; nbtValue.ScrollBars = ScrollBars.Both; nbtValue.WordWrap = false;
        editor.Controls.Add(nbtValue); editor.Controls.Add(applyLine); editor.Controls.Add(nbtInfo);
        nbtSplit.Panel2.Controls.Add(editor);
        Ui.InitialSplit(nbtSplit, width => width / 2);
        page.Controls.Add(nbtSplit); page.Controls.Add(tools);
        applyValue.Enabled = false; save.Enabled = false;
        nbtTree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is not NbtTag tag) return;
            bool container = tag.Type is NbtType.Compound or NbtType.List;
            nbtInfo.Text = $"{(tag.Name.Length == 0 ? "(unnamed)" : tag.Name)}  ·  TAG_{tag.Type}\n" + (container ? "Select a child value to edit it." : "The binary tag type is kept. Use invariant decimal numbers; arrays accept commas.");
            nbtValue.Text = tag.DisplayValue(); applyValue.Enabled = !container; nbtValue.ReadOnly = container;
        };
        nbtTree.BeforeExpand += (_, e) =>
        {
            if (e.Node?.Tag is NbtTag tag && e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag is null)
            {
                e.Node.Nodes.Clear();
                foreach (var child in (List<NbtTag>)tag.Value) e.Node.Nodes.Add(CreateNode(child));
            }
        };
        nbtFile.Text = "No file open";
        nbtInfo.Text = "Open a standalone Java NBT file. Plain, gzip and zlib compression are supported.\nRegion files and live world writes are not implemented.";
        return page;
    }
    private void OpenNbt()
    {
        using var dialog = new OpenFileDialog { Filter = "NBT files (*.dat;*.nbt)|*.dat;*.nbt|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            document = NbtCodec.ReadFile(dialog.FileName); nbtPath = dialog.FileName; nbtTree.Nodes.Clear(); nbtTree.Nodes.Add(CreateNode(document.Root));
            nbtTree.Nodes[0].Expand(); nbtFile.Text = $"{Path.GetFileName(dialog.FileName)} · {document.Compression}";
            saveNbt!.Enabled = true;
            Record($"Opened {Path.GetFileName(dialog.FileName)} ({document.Compression}). Changes stay in memory until saved as a new copy.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveNbt()
    {
        if (document is null) return;
        using var dialog = new SaveFileDialog { Filter = "NBT file (*.dat)|*.dat|NBT file (*.nbt)|*.nbt", FileName = "edited-copy.dat" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { NbtCodec.SaveCopy(document, dialog.FileName); Record("Saved a new NBT copy. Original file was preserved."); banner.Show($"Saved {Path.GetFileName(dialog.FileName)}. The original file was not changed.", Tone.Success); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ApplyNbtValue()
    {
        if (nbtTree.SelectedNode?.Tag is not NbtTag tag) return;
        try { tag.SetText(nbtValue.Text); nbtTree.SelectedNode.Text = NodeLabel(tag); Record($"Updated {tag.Name} in memory."); }
        catch (Exception ex) { ShowError(ex); }
    }
    private static string NodeLabel(NbtTag tag)
    {
        var value = tag.Type is NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray ? $"{((Array)tag.Value).Length} values"
            : tag.Type is NbtType.Compound or NbtType.List ? $"{((List<NbtTag>)tag.Value).Count} entries" : tag.DisplayValue();
        if (value.Length > 80) value = value[..80] + "…";
        return $"{(tag.Name.Length == 0 ? "(unnamed)" : tag.Name)}  ·  {tag.Type}  ·  {value}";
    }
    private static TreeNode CreateNode(NbtTag tag)
    {
        var node = new TreeNode(NodeLabel(tag)) { Tag = tag };
        if (tag.Type is NbtType.Compound or NbtType.List && ((List<NbtTag>)tag.Value).Count > 0)
            node.Nodes.Add(new TreeNode("…"));
        return node;
    }
    internal void ShowPageForTest(int index) { nav.Selected = index; ShowPage(index); }
    // A hidden top-level form does not print its children, so tests move the same control tree into a parked
    // (never shown) container, render it, and move it back.
    internal Bitmap RenderForTest(Size size)
    {
        var children = Controls.Cast<Control>().ToArray();
        using var shell = new Panel { Size = size, BackColor = BackColor, ForeColor = ForeColor, Font = Font };
        shell.Controls.AddRange(children); shell.PerformLayout();
        var image = new Bitmap(size.Width, size.Height); shell.DrawToBitmap(image, new Rectangle(Point.Empty, size));
        Controls.AddRange(children); return image;
    }
}
