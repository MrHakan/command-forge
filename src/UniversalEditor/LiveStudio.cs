using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UniversalEditor;

internal enum StudioPage { Scenario, Item, Commands, Import }
internal sealed record LiveStatus(bool Online, string Title, string Detail);

internal sealed class LiveStudio : UserControl
{
    private readonly Func<string> gameDirectory;
    private readonly GameCatalog catalog = new();
    private readonly ItemDesigner itemDesigner;
    private readonly ScenarioDesigner designer;
    private readonly Dictionary<StudioPage, Control> pages = [];
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    private readonly Panel liveBar = new() { Dock = DockStyle.Top, BackColor = Ui.Surface, Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(4)) };
    private string catalogSession = "";
    private bool UseDesigner => lastAuthoring == StudioPage.Scenario;
    private StudioPage lastAuthoring = StudioPage.Scenario;
    private LiveConnection? connection;
    private readonly BindingList<ScenarioNode> nodes = new(Scenario.Demo());
    private readonly ListBox steps = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, BackColor = Ui.Canvas, ForeColor = Ui.Text, IntegralHeight = false };
    private readonly TextBox command = Ui.TextInput("", true, true);
    private readonly TextBox explanation = Ui.TextInput("", true);
    private readonly TextBox name = Ui.TextInput();
    private readonly CheckBox conditional = new() { Text = "Conditional: run only if the previous block succeeded", AutoSize = true, Font = Ui.Body };
    private readonly Label feedback = new() { Dock = DockStyle.Fill, Font = Ui.Mono, ForeColor = Ui.Muted, UseMnemonic = false };
    private readonly Label liveMessage = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(10), Ui.S(7), 0, 0), UseMnemonic = false };
    private readonly Label targetLabel = new() { AutoSize = true, Font = Ui.Label, Margin = new Padding(Ui.S(10), Ui.S(9), 0, 0), UseMnemonic = false };
    private readonly NumericUpDown x = Coordinate(), y = Coordinate(), z = Coordinate();
    private readonly CheckBox live = new() { Text = "Live sync", AutoSize = true, Font = Ui.Strong, Margin = new Padding(Ui.S(8), Ui.S(7), Ui.S(8), 0) };
    private readonly Button stage, run, stop, readBlocks, near;
    private Button checkBlocks = null!, deleteBlock = null!;
    private readonly TextBox review = Ui.TextInput("", true, true);
    private readonly TextBox setupCommand = Ui.TextInput("scoreboard objectives add demo dummy", true, true);
    private readonly Button executeSetup;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private bool selecting, dirty, running, draftDirty;
    private DateTime changedAt, draftChangedAt;
    private string lastAck = "", session = "", expectedCommands = "", lastStatus = "";
    private bool awaitingInstall, anchorPending, awaitingValidation;
    private readonly Dictionary<int, string> nodeErrors = [];
    private readonly bool persist;
    public StudioPage Page { get; private set; } = StudioPage.Scenario;
    public MinecraftAssets Assets => catalog.Assets;
    public string? ProjectPath { get; private set; }
    public bool LiveOnline { get; private set; }
    public bool Unsaved { get; private set; }
    public string ProjectTitle => UseDesigner ? designer.Project.Name : ProjectPath is null ? "Command list" : Path.GetFileNameWithoutExtension(ProjectPath);
    public event Action<LiveStatus>? StatusChanged;
    public event Action<string, Tone>? Notify;
    public event Action? TitleChanged;

    public LiveStudio(Func<string> directory, bool persist = true)
    {
        gameDirectory = directory; this.persist = persist; Dock = DockStyle.Fill;
        catalog.Assets.Open(PrismLauncher.Discover().DataDirectory);
        catalog.LoadVisualChoices();
        itemDesigner = new ItemDesigner(catalog);
        designer = new ScenarioDesigner(catalog, () => itemDesigner.Recipe);
        designer.Edited += () => { Changed(); MarkUnsaved(); };
        designer.Replacing += () => { live.Checked = false; dirty = false; };
        designer.Message += (text, tone) => Notify?.Invoke(text, tone);
        itemDesigner.Message += (text, tone) => Notify?.Invoke(text, tone);
        itemDesigner.Edited += MarkDraft;
        itemDesigner.ApplyRequested += recipe => Guard(() =>
        {
            var p = Player(); Connection().Send(new JsonObject { ["operation"] = "item", ["playerId"] = p["id"]!.GetValue<string>(), ["item"] = recipe.Argument(), ["count"] = recipe.Count });
            liveMessage.Text = "Sending the item to your main hand…";
        });

        // Live bar: what to do with the active design in the connected world.
        stage = Ui.Button("Stage", Tone.Secondary, "Validate every command with the game's parser and place inactive command blocks (F6).", () => Guard(() => Apply(false)));
        run = Ui.Button("▶  Run / restart", Tone.Primary, "Place or update the blocks, then start the timeline from zero (F5).", () => Guard(() => Apply(true, true)));
        stop = Ui.Button("■  STOP", Tone.Danger, "Disable and clear the owned command blocks (Shift+F5). World effects are not undone.", () => Guard(Stop));
        readBlocks = Ui.Button("Read game blocks", Tone.Ghost, "Pull commands edited with Minecraft's own command-block screen into the command list.", () => Guard(ReadBlocks));
        near = Ui.Button("Near player", Tone.Secondary, "Use a spot 4 blocks from your player. Make sure the +Z strip is empty air.", () => Guard(() =>
        { var player = Player(); x.Value = player["x"]!.GetValue<int>() + 4; y.Value = player["y"]!.GetValue<int>() + 1; z.Value = player["z"]!.GetValue<int>() + 4; MarkDraft(); }));
        Ui.Tips.SetToolTip(live, "Apply edits automatically 0.7 s after you stop typing. Keeps the timeline clock.");
        live.CheckedChanged += (_, _) => { if (live.Checked && connection?.Ready != true) { live.Checked = false; Notify?.Invoke("Live sync needs a connected 26.2 world. Launch the game and open a singleplayer world first.", Tone.Info); } };
        liveMessage.Text = "Offline · launch 26.2 and open a world to stage or run."; liveMessage.ForeColor = Ui.Subtle;
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        row1.Controls.AddRange([run, stage, stop, Separator(), live, readBlocks, targetLabel]);
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        row2.Controls.Add(new Label { Text = "Anchor", AutoSize = true, Font = Ui.Label, ForeColor = Ui.Muted, Margin = new Padding(0, Ui.S(8), Ui.S(8), 0) });
        foreach (var (axis, input, color) in new[] { ("X", x, Color.FromArgb(236, 104, 104)), ("Y", y, Color.FromArgb(112, 214, 118)), ("Z", z, Color.FromArgb(108, 156, 246)) })
        {
            row2.Controls.Add(new Label { Text = axis, AutoSize = true, Font = Ui.Strong, ForeColor = color, Margin = new Padding(Ui.S(4), Ui.S(7), Ui.S(2), 0) });
            input.Margin = new Padding(0, Ui.S(3), Ui.S(4), 0); input.ValueChanged += (_, _) => MarkDraft(); row2.Controls.Add(input);
        }
        near.Margin = new Padding(Ui.S(6), 0, 0, Ui.S(6)); row2.Controls.Add(near);
        Ui.Tips.SetToolTip(row2, "First block position. The chain extends south (+Z). Limits: 128 blocks, within 64 blocks of the player.");
        row2.Controls.Add(liveMessage);
        liveBar.Controls.Add(row2); liveBar.Controls.Add(row1);
        liveBar.Height = Ui.S(102);

        pages[StudioPage.Scenario] = designer;
        pages[StudioPage.Item] = itemDesigner;
        pages[StudioPage.Commands] = BuildCommandsPage();
        pages[StudioPage.Import] = BuildImportPage(out executeSetup);
        foreach (var page in pages.Values) { page.Dock = DockStyle.Fill; page.Visible = false; pageHost.Controls.Add(page); }
        Controls.Add(pageHost); Controls.Add(liveBar);
        steps.DataSource = nodes; steps.SelectedIndexChanged += (_, _) => SelectNode();
        foreach (var input in new TextBox[] { name, command, explanation }) input.TextChanged += (_, _) => EditNode();
        conditional.CheckedChanged += (_, _) => EditNode();
        if (persist) RestoreDraft();
        SelectNode(); Navigate(StudioPage.Scenario);
        Ui.Apply(this); UpdateLiveControls(false, false);
        timer.Tick += (_, _) => Poll(); timer.Start();
        Disposed += (_, _) => { timer.Dispose(); catalog.Assets.Dispose(); };
    }
    private static Control Separator() => new Panel { Width = 1, Height = Ui.S(28), BackColor = Ui.Border, Margin = new Padding(Ui.S(6), Ui.S(2), Ui.S(6), 0) };
    private static NumericUpDown Coordinate() { var n = Ui.Number(-30000000, 30000000, 0); n.Width = Ui.S(86); return n; }

    public void Navigate(StudioPage page)
    {
        if (page is StudioPage.Scenario or StudioPage.Commands && page != lastAuthoring) { live.Checked = false; lastAuthoring = page; }
        Page = page;
        foreach (var (key, control) in pages) control.Visible = key == page;
        liveBar.Visible = page is StudioPage.Scenario or StudioPage.Commands;
        targetLabel.Text = lastAuthoring == StudioPage.Scenario ? "Applies the visual scenario" : "Applies the command list";
        targetLabel.ForeColor = Ui.Subtle;
        TitleChanged?.Invoke();
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Notify?.Invoke(ex.Message, ex is IOException or ArgumentException or InvalidDataException or JsonException ? Tone.Warning : Tone.Danger); }
    }
    private void MarkUnsaved() { MarkDraft(); if (!Unsaved) { Unsaved = true; TitleChanged?.Invoke(); } else TitleChanged?.Invoke(); }
    private void MarkDraft() { draftDirty = true; draftChangedAt = DateTime.UtcNow; }

    // ---------- Commands page ----------
    private Control BuildCommandsPage()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = Ui.S(6), BackColor = Ui.Canvas };
        split.Panel1.BackColor = Ui.Canvas; split.Panel2.BackColor = Ui.Surface;
        var listTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Ui.S(8), Ui.S(8), 0, Ui.S(2)), BackColor = Ui.Surface };
        listTools.Controls.AddRange([
            Ui.Button("+ Block", Tone.Primary, "Add a chain block after the selected one", () => Guard(() => { if (nodes.Count >= 128) throw new IOException("A chain can have at most 128 blocks."); int at = Math.Max(0, steps.SelectedIndex) + 1; nodes.Insert(Math.Min(at, nodes.Count), new ScenarioNode { Name = "Chain block" }); steps.SelectedIndex = Math.Min(at, nodes.Count - 1); Changed(); MarkUnsaved(); })),
            Ui.Button("Duplicate", Tone.Secondary, null, () => Guard(() => { if (steps.SelectedItem is not ScenarioNode n) return; if (nodes.Count >= 128) throw new IOException("A chain can have at most 128 blocks."); nodes.Insert(steps.SelectedIndex + 1, new ScenarioNode { Name = n.Name, Command = n.Command, Conditional = n.Conditional, Explanation = n.Explanation }); steps.SelectedIndex++; Changed(); MarkUnsaved(); })),
            Ui.Button("↑", Tone.Ghost, "Move up", () => MoveStep(-1)), Ui.Button("↓", Tone.Ghost, "Move down", () => MoveStep(1)),
            deleteBlock = Ui.Button("Delete", Tone.Danger, null, () => Guard(() => { if (steps.SelectedIndex < 0) return; if (nodes.Count == 1) throw new IOException("Keep at least one block."); int i = steps.SelectedIndex; nodes.RemoveAt(i); nodeErrors.Clear(); steps.SelectedIndex = Math.Min(i, nodes.Count - 1); Changed(); MarkUnsaved(); })),
            checkBlocks = Ui.Button("Check in game", Tone.Secondary, "Parse every command with the connected 26.2 game without placing anything, and mark the invalid ones.", () => Guard(CheckInGame)),
            Ui.Button("Fix legacy syntax", Tone.Ghost, "Rewrite pre-1.20.5 particle arguments (block, block_marker, falling_dust, item, dust …) to the 26.2 syntax.", FixLegacySyntax)]);
        listTools.SetFlowBreak(deleteBlock, true);
        steps.ItemHeight = Ui.S(52);
        steps.SelectedIndexChanged += (_, _) => ShowNodeError();
        steps.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= nodes.Count) return;
            var n = nodes[e.Index]; bool active = (e.State & DrawItemState.Selected) != 0; var g = e.Graphics;
            using (var back = new SolidBrush(active ? Ui.Hover : Ui.Canvas)) g.FillRectangle(back, e.Bounds);
            bool failed = nodeErrors.TryGetValue(e.Index, out var error);
            if (active || failed) { using var accent = new SolidBrush(failed ? Ui.Danger : Ui.Accent); g.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + Ui.S(6), Ui.S(3), e.Bounds.Height - Ui.S(12)); }
            var kind = e.Index == 0 ? "minecraft:repeating_command_block" : "minecraft:chain_command_block";
            MinecraftAssets.Draw(g, catalog.Assets.Icon("blocks", kind), new Rectangle(e.Bounds.X + Ui.S(10), e.Bounds.Y + Ui.S(10), Ui.S(30), Ui.S(30)));
            int tx = e.Bounds.X + Ui.S(50);
            TextRenderer.DrawText(g, $"{e.Index + 1:00}  {System.Text.RegularExpressions.Regex.Replace(n.Name, @"^\d+\s*·\s*", "")}", Ui.Strong, new Rectangle(tx, e.Bounds.Y + Ui.S(7), e.Bounds.Width - tx - Ui.S(60), Ui.S(20)), Ui.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, failed ? "✕ " + error : n.Command, Ui.Small, new Rectangle(tx, e.Bounds.Y + Ui.S(27), e.Bounds.Width - tx - Ui.S(8), Ui.S(18)), failed ? Ui.Danger : Ui.Subtle, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (n.Conditional) TextRenderer.DrawText(g, "COND", Ui.Small, new Rectangle(e.Bounds.Right - Ui.S(52), e.Bounds.Y + Ui.S(7), Ui.S(46), Ui.S(18)), Ui.Warning, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            using var line = new Pen(Ui.Surface); g.DrawLine(line, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        };
        split.Panel1.Controls.Add(steps); split.Panel1.Controls.Add(listTools);

        var form = new FormFields();
        form.Section("Block");
        form.Row("Name", name);
        command.Height = Ui.S(150); command.WordWrap = true; var commandRow = form.Row("Command", command, "Exact command syntax for Java 26.2, without the leading slash. Block 1 repeats every tick; the rest are chained.");
        commandRow.Input.Height = Ui.S(150);
        form.Custom(conditional);
        form.Row("Explanation", explanation, "Plain-English note for whoever reads this later. Not sent to the game.");
        form.Section("Game read-back");
        var readBack = new Card(); readBack.Height = Ui.S(110); readBack.Controls.Add(feedback); form.Custom(readBack);
        feedback.Text = "Stage or run the list to see each block's success count and output here.";
        split.Panel2.Controls.Add(form);
        Ui.InitialSplit(split, width => Math.Clamp(width * 2 / 5, Ui.S(340), Ui.S(440)));
        return split;
    }
    private void SelectNode()
    {
        if (steps.SelectedItem is not ScenarioNode node) return;
        selecting = true; name.Text = node.Name; command.Text = node.Command; explanation.Text = node.Explanation;
        conditional.Checked = node.Conditional; conditional.Enabled = steps.SelectedIndex > 0; selecting = false;
    }
    private void EditNode()
    {
        if (selecting || steps.SelectedItem is not ScenarioNode node) return;
        if (node.Command != command.Text.TrimStart('/')) nodeErrors.Remove(steps.SelectedIndex);
        node.Name = name.Text; node.Command = command.Text.TrimStart('/'); node.Explanation = explanation.Text; node.Conditional = conditional.Checked;
        steps.Invalidate(); Changed(); MarkUnsaved();
    }
    private void Changed() { dirty = true; changedAt = DateTime.UtcNow; MarkDraft(); }
    private void MoveStep(int delta)
    {
        int index = steps.SelectedIndex, next = index + delta; if (index < 0 || next < 0 || next >= nodes.Count) return;
        var node = nodes[index]; nodes.RemoveAt(index); nodes.Insert(next, node); nodeErrors.Clear(); steps.SelectedIndex = next; Changed(); MarkUnsaved();
    }
    private void ReplaceNodes(IEnumerable<ScenarioNode> replacement)
    { selecting = true; nodes.Clear(); nodeErrors.Clear(); foreach (var node in replacement) nodes.Add(node); selecting = false; steps.SelectedIndex = 0; SelectNode(); }

    private void ShowNodeError()
    {
        if (steps.SelectedIndex >= 0 && nodeErrors.TryGetValue(steps.SelectedIndex, out var error)) { feedback.Text = $"Block {steps.SelectedIndex + 1} does not parse in 26.2:\n{error}"; feedback.ForeColor = Ui.Danger; }
        else feedback.ForeColor = Ui.Muted;
    }
    private void CheckInGame()
    {
        var p = Player(); var values = new JsonArray();
        foreach (var n in nodes) values.Add(new JsonObject { ["command"] = n.Command, ["conditional"] = n.Conditional });
        Connection().Send(new JsonObject { ["operation"] = "validate", ["playerId"] = p["id"]!.GetValue<string>(), ["x"] = (int)x.Value, ["y"] = (int)y.Value, ["z"] = (int)z.Value, ["nodes"] = values });
        awaitingValidation = true; liveMessage.Text = $"Checking {nodes.Count} commands with the game's parser…"; liveMessage.ForeColor = Ui.Muted;
    }
    private void ReadValidation(JsonArray? results)
    {
        nodeErrors.Clear();
        foreach (var r in results ?? [])
            if (r?["ok"]?.GetValue<bool>() == false && r["index"]?.GetValue<int>() is int i && i >= 0 && i < nodes.Count) nodeErrors[i] = r["message"]?.GetValue<string>() ?? "Invalid command";
        steps.Invalidate();
        if (nodeErrors.Count == 0) { Notify?.Invoke($"All {nodes.Count} commands parse in 26.2. Stage or Run will place them.", Tone.Success); return; }
        var list = string.Join(", ", nodeErrors.Keys.Order().Take(12).Select(i => i + 1)) + (nodeErrors.Count > 12 ? ", …" : "");
        Notify?.Invoke($"{nodeErrors.Count} of {nodes.Count} commands do not parse in 26.2 (blocks {list}). They are marked in red; fix them before Stage or Run. Fix legacy syntax handles old particle arguments.", Tone.Warning);
        steps.SelectedIndex = nodeErrors.Keys.Min(); ShowNodeError();
    }
    private void FixLegacySyntax()
    {
        var touched = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var upgraded = LegacySyntax.Upgrade(nodes[i].Command);
            if (upgraded == nodes[i].Command) continue;
            nodes[i].Command = upgraded; nodeErrors.Remove(i); touched.Add(i + 1);
        }
        if (touched.Count == 0) { Notify?.Invoke("No pre-1.20.5 particle syntax found.", Tone.Info); return; }
        SelectNode(); steps.Invalidate(); Changed(); MarkUnsaved();
        Notify?.Invoke($"Rewrote {touched.Count} command(s) to 26.2 particle syntax (blocks {string.Join(", ", touched)}). Use Check in game to confirm.", Tone.Success);
    }

    // ---------- Import page ----------
    private Control BuildImportPage(out Button execute)
    {
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Ui.S(18)) };
        var importCard = new Card("Import a command.txt installer", "Decodes a falling-block / minecart installer into the Commands page. Nothing runs in the game. Setup and cleanup commands are listed below for review; placement context, initialization and old syntax are not converted automatically.");
        var importTools = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        importTools.Controls.AddRange([Ui.Button("Import command.txt…", Tone.Primary, null, () => Guard(ImportCommands)), Ui.Button("Load demo command list", Tone.Secondary, null, () => { live.Checked = false; ReplaceNodes(Scenario.Demo()); dirty = false; Navigate(StudioPage.Commands); TitleChanged?.Invoke(); })]);
        importCard.Controls.Add(importTools); importCard.Height = Ui.S(150);
        var reviewCard = new Card("Installer review", "Commands from the last import that are not command blocks. Review only; never executed automatically.");
        review.ReadOnly = true; review.Dock = DockStyle.Fill; review.WordWrap = true; reviewCard.Controls.Add(review); reviewCard.Controls.SetChildIndex(review, 0); reviewCard.Height = Ui.S(300);
        var setupCard = new Card("Run one setup command", "Runs immediately, once, at your player's position in the connected world. Use it for reviewed setup such as scoreboard objectives.");
        setupCommand.Dock = DockStyle.Fill; setupCard.Controls.Add(setupCommand); setupCard.Controls.SetChildIndex(setupCommand, 0);
        execute = Ui.Button("Execute once in game", Tone.Danger, "Sends this single command to the bridge-enabled world now.", () => Guard(() =>
        {
            var p = Player(); Connection().Send(new JsonObject { ["operation"] = "execute", ["playerId"] = p["id"]!.GetValue<string>(), ["command"] = setupCommand.Text.Trim().TrimStart('/') });
            liveMessage.Text = "Sent the setup command…";
        }));
        var executeLine = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, Ui.S(8), 0, 0) }; executeLine.Controls.Add(execute);
        setupCard.Controls.Add(executeLine); setupCard.Height = Ui.S(230);
        var guide = new Card("How live editing works",
            "1. Launch the bridge-enabled 26.2 instance and open a private singleplayer world.  2. Press Near player and check the +Z strip is empty, loaded air.  " +
            "3. Stage validates and places inactive blocks; Run / restart arms them and resets the clock.  4. Live sync applies edits after 0.7 s.  5. STOP disables and clears owned commands; it does not undo world effects.  " +
            "Installed blocks stop when the world closes; allocate a fresh strip after reopening. Read game blocks pulls edits made with Minecraft's own command-block screen.");
        guide.Height = Ui.S(170); guide.FitText = true;
        stack.Controls.AddRange([importCard, reviewCard, setupCard, guide]);
        stack.ClientSizeChanged += (_, _) => { int w = Math.Max(Ui.S(300), stack.ClientSize.Width - stack.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth); foreach (Control c in stack.Controls) c.Width = w; };
        review.Text = "No installer imported yet.";
        return stack;
    }
    private void ImportCommands()
    {
        using var dialog = new OpenFileDialog { Filter = "Command text (*.txt)|*.txt|All files (*.*)|*.*", Title = "Import command.txt (read only; nothing is executed)" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var result = Scenario.Import(File.ReadAllText(dialog.FileName)); live.Checked = false; ReplaceNodes(result.Nodes);
        review.Text = result.Notice + "\r\n\r\nINSTALLER COMMANDS (review only; never auto-executed)\r\n\r\n" + string.Join("\r\n\r\n", result.InstallerCommands);
        dirty = false; ProjectPath = null; Unsaved = true; Navigate(StudioPage.Commands); MarkDraft();
        int legacy = nodes.Count(n => LegacySyntax.Upgrade(n.Command) != n.Command);
        Notify?.Invoke($"Imported {result.Nodes.Count} command blocks into Commands and {result.InstallerCommands.Count} installer commands into Import review. Nothing was executed." +
            (legacy > 0 ? $" {legacy} use pre-1.20.5 particle syntax: click Fix legacy syntax, then Check in game." : " Use Check in game before Stage or Run."), legacy > 0 ? Tone.Warning : Tone.Success);
    }

    // ---------- Project files ----------
    public void SaveProject(bool saveAs) => Guard(() =>
    {
        bool design = lastAuthoring == StudioPage.Scenario;
        string? path = saveAs ? null : ProjectPath;
        if (path is null)
        {
            using var dialog = new SaveFileDialog { Filter = "Scenario project (*.json)|*.json", FileName = (design ? designer.Project.Name : "command-list") + ".json", Title = design ? "Save scenario" : "Save command list" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.FileName;
        }
        File.WriteAllText(path, design ? JsonSerializer.Serialize(designer.Project, new JsonSerializerOptions { WriteIndented = true }) : Scenario.Export(nodes.ToList()));
        ProjectPath = path; Unsaved = false; MarkDraft(); TitleChanged?.Invoke();
        Notify?.Invoke($"Saved {Path.GetFileName(path)}.", Tone.Success);
    });
    public void OpenProject() => Guard(() =>
    {
        using var dialog = new OpenFileDialog { Filter = "Scenario project (*.json)|*.json", Title = "Open scenario or command list" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var project = JsonNode.Parse(File.ReadAllText(dialog.FileName))!.AsObject();
        if (project["Format"]?.GetValue<string>() == "universal-editor-designer-1")
        {
            live.Checked = false; designer.SetProject(JsonSerializer.Deserialize<DesignerProject>(project.ToJsonString())!); dirty = false;
            ProjectPath = dialog.FileName; Unsaved = false; Navigate(StudioPage.Scenario); MarkDraft(); Notify?.Invoke($"Opened {Path.GetFileName(dialog.FileName)}.", Tone.Success); return;
        }
        if (project["format"]?.GetValue<string>() != "universal-editor-scenario-1" || project["target"]?.GetValue<string>() != "26.2") throw new IOException("Unsupported project format or target.");
        var loaded = JsonSerializer.Deserialize<List<ScenarioNode>>(project["nodes"]!.ToJsonString())!; if (loaded.Count is < 1 or > 128) throw new IOException("Use 1–128 nodes.");
        live.Checked = false; ReplaceNodes(loaded); dirty = false; ProjectPath = dialog.FileName; Unsaved = false; Navigate(StudioPage.Commands); MarkDraft();
        Notify?.Invoke($"Opened {Path.GetFileName(dialog.FileName)} as a command list.", Tone.Success);
    });
    private void RestoreDraft()
    {
        var draft = WorkingDraft.Load(); if (draft is null) return;
        try { if (draft.Scenario is not null) designer.SetProject(draft.Scenario); } catch (ArgumentException) { }
        if (draft.Item is not null) itemDesigner.SetRecipe(draft.Item);
        if (draft.Commands is { Count: >= 1 and <= 128 }) ReplaceNodes(draft.Commands);
        if (draft.Anchor is { Length: 3 }) { x.Value = Math.Clamp(draft.Anchor[0], -30000000, 30000000); y.Value = Math.Clamp(draft.Anchor[1], -30000000, 30000000); z.Value = Math.Clamp(draft.Anchor[2], -30000000, 30000000); }
        ProjectPath = draft.ProjectPath is not null && File.Exists(draft.ProjectPath) ? draft.ProjectPath : null;
        dirty = false; draftDirty = false;
    }
    private void SaveDraft()
    {
        if (!persist) return;
        new WorkingDraft { Scenario = designer.Project, Item = itemDesigner.Recipe, Commands = nodes.ToList(), Anchor = [(int)x.Value, (int)y.Value, (int)z.Value], ProjectPath = ProjectPath }.Save();
        draftDirty = false;
    }

    // Text for Discord Rich Presence. Names typed by the user are included only when names is true.
    public (string Details, string State) PresenceText(bool names)
    {
        static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
        switch (Page)
        {
            case StudioPage.Scenario:
                string cards = Plural(designer.Project.Actions.Count, "action card");
                return ("Designing a scenario", names && designer.Project.Name.Trim().Length > 0 ? $"{designer.Project.Name.Trim()} · {cards}" : cards);
            case StudioPage.Item:
                var r = itemDesigner.Recipe; string item = ResourceNames.Friendly(r.Id);
                string extra = r.Enchantments.Count > 0 ? " · " + Plural(r.Enchantments.Count, "enchantment") : "";
                return ("Crafting an item", (names && r.Name.Trim().Length > 0 ? $"“{r.Name.Trim()}” · {item}" : item) + extra);
            case StudioPage.Commands: return ("Editing a command block chain", Plural(nodes.Count, "command block"));
            default: return ("Reviewing a command.txt import", Plural(nodes.Count, "command block") + " in the draft");
        }
    }

    // ---------- Shortcuts (routed from the main window) ----------
    public bool HandleShortcut(Keys keys, bool typing)
    {
        switch (keys)
        {
            case Keys.F5: if (run.Enabled && liveBar.Visible) run.PerformClick(); return true;
            case Keys.F6: if (stage.Enabled && liveBar.Visible) stage.PerformClick(); return true;
            case Keys.Shift | Keys.F5: if (stop.Enabled) Guard(Stop); return true;
            case Keys.Control | Keys.S: SaveProject(false); return true;
            case Keys.Control | Keys.Shift | Keys.S: SaveProject(true); return true;
            case Keys.Control | Keys.O: OpenProject(); return true;
            case Keys.Control | Keys.Z when Page == StudioPage.Scenario && !typing: designer.Undo(); return true;
            case Keys.Control | Keys.Y when Page == StudioPage.Scenario && !typing: designer.Redo(); return true;
            case Keys.Control | Keys.Shift | Keys.Z when Page == StudioPage.Scenario && !typing: designer.Redo(); return true;
        }
        return false;
    }

    // ---------- Live connection ----------
    private LiveConnection Connection()
    {
        var path = Path.Combine(Path.GetFullPath(gameDirectory()), ".universal-editor");
        if (connection is null || connection.DirectoryPath != path) { connection = new LiveConnection(gameDirectory()); session = ""; lastAck = ""; }
        return connection;
    }
    private JsonObject Player()
    {
        var c = Connection(); if (!c.Ready) throw new IOException("Open a world in the bridge-enabled 26.2 instance first.");
        var players = c.State!["players"]!.AsArray(); if (players.Count != 1) throw new IOException("Use a private singleplayer world with one player."); return players[0]!.AsObject();
    }
    private void Apply(bool run, bool restart = false)
    {
        var p = Player(); var values = new JsonArray(); foreach (var n in UseDesigner ? designer.Compile() : nodes.ToList()) values.Add(new JsonObject { ["command"] = n.Command, ["conditional"] = n.Conditional });
        var request = new JsonObject { ["operation"] = "install", ["playerId"] = p["id"]!.GetValue<string>(), ["x"] = (int)x.Value, ["y"] = (int)y.Value, ["z"] = (int)z.Value, ["running"] = run, ["nodes"] = values };
        if (UseDesigner) { request["timelineObjective"] = designer.Project.Objective; request["resetTimeline"] = restart; }
        Connection().Send(request);
        running = run; awaitingInstall = true; dirty = false; liveMessage.Text = "Waiting for the game to validate and read back…"; liveMessage.ForeColor = Ui.Muted;
    }
    private void Stop()
    { live.Checked = false; dirty = false; Connection().Send(new JsonObject { ["operation"] = "stop" }); running = false; expectedCommands = ""; liveMessage.Text = "STOP sent. Owned blocks are being disabled and cleared."; }
    public void Shutdown()
    {
        try { if (connection?.Ready == true) connection.Send(new JsonObject { ["operation"] = "stop" }); } catch (IOException) { }
        SaveDraft();
    }
    public void InitializeView() { if (nodes.Count > 0) { steps.SelectedIndex = 0; SelectNode(); } }
    internal void ShowItemProof() { Navigate(StudioPage.Item); itemDesigner.ShowItemProof(); }
    private void ReadBlocks()
    {
        var c = Connection(); if (!c.Ready) throw new IOException("The live bridge is offline.");
        var blocks = c.State!["blocks"]!.AsArray(); if (blocks.Count == 0) throw new IOException("No scenario is installed in this world session.");
        if (blocks.Any(b => b?["owned"]?.GetValue<bool>() != true)) throw new IOException("An installed block was replaced. Reopen the world and allocate a new region.");
        live.Checked = false;
        ReplaceNodes(blocks.Select((b, i) => new ScenarioNode { Name = $"Game block {i + 1}", Command = b!["command"]!.GetValue<string>(), Conditional = b["conditional"]!.GetValue<bool>() }));
        x.Value = blocks[0]!["x"]!.GetValue<int>(); y.Value = blocks[0]!["y"]!.GetValue<int>(); z.Value = blocks[0]!["z"]!.GetValue<int>();
        expectedCommands = BlockFingerprint(blocks); running = blocks[0]!["automatic"]!.GetValue<bool>(); dirty = false;
        Navigate(StudioPage.Commands); MarkDraft();
        Notify?.Invoke($"Read {blocks.Count} blocks from the game into Commands.", Tone.Success);
    }
    private static string BlockFingerprint(JsonArray blocks) => string.Join("\n", blocks.Select(b => b?["command"]?.GetValue<string>() + "|" + b?["conditional"]?.ToJsonString() + "|" + b?["automatic"]?.ToJsonString()));
    private void UpdateLiveControls(bool ready, bool pending)
    {
        stage.Enabled = run.Enabled = near.Enabled = readBlocks.Enabled = ready && !pending; stop.Enabled = ready;
        executeSetup.Enabled = ready && !pending; itemDesigner.Online = ready && !pending; checkBlocks.Enabled = ready && !pending;
    }
    private void Status(LiveStatus status)
    {
        LiveOnline = status.Online;
        string key = status.Online + status.Title + status.Detail; if (key == lastStatus) return; lastStatus = key; StatusChanged?.Invoke(status);
    }
    private void Poll()
    {
        try
        {
            if (draftDirty && (DateTime.UtcNow - draftChangedAt).TotalSeconds >= 2) SaveDraft();
            var c = Connection(); c.Poll();
            var prismRoot = Directory.GetParent(gameDirectory())?.Parent?.Parent?.FullName;
            if (prismRoot is not null && catalog.Assets.Open(prismRoot)) { catalog.LoadVisualChoices(); itemDesigner.RefreshCatalog(); designer.RefreshCatalog(); }
            if (!c.Ready)
            {
                Status(new(false, "Live · offline", "Live editing needs the bridge-enabled Java 26.2 instance with a singleplayer world open. Drafts stay editable offline."));
                if (live.Checked) live.Checked = false;
                UpdateLiveControls(false, false);
                if (liveMessage.Text.Length == 0 || !liveMessage.Text.StartsWith("Offline", StringComparison.Ordinal)) { liveMessage.Text = "Offline · launch 26.2 and open a world to stage or run."; liveMessage.ForeColor = Ui.Subtle; }
                return;
            }
            var state = c.State!; string currentSession = state["session"]!.GetValue<string>();
            if (session != currentSession) { session = currentSession; expectedCommands = ""; live.Checked = false; running = false; anchorPending = true; liveMessage.Text = state["message"]?.GetValue<string>() ?? "Connected."; liveMessage.ForeColor = Ui.Muted; }
            if (catalogSession != currentSession) { catalog.Load(gameDirectory()); if (catalog.Exact) { catalogSession = currentSession; itemDesigner.RefreshCatalog(); designer.RefreshCatalog(); } }
            var p = state["players"]!.AsArray().FirstOrDefault();
            // New world session with nothing installed: put an unset or far-away anchor next to the player.
            if (anchorPending && p is not null && state["blocks"]!.AsArray().Count == 0)
            {
                anchorPending = false;
                int px = p["x"]!.GetValue<int>(), py = p["y"]!.GetValue<int>(), pz = p["z"]!.GetValue<int>();
                long dx = (long)x.Value - px, dy = (long)y.Value - py, dz = (long)z.Value - pz;
                if ((x.Value == 0 && y.Value == 0 && z.Value == 0) || dx * dx + dy * dy + dz * dz > 48L * 48)
                {
                    x.Value = px + 4; y.Value = py + 1; z.Value = pz + 4; MarkDraft();
                    Notify?.Invoke($"Anchor moved next to you ({px + 4}, {py + 1}, {pz + 4}). Check that the +Z strip is empty air before staging.", Tone.Info);
                }
            }
            bool paused = state["paused"]!.GetValue<bool>();
            Status(new(true, paused ? "Live · paused" : "Live · connected",
                $"Java 26.2 · {(paused ? "world paused" : "world ticking")}\n{p?["name"]?.GetValue<string>() ?? "Waiting for player"} · {p?["dimension"]?.GetValue<string>()}\n{state["message"]!.GetValue<string>()}"));
            UpdateLiveControls(true, c.PendingId is not null);
            if (p is not null) itemDesigner.Observe(p);
            var blocks = state["blocks"]!.AsArray(); string ack = state["requestId"]!.GetValue<string>();
            if (ack.Length > 0 && ack != lastAck)
            {
                lastAck = ack; string message = state["message"]!.GetValue<string>(); bool accepted = state["accepted"]!.GetValue<bool>();
                liveMessage.Text = (accepted ? "✓ " : "✕ ") + message; liveMessage.ForeColor = accepted ? Ui.Accent : Ui.Danger;
                if (awaitingValidation) { awaitingValidation = false; if (accepted) ReadValidation(state["validation"] as JsonArray); }
                if (!accepted)
                {
                    live.Checked = false; running = state["armed"]?.GetValue<bool>() ?? false;
                    var block = System.Text.RegularExpressions.Regex.Match(message, @"\ABlock (\d+): (.*)\z", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (block.Success && !UseDesigner && int.TryParse(block.Groups[1].Value, out var number) && number >= 1 && number <= nodes.Count)
                    {
                        nodeErrors[number - 1] = block.Groups[2].Value; steps.Invalidate(); steps.SelectedIndex = number - 1; ShowNodeError();
                        Notify?.Invoke($"Nothing was placed: block {number} does not parse in 26.2 ({block.Groups[2].Value}). Use Check in game to find every invalid block.", Tone.Danger);
                    }
                    else Notify?.Invoke("The game rejected the request: " + message, Tone.Danger);
                }
                if (awaitingInstall) { expectedCommands = BlockFingerprint(blocks); awaitingInstall = false; }
            }
            if (c.PendingId is null && expectedCommands.Length > 0 && expectedCommands != BlockFingerprint(blocks))
            {
                live.Checked = false; dirty = false; expectedCommands = "";
                Notify?.Invoke("Command blocks were changed inside the game. Use Read game blocks to pull them in before syncing again.", Tone.Warning);
            }
            if (lastAuthoring == StudioPage.Commands && steps.SelectedIndex >= 0 && steps.SelectedIndex < blocks.Count && !dirty && c.PendingId is null)
            {
                var b = blocks[steps.SelectedIndex]!;
                if (b["owned"]?.GetValue<bool>() == true) feedback.Text = $"Block {steps.SelectedIndex + 1} at {b["x"]}, {b["y"]}, {b["z"]}\nSuccess count: {b["success"]}\n{b["output"]?.GetValue<string>()}";
            }
            if (live.Checked && dirty && c.PendingId is null && (DateTime.UtcNow - changedAt).TotalMilliseconds >= 700)
            { try { Apply(running); } catch (Exception ex) { live.Checked = false; Notify?.Invoke(ex.Message, Tone.Warning); } }
        }
        catch (Exception ex) { live.Checked = false; Status(new(false, "Live · error", "Bridge state error: " + ex.Message)); }
    }
}
