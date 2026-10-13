using System.Text.Json;
using System.Text.Json.Nodes;
using System.ComponentModel;
using System.Globalization;

namespace UniversalEditor;

// A labelled input: caption above, control below, optional wrapped hint underneath.
internal sealed class FieldRow : Panel
{
    private readonly Label caption;
    private readonly Label? help;
    public Control Input { get; }
    public FieldRow(string label, Control input, string? hint = null)
    {
        Input = input; Margin = new Padding(0, 0, 0, Ui.S(12));
        caption = new Label { Text = label, Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Label, ForeColor = Ui.Muted, UseMnemonic = false, AutoEllipsis = true };
        input.Dock = DockStyle.Top;
        if (input is TextBox { Multiline: true }) input.Height = Ui.S(84);
        if (hint is not null) Controls.Add(help = new Label { Text = hint, Dock = DockStyle.Top, Font = Ui.Small, ForeColor = Ui.Subtle, UseMnemonic = false, Padding = new Padding(0, Ui.S(4), 0, 0) });
        Controls.Add(input); Controls.Add(caption);
        Fit();
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Fit(); }
    private void Fit()
    {
        int helpHeight = help is null ? 0 : TextRenderer.MeasureText(help.Text, help.Font, new Size(Math.Max(60, Width), 0), TextFormatFlags.WordBreak).Height + Ui.S(6);
        if (help is not null && help.Height != helpHeight) help.Height = helpHeight;
        int total = caption.Height + Input.Height + helpHeight;
        if (Height != total) Height = total;
    }
}

// Vertical property form. Every builder method adds one full-width row.
internal sealed class FormFields : FlowLayoutPanel
{
    public FormFields()
    {
        Dock = DockStyle.Fill; AutoScroll = true; FlowDirection = FlowDirection.TopDown; WrapContents = false;
        Padding = new Padding(Ui.S(18), Ui.S(6), Ui.S(18), Ui.S(24)); Ui.DarkNative(this);
    }
    private int RowWidth => Math.Max(Ui.S(220), ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
    protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); SuspendLayout(); foreach (Control c in Controls) Fit(c); ResumeLayout(true); }
    private void Fit(Control c)
    {
        switch (c)
        {
            case Label { AutoSize: true } label: label.MaximumSize = new Size(RowWidth, 0); break;
            case FlowLayoutPanel { AutoSize: true } line: line.MaximumSize = new Size(RowWidth, 0); break;
            case CheckBox or System.Windows.Forms.Button: break;
            default: c.Width = RowWidth; break;
        }
    }
    private T Add<T>(T control) where T : Control { Controls.Add(control); Fit(control); return control; }
    private static string DefaultDisplay(string id) => id.StartsWith("minecraft:", StringComparison.Ordinal) ? ResourceNames.Friendly(id) : id;

    public void Custom(Control control) { control.Margin = new Padding(0, 0, 0, Ui.S(8)); Add(control); }
    public void Section(string title)
    {
        var label = new Label
        {
            Text = title.ToUpperInvariant(), AutoSize = false, Height = Ui.S(34), Font = Ui.Label, ForeColor = Ui.Accent, TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(0, 0, 0, Ui.S(6)), Margin = new Padding(0, Controls.Count == 0 ? 0 : Ui.S(10), 0, Ui.S(12)), UseMnemonic = false
        };
        label.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, label.Height - 1, label.Width, label.Height - 1); };
        Add(label);
    }
    public void Note(string text, Tone tone = Tone.Muted) =>
        Add(new Label { Text = text, AutoSize = true, Font = Ui.Small, ForeColor = tone == Tone.Muted ? Ui.Subtle : Ui.ToneColor(tone), Margin = new Padding(0, 0, 0, Ui.S(10)), UseMnemonic = false });
    public FieldRow Row(string label, Control input, string? hint = null) => Add(new FieldRow(label, input, hint));
    public void ResourceChoice(string label, GameCatalog catalog, string category, string selected, Action<string> change, string? hint = null)
    {
        var picker = new ResourcePicker(catalog.Assets, category, catalog.Get(category), selected, change) { Height = Ui.S(300) };
        Row(label, picker, hint);
    }
    public TextBox TextField(string label, string value, Action<string> change, bool multiline = false, string? hint = null, string? placeholder = null, bool mono = false)
    {
        var text = Ui.TextInput(value, multiline, mono); if (placeholder is not null) text.PlaceholderText = placeholder;
        Row(label, text, hint); text.TextChanged += (_, _) => change(text.Text); return text;
    }
    public NumericUpDown Number(string label, decimal value, decimal min, decimal max, Action<decimal> change, int decimals = 0, string? hint = null)
    {
        var input = Ui.Number(min, max, value, decimals); Row(label, input, hint); input.ValueChanged += (_, _) => change(input.Value); return input;
    }
    public CheckBox Toggle(string label, bool value, Action<bool> change, string? hint = null)
    {
        var input = new CheckBox { Text = label, Checked = value, AutoSize = true, Font = Ui.Body, ForeColor = Ui.Text, Margin = new Padding(0, 0, 0, Ui.S(hint is null ? 10 : 2)), UseMnemonic = false };
        Add(input); if (hint is not null) Note(hint);
        input.CheckedChanged += (_, _) => change(input.Checked); return input;
    }
    public DarkCombo Choice(string label, IEnumerable<string> choices, string selected, Action<string> change, bool search = false,
        Func<string, string>? display = null, Func<string, Color?>? swatch = null, Func<string, Image?>? icon = null, string? hint = null)
    {
        var values = choices.Distinct().ToArray(); bool filtering = false;
        var combo = new DarkCombo { FormattingEnabled = true, DropDownWidth = Ui.S(360) };
        var shown = display ?? DefaultDisplay;
        combo.Format += (_, e) => { if (e.ListItem is string id) e.Value = shown(id); };
        if (swatch is not null) combo.SwatchFor = o => o is string s ? swatch(s) : null;
        if (icon is not null) combo.IconFor = o => o is string s ? icon(s) : null;
        combo.Items.AddRange(values.Cast<object>().ToArray()); if (!combo.Items.Contains(selected)) combo.Items.Add(selected); combo.SelectedItem = selected;
        Control input = combo;
        if (search)
        {
            var filter = Ui.TextInput(); filter.PlaceholderText = "Filter…"; filter.Dock = DockStyle.Right; filter.Width = Ui.S(130);
            var line = new Panel { Height = combo.Height }; combo.Dock = DockStyle.Fill;
            line.Controls.Add(combo); line.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(6) }); line.Controls.Add(filter); input = line;
            filter.TextChanged += (_, _) =>
            {
                var current = combo.SelectedItem as string; filtering = true; combo.BeginUpdate(); combo.Items.Clear();
                string query = filter.Text.Replace('_', ' ');
                combo.Items.AddRange(values.Where(v => v.Replace('_', ' ').Contains(query, StringComparison.OrdinalIgnoreCase) || shown(v).Contains(query, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
                if (current is not null) { if (!combo.Items.Contains(current)) combo.Items.Insert(0, current); combo.SelectedItem = current; }
                combo.EndUpdate(); filtering = false;
            };
        }
        Row(label, input, hint);
        combo.SelectedIndexChanged += (_, _) => { if (!filtering && combo.SelectedItem is string value) change(value); };
        return combo;
    }
    public void Vector(string label, (decimal X, decimal Y, decimal Z) value, Action<decimal, decimal, decimal> change, decimal horizontal = 30000000, decimal vertical = 2048, string? hint = null)
    {
        var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Height = Ui.S(28), Margin = Padding.Empty, Padding = Padding.Empty };
        var inputs = new NumericUpDown[3]; var axes = new[] { ("X", Color.FromArgb(236, 104, 104)), ("Y", Color.FromArgb(112, 214, 118)), ("Z", Color.FromArgb(108, 156, 246)) };
        decimal[] values = [value.X, value.Y, value.Z];
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i < 2 ? Ui.S(8) : 0, 0) };
            decimal limit = i == 1 ? vertical : horizontal;
            inputs[i] = Ui.Number(-limit, limit, values[i], 2); inputs[i].Dock = DockStyle.Fill;
            cell.Controls.Add(inputs[i]); cell.Controls.Add(new Label { Text = axes[i].Item1, Dock = DockStyle.Left, Width = Ui.S(18), Font = Ui.Strong, ForeColor = axes[i].Item2, TextAlign = ContentAlignment.MiddleLeft });
            grid.Controls.Add(cell, i, 0);
        }
        foreach (var input in inputs) input.ValueChanged += (_, _) => change(inputs[0].Value, inputs[1].Value, inputs[2].Value);
        Row(label, grid, hint);
    }
    // A full-width input followed by fixed-width trailing controls on the same line.
    public void Line(string label, Control main, params Control[] trailing)
    {
        var line = new Panel { Height = Math.Max(Ui.S(30), main.Height) }; main.Dock = DockStyle.Fill; line.Controls.Add(main);
        foreach (var control in trailing) { control.Dock = DockStyle.Right; line.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(6) }); line.Controls.Add(control); }
        Row(label, line);
    }
    public Button Button(string label, Action action, Tone tone = Tone.Secondary, string? tip = null) => Add(Ui.Button(label, tone, tip, action));
    public void Buttons(params (string Text, Tone Tone, Action Action)[] buttons)
    {
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, Ui.S(6), 0, Ui.S(8)) };
        foreach (var (text, tone, action) in buttons) line.Controls.Add(Ui.Button(text, tone, null, action));
        Add(line);
    }
}

// Icon + title + subtitle header used at the top of property panels.
internal sealed class IconHeader : Control
{
    private readonly Func<Image?> icon;
    private readonly Func<string> subtitle;
    private readonly Color accent;
    public IconHeader(Func<Image?> icon, string title, Func<string> subtitle, Color accent)
    { this.icon = icon; this.subtitle = subtitle; this.accent = accent; Text = title; Height = Ui.S(64); DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Ui.Surface);
        var tile = new Rectangle(0, Ui.S(8), Ui.S(48), Ui.S(48));
        using (var fill = new SolidBrush(Color.FromArgb(48, accent))) g.FillRectangle(fill, tile);
        using (var border = new Pen(Color.FromArgb(140, accent))) g.DrawRectangle(border, tile);
        MinecraftAssets.Draw(g, icon(), Rectangle.Inflate(tile, -Ui.S(8), -Ui.S(8)));
        TextRenderer.DrawText(g, Text, Ui.Heading, new Rectangle(tile.Right + Ui.S(14), Ui.S(10), Width - tile.Right - Ui.S(14), Ui.S(24)), Ui.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, subtitle(), Ui.Body, new Rectangle(tile.Right + Ui.S(14), Ui.S(34), Width - tile.Right - Ui.S(14), Ui.S(20)), Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

internal static class ActionText
{
    public static string Name(ActionKind kind) => kind switch
    {
        ActionKind.Message => "Message", ActionKind.Particle => "Particles", ActionKind.Effect => "Status effect", ActionKind.Summon => "Summon mob",
        ActionKind.GiveItem => "Give item", ActionKind.Teleport => "Teleport", ActionKind.Sound => "Play sound", ActionKind.SetBlock => "Place block",
        ActionKind.Time => "Set time", ActionKind.Weather => "Set weather", ActionKind.Variable => "Variable", ActionKind.Tag => "Player tag", _ => kind.ToString()
    };
    public static string Describe(ActionKind kind) => kind switch
    {
        ActionKind.Message => "Show a title, action-bar or chat message", ActionKind.Particle => "Spawn particles around players", ActionKind.Effect => "Give a status effect",
        ActionKind.Summon => "Spawn a mob near players", ActionKind.GiveItem => "Give a designed item", ActionKind.Teleport => "Move players to a position",
        ActionKind.Sound => "Play a sound at players", ActionKind.SetBlock => "Place a block relative to players", ActionKind.Time => "Change the time of day",
        ActionKind.Weather => "Change the weather", ActionKind.Variable => "Set or change a scenario variable", ActionKind.Tag => "Add or remove a player tag", _ => ""
    };
    public static string Group(ActionKind kind) => kind switch
    {
        ActionKind.Message or ActionKind.Particle or ActionKind.Sound => "Show & sound",
        ActionKind.Effect or ActionKind.GiveItem or ActionKind.Teleport or ActionKind.Tag => "Players",
        ActionKind.Summon or ActionKind.SetBlock or ActionKind.Time or ActionKind.Weather => "World",
        _ => "Logic"
    };
    public static Color Hue(ActionKind kind) => Group(kind) switch
    {
        "Show & sound" => Ui.Info, "Players" => Color.FromArgb(178, 132, 246), "World" => Color.FromArgb(104, 200, 120), _ => Ui.Warning
    };
    public static string Roman(int level) => level is >= 1 and <= 10 ? new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" }[level - 1] : level.ToString(CultureInfo.InvariantCulture);
    private static string Title(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);
    public static string Summary(ActionSpec a) => a.Kind switch
    {
        ActionKind.Message => $"“{a.Text}” · {a.Presentation.ToLowerInvariant()}",
        ActionKind.Particle => $"{ResourceNames.Friendly(a.Resource)} × {a.Count}",
        ActionKind.Effect => $"{ResourceNames.Friendly(a.Resource)} {Roman(a.EffectLevel)} · {a.DurationSeconds}s",
        ActionKind.Summon => ResourceNames.Friendly(a.Resource) + (a.MobName.Length > 0 ? $" “{a.MobName}”" : ""),
        ActionKind.GiveItem => $"{(a.Item.Name.Length > 0 ? a.Item.Name : ResourceNames.Friendly(a.Item.Id))} × {a.Item.Count}",
        ActionKind.Teleport => $"to {a.X:0.##} {a.Y:0.##} {a.Z:0.##}",
        ActionKind.Sound => a.Resource.Replace("minecraft:", ""),
        ActionKind.SetBlock => $"{ResourceNames.Friendly(a.Resource)} at ~{a.X:0.##} ~{a.Y:0.##} ~{a.Z:0.##}",
        ActionKind.Time or ActionKind.Weather => Title(a.Option),
        ActionKind.Variable => $"{a.VariableName} {a.VariableOperation switch { "Add" => "+=", "Subtract" => "−=", _ => "=" }} {a.VariableValue}",
        ActionKind.Tag => $"{a.Option} tag “{a.Text}”",
        _ => ""
    };
    public static string Comparison(string value) => value switch { "Not equal" => "≠", "At least" => "≥", "At most" => "≤", _ => "=" };
    public static IEnumerable<string> Chips(ActionSpec a)
    {
        yield return a.When();
        if (a.EndSeconds is not null && a.Timing is TimingKind.EveryTick or TimingKind.EveryInterval) yield return $"until {a.EndSeconds:0.##}s";
        if (a.Kind != ActionKind.Variable && a.Target == "Nearest player") yield return "nearest player";
        if (a.Kind != ActionKind.Variable && a.RequiredTag.Length > 0) yield return (a.UnlessTag ? "without tag " : "tag ") + a.RequiredTag;
        if (a.ConditionVariable.Length > 0) yield return $"if {a.ConditionVariable} {Comparison(a.Comparison)} {a.ConditionValue}";
    }
    public static string Gutter(ActionSpec a) => a.Timing switch
    {
        TimingKind.OnStart => "0s", TimingKind.OnceAfterDelay => $"{a.DelaySeconds:0.##}s", TimingKind.EveryTick => "↻ tick", _ => $"↻ {a.IntervalSeconds:0.##}s"
    };
    public static readonly Dictionary<string, string> TimingNames = new()
    {
        ["On start"] = "Runs once when the scenario starts", ["Once after delay"] = "Runs once after a delay",
        ["Every game tick"] = "Repeats 20 times per second", ["Every interval"] = "Repeats on a fixed interval"
    };
}

internal static class McColors
{
    public static readonly Dictionary<string, Color> Text = new()
    {
        ["black"] = Color.FromArgb(0, 0, 0), ["dark_blue"] = Color.FromArgb(0, 0, 170), ["dark_green"] = Color.FromArgb(0, 170, 0), ["dark_aqua"] = Color.FromArgb(0, 170, 170),
        ["dark_red"] = Color.FromArgb(170, 0, 0), ["dark_purple"] = Color.FromArgb(170, 0, 170), ["gold"] = Color.FromArgb(255, 170, 0), ["gray"] = Color.FromArgb(170, 170, 170),
        ["dark_gray"] = Color.FromArgb(85, 85, 85), ["blue"] = Color.FromArgb(85, 85, 255), ["green"] = Color.FromArgb(85, 255, 85), ["aqua"] = Color.FromArgb(85, 255, 255),
        ["red"] = Color.FromArgb(255, 85, 85), ["light_purple"] = Color.FromArgb(255, 85, 255), ["yellow"] = Color.FromArgb(255, 255, 85), ["white"] = Color.FromArgb(255, 255, 255)
    };
    public static Color Of(string name) => Text.TryGetValue(name, out var color) ? color : Color.White;
    public static Color? Swatch(string name) => Text.TryGetValue(name, out var color) ? color : null;
    public static string Display(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));
}

internal sealed class ItemDesigner : UserControl
{
    private readonly GameCatalog catalog;
    private readonly TabbedPanel tabs = new();
    private readonly Panel appearanceHost = new(), enchantHost = new(), attributeHost = new();
    private readonly TextBox generated = Ui.TextInput("", true, true);
    private readonly Label observed = new() { Dock = DockStyle.Top, Height = Ui.S(70), Font = Ui.Body, ForeColor = Ui.Muted, UseMnemonic = false };
    private readonly TextBox observedRaw = Ui.TextInput("", true, true);
    private readonly Button apply, read, copy;
    private JsonObject? actual;
    private string actualSnbt = "";
    private bool online;
    private readonly ItemVisualPreview visual;
    public ItemRecipe Recipe { get; private set; } = new();
    public event Action? Edited;
    public event Action<ItemRecipe>? ApplyRequested;
    public event Action<string, Tone>? Message;
    public ItemDesigner(GameCatalog catalog)
    {
        this.catalog = catalog; Dock = DockStyle.Fill; visual = new ItemVisualPreview(catalog.Assets, () => Recipe) { Dock = DockStyle.Fill };
        generated.ReadOnly = observedRaw.ReadOnly = true; generated.Dock = observedRaw.Dock = DockStyle.Fill; generated.WordWrap = observedRaw.WordWrap = true;
        apply = Ui.Button("Apply to held item", Tone.Primary, "Replace the item in your main hand with this design (bridge-enabled 26.2 world).", () => ApplyRequested?.Invoke(Recipe));
        read = Ui.Button("Load held item", Tone.Secondary, "Load the item you are holding in game into this editor. Unmapped components are preserved.", LoadHeld);
        copy = Ui.Button("Copy /give command", Tone.Ghost, "Copy a /give command for this item to the clipboard.", CopyCommand);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Ui.S(200), FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, Ui.S(14), Ui.S(14), 0) };
        foreach (var b in new[] { apply, read, copy }) { b.AutoSize = false; b.Width = Ui.S(184); b.Height = Ui.S(34); actions.Controls.Add(b); }
        var header = new Panel { Dock = DockStyle.Top, Height = Ui.S(176), BackColor = Ui.Surface };
        header.Controls.Add(visual); header.Controls.Add(actions);
        foreach (var host in new[] { appearanceHost, enchantHost, attributeHost }) host.Dock = DockStyle.Fill;
        tabs.Add("Appearance", appearanceHost); tabs.Add("Enchantments", enchantHost); tabs.Add("Attributes", attributeHost);
        var game = new Panel { Padding = new Padding(Ui.S(18), Ui.S(12), Ui.S(18), Ui.S(18)) };
        var reload = Ui.Button("Load this item into the editor", Tone.Secondary, null, LoadHeld); reload.Dock = DockStyle.Top;
        game.Controls.Add(observedRaw); game.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(10) }); game.Controls.Add(reload); game.Controls.Add(observed);
        tabs.Add("In game", game);
        var command = new Panel { Padding = new Padding(Ui.S(18), Ui.S(12), Ui.S(18), Ui.S(18)) };
        command.Controls.Add(generated);
        command.Controls.Add(new Label { Text = "Generated item argument. Read-only: edit the forms instead. The game's own parser validates it when applied.", Dock = DockStyle.Top, Height = Ui.S(40), ForeColor = Ui.Muted, UseMnemonic = false });
        tabs.Add("Command", command);
        Controls.Add(tabs); Controls.Add(header);
        observed.Text = "Not connected. Open a world in the bridge-enabled 26.2 instance to see the item you are holding.";
        Online = false; Rebuild();
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Online { get => online; set { online = value; apply.Enabled = read.Enabled = value; } }
    public void SetRecipe(ItemRecipe recipe) { Recipe = recipe; Rebuild(); }
    private void LoadHeld()
    {
        if (actual is null || actual["id"]?.GetValue<string>() == "minecraft:air") { Message?.Invoke("Hold an item in your main hand in game, then try again.", Tone.Warning); return; }
        try { Recipe = ItemRecipe.Read(actual, actualSnbt); Rebuild(); Change(); tabs.Selected = 0; Message?.Invoke($"Loaded {ResourceNames.Friendly(Recipe.Id)} from your hand. Changes apply only when you click Apply.", Tone.Success); }
        catch (Exception e) { Message?.Invoke(e.Message, Tone.Danger); }
    }
    private void CopyCommand()
    {
        try { Clipboard.SetText("/give @s " + Recipe.Argument() + " " + Recipe.Count); Message?.Invoke("Copied the /give command to the clipboard.", Tone.Success); }
        catch (Exception e) { Message?.Invoke(e.Message, Tone.Danger); }
    }
    public void Observe(JsonNode player)
    {
        string id = player["itemId"]?.GetValue<string>() ?? "minecraft:air";
        observed.Text = id == "minecraft:air" ? "Your main hand is empty." : $"Holding {ResourceNames.Friendly(id)} × {player["count"]}  ·  {id}\nDraft edits never replace this item until you click Apply.";
        observedRaw.Text = player["itemSnbt"]?.GetValue<string>() ?? "";
        actualSnbt = observedRaw.Text;
        if (player["itemJson"] is JsonObject data) actual = (JsonObject)data.DeepClone();
    }
    public void RefreshCatalog() => Rebuild();
    internal void ShowItemProof()
    { if (actual is not null && actual["id"]?.GetValue<string>() != "minecraft:air") { Recipe = ItemRecipe.Read(actual, actualSnbt); Rebuild(); } tabs.Selected = 1; }
    private static void Host(Panel host, FormFields fields)
    {
        int scroll = host.Controls.Count > 0 && host.Controls[0] is FormFields old ? -old.AutoScrollPosition.Y : 0;
        host.SuspendLayout();
        foreach (Control c in host.Controls.Cast<Control>().ToArray()) { host.Controls.Remove(c); c.Dispose(); }
        host.Controls.Add(fields); host.ResumeLayout(true); Ui.Apply(fields);
        if (scroll > 0) fields.AutoScrollPosition = new Point(0, scroll);
    }
    private void Rebuild()
    {
        var f = new FormFields();
        f.Section("Item");
        f.ResourceChoice("Item", catalog, "items", Recipe.Id, v => { Recipe.Id = v; Change(); }, "Click a slot, search by name or ID, or use the arrow keys.");
        f.Number("Count", Recipe.Count, 1, 64, v => { Recipe.Count = (int)v; Change(); });
        f.Section("Name & description");
        f.TextField("Display name", Recipe.Name, v => { Recipe.Name = v; Change(); }, placeholder: "Leave empty to keep the default name");
        f.Choice("Text color", Authoring.Colors, Recipe.Color, v => { Recipe.Color = v; Change(); }, display: McColors.Display, swatch: McColors.Swatch);
        f.Toggle("Bold", Recipe.Bold, v => { Recipe.Bold = v; Change(); }); f.Toggle("Italic", Recipe.Italic, v => { Recipe.Italic = v; Change(); });
        f.TextField("Lore", Recipe.Lore, v => { Recipe.Lore = v; Change(); }, true, "One tooltip line per row. Uses the same color and style as the name.");
        f.Section("Behaviour");
        f.Toggle("Unbreakable", Recipe.Unbreakable, v => { Recipe.Unbreakable = v; Change(); });
        f.Choice("Enchantment glint", ["Game default", "Always glow", "Never glow"], Recipe.Glint is null ? "Game default" : Recipe.Glint == true ? "Always glow" : "Never glow",
            v => { Recipe.Glint = v == "Game default" ? null : v == "Always glow"; Change(); });
        f.Toggle("Set durability damage", Recipe.Damage is not null, v => { Recipe.Damage = v ? 0 : null; Rebuild(); Change(); });
        if (Recipe.Damage is not null) f.Number("Damage taken", Recipe.Damage.Value, 0, int.MaxValue, v => { Recipe.Damage = (int)v; Change(); }, hint: "0 is a brand-new item. Higher values show a lower durability bar.");
        if (Recipe.Preserved.Count > 0) { f.Section("Preserved from game"); f.Note($"{Recipe.Preserved.Count} component(s) without a form are kept exactly as read: " + string.Join(", ", Recipe.Preserved.Select(p => p.Key.Replace("minecraft:", "")))); }
        Host(appearanceHost, f);

        var enchant = new FormFields(); enchant.Section($"Enchantments · {Recipe.Enchantments.Count}");
        if (Recipe.Enchantments.Count == 0) enchant.Note("No enchantments. Add one below; the game validates whether it applies to this item.");
        int index = 0;
        foreach (var entry in Recipe.Enchantments.ToArray())
        {
            var choice = new DarkCombo { FormattingEnabled = true, DropDownWidth = Ui.S(300) };
            choice.Format += (_, e) => { if (e.ListItem is string id) e.Value = ResourceNames.Friendly(id); };
            choice.Items.AddRange(catalog.Get("enchantments").Cast<object>().ToArray()); if (!choice.Items.Contains(entry.Id)) choice.Items.Add(entry.Id); choice.SelectedItem = entry.Id;
            choice.SelectedIndexChanged += (_, _) => { if (choice.SelectedItem is string v) { entry.Id = v; Change(); } };
            var level = Ui.Number(1, 255, entry.Level); level.Width = Ui.S(76); level.ValueChanged += (_, _) => { entry.Level = (int)level.Value; Change(); };
            var remove = Ui.Button("✕", Tone.Ghost, "Remove this enchantment", () => { Recipe.Enchantments.Remove(entry); Rebuild(); Change(); }); remove.AutoSize = false; remove.Width = Ui.S(34);
            enchant.Line($"Enchantment {++index} · level", choice, level, remove);
        }
        enchant.Button("+ Add enchantment", () =>
        {
            Recipe.Enchantments.Add(new() { Id = catalog.Get("enchantments").FirstOrDefault(id => Recipe.Enchantments.All(e => e.Id != id)) ?? "minecraft:unbreaking", Level = 1 });
            Rebuild(); Change();
        }, Tone.Primary);
        Host(enchantHost, enchant);

        var attributes = new FormFields(); attributes.Section($"Attribute modifiers · {Recipe.Attributes.Count}");
        attributes.Note("Adding modifiers replaces the item's default modifiers (for example a sword's base attack damage).");
        index = 0;
        foreach (var entry in Recipe.Attributes.ToArray())
        {
            attributes.Section($"Modifier {++index}");
            attributes.Choice("Attribute", catalog.Get("attributes"), entry.Type, v => { entry.Type = v; Change(); }, true);
            attributes.Number("Amount", entry.Amount, -1000000, 1000000, v => { entry.Amount = v; Change(); }, 3);
            attributes.Choice("Operation", ["add_value", "add_multiplied_base", "add_multiplied_total"], entry.Operation, v => { entry.Operation = v; Change(); },
                display: v => v switch { "add_value" => "Add amount", "add_multiplied_base" => "Multiply base value (+amount × base)", _ => "Multiply total (× 1 + amount)" });
            attributes.Choice("Active when in", ["any", "mainhand", "offhand", "hand", "head", "chest", "legs", "feet", "armor", "body"], entry.Slot, v => { entry.Slot = v; Change(); }, display: SlotName);
            attributes.Button("Remove modifier", () => { Recipe.Attributes.Remove(entry); Rebuild(); Change(); }, Tone.Danger);
        }
        attributes.Button("+ Add attribute modifier", () => { Recipe.Attributes.Add(new()); Rebuild(); Change(); }, Tone.Primary);
        Host(attributeHost, attributes);
        UpdatePreview();
    }
    internal static string SlotName(string slot) => slot switch
    {
        "any" => "Any slot", "mainhand" => "Main hand", "offhand" => "Off hand", "hand" => "Either hand", "head" => "Head", "chest" => "Chest",
        "legs" => "Legs", "feet" => "Feet", "armor" => "Any armor slot", "body" => "Body (animals)", _ => slot
    };
    private void Change() { UpdatePreview(); Edited?.Invoke(); }
    private void UpdatePreview()
    {
        visual.Invalidate();
        try { generated.Text = Recipe.Argument() + "\r\n\r\n/give @s " + Recipe.Argument() + " " + Recipe.Count; visual.Error = null; }
        catch (Exception e) { generated.Text = e.Message; visual.Error = e.Message; }
    }
}

internal sealed class ScenarioDesigner : UserControl
{
    private readonly GameCatalog catalog;
    private readonly Func<ItemRecipe> item;
    private readonly TimelineCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly Panel properties = new() { Dock = DockStyle.Fill };
    private readonly BudgetBar budget = new() { Dock = DockStyle.Bottom };
    private readonly TextBox preview = Ui.TextInput("", true, true);
    private readonly TextBox projectName = Ui.TextInput();
    private readonly Button undoButton, redoButton;
    private readonly List<string> undo = [], redo = [];
    private string snapshot = "";
    private DateTime lastEdit;
    private IconHeader? header;
    public DesignerProject Project { get; private set; } = Authoring.Template("Countdown reveal");
    public event Action? Edited;
    public event Action? Replacing;
    public event Action<string, Tone>? Message;
    public ScenarioDesigner(GameCatalog catalog, Func<ItemRecipe> item)
    {
        this.catalog = catalog; this.item = item; Dock = DockStyle.Fill; canvas.Assets = catalog.Assets;
        preview.ReadOnly = true; preview.Dock = DockStyle.Fill; preview.WordWrap = false; preview.ScrollBars = ScrollBars.Both;
        var toolbar = new Panel { Dock = DockStyle.Top, Height = Ui.S(56), Padding = new Padding(Ui.S(14), Ui.S(12), Ui.S(10), Ui.S(8)), BackColor = Ui.Surface };
        projectName.Font = Ui.Heading; projectName.Dock = DockStyle.Left; projectName.Width = Ui.S(260); projectName.BorderStyle = BorderStyle.None; projectName.BackColor = Ui.Surface;
        Ui.Tips.SetToolTip(projectName, "Scenario name");
        projectName.TextChanged += (_, _) => { if (Project.Name != projectName.Text) { Project.Name = projectName.Text; Change(); } };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        undoButton = Ui.Button("Undo", Tone.Ghost, "Undo (Ctrl+Z)", Undo); redoButton = Ui.Button("Redo", Tone.Ghost, "Redo (Ctrl+Y)", Redo);
        var templates = Ui.Button("Templates ▾", Tone.Secondary, "Start from a ready-made scenario");
        templates.Click += (_, _) => TemplateMenu().Show(templates, new Point(0, templates.Height));
        var add = Ui.Button("+ Add action ▾", Tone.Primary, "Add a card after the selected one (Insert)");
        add.Click += (_, _) => AddMenu().Show(add, new Point(0, add.Height));
        right.Controls.AddRange([undoButton, redoButton, templates, add]);
        toolbar.Controls.Add(projectName); toolbar.Controls.Add(right);
        var views = new TabbedPanel();
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = Ui.S(6), BackColor = Ui.Canvas };
        split.Panel1.BackColor = Ui.Canvas; split.Panel2.BackColor = Ui.Surface;
        split.Panel1.Controls.Add(canvas); split.Panel2.Controls.Add(properties);
        views.Add("Design", split);
        var generatedPage = new Panel { Padding = new Padding(Ui.S(14)) }; generatedPage.Controls.Add(preview);
        generatedPage.Controls.Add(new Label { Text = "Exact command blocks this design compiles to: a clock block first, then one chain block per card (interval cards add four helpers).", Dock = DockStyle.Top, Height = Ui.S(36), ForeColor = Ui.Muted, UseMnemonic = false });
        views.Add("Generated commands", generatedPage);
        Controls.Add(views); Controls.Add(budget); Controls.Add(toolbar);
        Ui.InitialSplit(split, width => Math.Clamp(width * 2 / 5, Ui.S(260), Ui.S(380)));
        canvas.SelectionChanged += BuildProperties;
        canvas.Reorder += (from, to) => { var action = Project.Actions[from]; Project.Actions.RemoveAt(from); Project.Actions.Insert(to, action); canvas.Selected = to; Commit(); };
        canvas.AddRequested += point => AddMenu().Show(canvas, point);
        canvas.ContextRequested += point => CardMenu().Show(canvas, point);
        canvas.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) RemoveSelected();
            else if (e.KeyCode == Keys.D && e.Control) Duplicate();
            else if (e.KeyCode == Keys.Insert) AddMenu().Show(canvas, new Point(Ui.S(60), Ui.S(40)));
            else if (e.Alt && e.KeyCode is Keys.Up or Keys.Down) MoveCard(e.KeyCode == Keys.Up ? -1 : 1);
            else return;
            e.Handled = e.SuppressKeyPress = true;
        };
        ResetHistory(); RefreshView();
    }
    public void SetProject(DesignerProject project, bool recordUndo = false)
    {
        Authoring.Compile(project); Replacing?.Invoke();
        if (recordUndo) { undo.Add(snapshot); redo.Clear(); }
        Project = project; canvas.Selected = 0; snapshot = Serialize(); lastEdit = DateTime.MinValue;
        if (!recordUndo) { undo.Clear(); redo.Clear(); }
        RefreshView(); Edited?.Invoke();
    }
    public void RefreshCatalog() => BuildProperties();
    public List<ScenarioNode> Compile() => Authoring.Compile(Project);
    public bool CanUndo => undo.Count > 0;
    internal IReadOnlyDictionary<int, string> CardErrorsForTest => canvas.Errors;
    internal void RevalidateForTest() => ValidateCards();
    private string Serialize() => JsonSerializer.Serialize(Project);
    private void ResetHistory() { undo.Clear(); redo.Clear(); snapshot = Serialize(); lastEdit = DateTime.MinValue; UpdateHistoryButtons(); }
    private void UpdateHistoryButtons() { undoButton.Enabled = undo.Count > 0; redoButton.Enabled = redo.Count > 0; }
    private void Track(bool coalesce)
    {
        var now = Serialize(); if (now == snapshot) return;
        if (!coalesce || (DateTime.UtcNow - lastEdit).TotalMilliseconds > 900) { undo.Add(snapshot); if (undo.Count > 200) undo.RemoveAt(0); }
        redo.Clear(); snapshot = now; lastEdit = coalesce ? DateTime.UtcNow : DateTime.MinValue; UpdateHistoryButtons();
    }
    public void Undo() { if (undo.Count == 0) return; redo.Add(snapshot); Restore(undo[^1]); undo.RemoveAt(undo.Count - 1); }
    public void Redo() { if (redo.Count == 0) return; undo.Add(snapshot); Restore(redo[^1]); redo.RemoveAt(redo.Count - 1); }
    private void Restore(string state)
    {
        Project = JsonSerializer.Deserialize<DesignerProject>(state)!; snapshot = state; lastEdit = DateTime.MinValue;
        canvas.Selected = Math.Clamp(canvas.Selected, 0, Project.Actions.Count - 1); RefreshView(); UpdateHistoryButtons(); Edited?.Invoke();
    }
    private ContextMenuStrip TemplateMenu()
    {
        var menu = Ui.Menu();
        foreach (var (name, about) in new[] { ("Countdown reveal", "3 · 2 · 1 · Go! titles with a particle burst"), ("Fog introduction", "Title, cave sound, darkness and smoke"), ("Particle loop", "Repeating end-rod particles"), ("Mob showcase", "Title and a frozen named mob") })
        {
            var entry = new ToolStripMenuItem(name) { ToolTipText = about };
            entry.Click += (_, _) => { SetProject(Authoring.Template(name), true); UpdateHistoryButtons(); Message?.Invoke($"Loaded the {name} template. Press Ctrl+Z to restore your previous design.", Tone.Info); };
            menu.Items.Add(entry);
        }
        return menu;
    }
    private ContextMenuStrip AddMenu()
    {
        var menu = Ui.Menu();
        foreach (var group in Enum.GetValues<ActionKind>().GroupBy(ActionText.Group))
        {
            if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(group.Key.ToUpperInvariant()) { Enabled = false, Font = Ui.Label });
            foreach (var kind in group)
            {
                var sample = new ActionSpec { Kind = kind }; Defaults(sample);
                var entry = new ToolStripMenuItem(ActionText.Name(kind)) { Image = catalog.Assets.ActionIcon(sample) is Bitmap icon ? new Bitmap(icon) : null, ToolTipText = ActionText.Describe(kind) };
                entry.Click += (_, _) => AddAction(kind); menu.Items.Add(entry);
            }
        }
        menu.Closed += (_, _) => BeginInvoke(() => { foreach (ToolStripItem i in menu.Items) i.Image?.Dispose(); menu.Dispose(); });
        return menu;
    }
    private ContextMenuStrip CardMenu()
    {
        var menu = Ui.Menu();
        void Item(string text, string keys, Action action, bool enabled = true) { var i = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys, Enabled = enabled }; i.Click += (_, _) => action(); menu.Items.Add(i); }
        Item("Duplicate", "Ctrl+D", Duplicate); Item("Move up", "Alt+Up", () => MoveCard(-1), canvas.Selected > 0); Item("Move down", "Alt+Down", () => MoveCard(1), canvas.Selected < Project.Actions.Count - 1);
        menu.Items.Add(new ToolStripSeparator()); Item("Delete", "Del", RemoveSelected, Project.Actions.Count > 1);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        return menu;
    }
    private void AddAction(ActionKind kind)
    {
        if (Project.Actions.Count >= 100) { Message?.Invoke("A scenario can have at most 100 cards.", Tone.Warning); return; }
        var action = new ActionSpec { Kind = kind }; Defaults(action);
        int at = Math.Clamp(canvas.Selected + 1, 0, Project.Actions.Count); Project.Actions.Insert(at, action); canvas.Selected = at; Commit(); canvas.Focus();
    }
    private void Duplicate()
    {
        if (Project.Actions.Count >= 100) { Message?.Invoke("A scenario can have at most 100 cards.", Tone.Warning); return; }
        var copy = JsonSerializer.Deserialize<ActionSpec>(JsonSerializer.Serialize(Project.Actions[canvas.Selected]))!;
        Project.Actions.Insert(canvas.Selected + 1, copy); canvas.Selected++; Commit();
    }
    private void RemoveSelected()
    {
        if (Project.Actions.Count <= 1) { Message?.Invoke("A scenario needs at least one card.", Tone.Warning); return; }
        Project.Actions.RemoveAt(canvas.Selected); canvas.Selected = Math.Min(canvas.Selected, Project.Actions.Count - 1); Commit();
    }
    private void MoveCard(int delta)
    {
        int from = canvas.Selected, to = from + delta; if (to < 0 || to >= Project.Actions.Count) return;
        var action = Project.Actions[from]; Project.Actions.RemoveAt(from); Project.Actions.Insert(to, action); canvas.Selected = to; Commit();
    }
    private void Commit() { RefreshView(); Track(false); Edited?.Invoke(); }
    private void RefreshView()
    {
        if (projectName.Text != Project.Name) projectName.Text = Project.Name;
        canvas.Actions = Project.Actions; ValidateCards(); canvas.RefreshCards(); BuildProperties(); UpdatePreview(); UpdateHistoryButtons();
    }
    internal static void Defaults(ActionSpec a)
    {
        a.Resource = a.Kind switch { ActionKind.Effect => "minecraft:darkness", ActionKind.Summon => "minecraft:pig", ActionKind.SetBlock => "minecraft:glass", ActionKind.Sound => "minecraft:block.note_block.chime", _ => "minecraft:end_rod" };
        if (a.Kind == ActionKind.Weather) a.Option = "clear"; if (a.Kind == ActionKind.Time) a.Option = "day"; if (a.Kind == ActionKind.Tag) { a.Option = "add"; a.Text = "scenario_player"; }
    }
    private void ValidateCards()
    {
        var errors = new Dictionary<int, string>();
        for (int i = 0; i < Project.Actions.Count; i++)
        {
            try { Authoring.Compile(new DesignerProject { Objective = Project.Objective, Name = Project.Name, Actions = [Project.Actions[i]] }); }
            catch (ArgumentException e) { errors[i] = e.Message; }
        }
        canvas.Errors = errors;
    }
    private void BuildProperties()
    {
        int scroll = properties.Controls.Count > 0 && properties.Controls[0] is FormFields previous && previous.Tag is int last && last == canvas.Selected ? -previous.AutoScrollPosition.Y : 0;
        properties.SuspendLayout();
        foreach (Control old in properties.Controls.Cast<Control>().ToArray()) { properties.Controls.Remove(old); old.Dispose(); }
        header = null;
        if (canvas.Selected < 0 || canvas.Selected >= Project.Actions.Count) { properties.ResumeLayout(); return; }
        var a = Project.Actions[canvas.Selected]; var f = new FormFields { Tag = canvas.Selected }; properties.Controls.Add(f);
        f.Custom(header = new IconHeader(() => catalog.Assets.ActionIcon(a), $"{canvas.Selected + 1:00} · {ActionText.Name(a.Kind)}", () => ActionText.Summary(a), ActionText.Hue(a.Kind)));
        if (canvas.Errors.TryGetValue(canvas.Selected, out var error)) f.Note("⚠ " + error, Tone.Danger);
        f.Section("Action");
        f.Choice("Action type", Enum.GetNames<ActionKind>(), a.Kind.ToString(), v => { a.Kind = Enum.Parse<ActionKind>(v); Defaults(a); BuildProperties(); Change(); },
            display: v => ActionText.Name(Enum.Parse<ActionKind>(v)), icon: v => { var s = new ActionSpec { Kind = Enum.Parse<ActionKind>(v) }; Defaults(s); return catalog.Assets.ActionIcon(s); });
        switch (a.Kind)
        {
            case ActionKind.Message:
                f.TextField("Message text", a.Text, v => { a.Text = v; Change(); });
                f.Choice("Show as", ["Title", "Action bar", "Chat"], a.Presentation, v => { a.Presentation = v; Change(); }, hint: "Title: large centered text. Action bar: small text above the hotbar. Chat: a chat line.");
                f.Choice("Color", Authoring.Colors, a.Color, v => { a.Color = v; Change(); }, display: McColors.Display, swatch: McColors.Swatch);
                break;
            case ActionKind.Particle:
                f.ResourceChoice("Particle", catalog, "particles", a.Resource, v => { a.Resource = v; Change(); }, "Simple particle types. Parameterized particles such as dust are not available yet.");
                f.Number("Particles per run", a.Count, 1, 1000, v => { a.Count = (int)v; Change(); });
                f.Vector("Offset from each player", (a.X, a.Y, a.Z), (x, y, z) => { a.X = x; a.Y = y; a.Z = z; Change(); });
                break;
            case ActionKind.Effect:
                f.ResourceChoice("Effect", catalog, "effects", a.Resource, v => { a.Resource = v; Change(); });
                f.Number("Duration (seconds)", a.DurationSeconds, 1, 1000000, v => { a.DurationSeconds = (int)v; Change(); });
                f.Number("Level", a.EffectLevel, 1, 256, v => { a.EffectLevel = (int)v; Change(); }, hint: "Level 1 is the normal strength (Speed I).");
                f.Toggle("Hide effect particles", a.HideParticles, v => { a.HideParticles = v; Change(); });
                break;
            case ActionKind.Summon:
                f.ResourceChoice("Mob", catalog, "entities", a.Resource, v => { a.Resource = v; Change(); });
                f.TextField("Name tag", a.MobName, v => { a.MobName = v; Change(); }, placeholder: "Optional display name");
                f.Toggle("Frozen (no AI)", a.NoAi, v => { a.NoAi = v; Change(); }); f.Toggle("Invulnerable", a.Invulnerable, v => { a.Invulnerable = v; Change(); });
                f.Vector("Offset from each player", (a.X, a.Y, a.Z), (x, y, z) => { a.X = x; a.Y = y; a.Z = z; Change(); });
                f.Note("Every run spawns another mob. STOP does not remove mobs that were already summoned.", Tone.Warning);
                break;
            case ActionKind.GiveItem:
                f.Custom(new ItemVisualPreview(catalog.Assets, () => a.Item) { Height = Ui.S(150) });
                f.Button("Use the current Item page design", () => { a.Item = JsonSerializer.Deserialize<ItemRecipe>(JsonSerializer.Serialize(item()))!; BuildProperties(); Change(); }, Tone.Primary,
                    "Copies a snapshot. Later edits on the Item page do not change this card.");
                f.Note("This card keeps its own copy of the item. Every run gives another item; STOP does not take items back.", Tone.Warning);
                break;
            case ActionKind.Teleport:
                f.Vector("Destination (absolute world coordinates)", (a.X, a.Y, a.Z), (x, y, z) => { a.X = x; a.Y = y; a.Z = z; Change(); });
                break;
            case ActionKind.Sound:
                f.Choice("Sound", catalog.Get("sounds"), a.Resource, v => { a.Resource = v; Change(); }, true, display: v => v.Replace("minecraft:", ""));
                f.Vector("Offset from each player", (a.X, a.Y, a.Z), (x, y, z) => { a.X = x; a.Y = y; a.Z = z; Change(); });
                break;
            case ActionKind.SetBlock:
                f.ResourceChoice("Block", catalog, "blocks", a.Resource, v => { a.Resource = v; Change(); });
                f.Vector("Offset from each player", (a.X, a.Y, a.Z), (x, y, z) => { a.X = x; a.Y = y; a.Z = z; Change(); });
                f.Note("Placed blocks stay after STOP.", Tone.Warning);
                break;
            case ActionKind.Time: f.Choice("Time of day", ["day", "noon", "night", "midnight"], a.Option, v => { a.Option = v; Change(); }, display: v => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v)); break;
            case ActionKind.Weather: f.Choice("Weather", ["clear", "rain", "thunder"], a.Option, v => { a.Option = v; Change(); }, display: v => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v)); break;
            case ActionKind.Variable:
                f.TextField("Variable name", a.VariableName, v => { a.VariableName = v; Change(); }, hint: "1–16 letters, digits or underscores, starting with a letter.");
                f.Choice("Operation", ["Set", "Add", "Subtract"], a.VariableOperation, v => { a.VariableOperation = v; Change(); });
                f.Number("Value", a.VariableValue, int.MinValue, int.MaxValue, v => { a.VariableValue = (int)v; Change(); });
                f.Note("Variables are shared by the whole scenario and survive live edits. Give them a starting value with an On start card.");
                break;
            case ActionKind.Tag:
                f.TextField("Tag name", a.Text, v => { a.Text = v; Change(); });
                f.Choice("Operation", ["add", "remove"], a.Option, v => { a.Option = v; Change(); }, display: v => v == "add" ? "Add tag" : "Remove tag");
                break;
        }
        f.Section("When");
        f.Choice("Runs", ["On start", "Once after delay", "Every game tick", "Every interval"],
            a.Timing switch { TimingKind.OnStart => "On start", TimingKind.OnceAfterDelay => "Once after delay", TimingKind.EveryTick => "Every game tick", _ => "Every interval" },
            v => { a.Timing = v switch { "On start" => TimingKind.OnStart, "Once after delay" => TimingKind.OnceAfterDelay, "Every game tick" => TimingKind.EveryTick, _ => TimingKind.EveryInterval }; BuildProperties(); Change(); },
            hint: ActionText.TimingNames.GetValueOrDefault(a.Timing switch { TimingKind.OnStart => "On start", TimingKind.OnceAfterDelay => "Once after delay", TimingKind.EveryTick => "Every game tick", _ => "Every interval" }));
        if (a.Timing != TimingKind.OnStart) f.Number(a.Timing == TimingKind.OnceAfterDelay ? "Delay (seconds)" : "Start after (seconds)", a.DelaySeconds, 0, 50000, v => { a.DelaySeconds = v; Change(); }, 2);
        if (a.Timing == TimingKind.EveryInterval) f.Number("Repeat every (seconds)", a.IntervalSeconds, 0.05m, 50000, v => { a.IntervalSeconds = v; Change(); }, 2, "Minimum 0.05 s (one game tick). Uses four helper blocks.");
        if (a.Timing is TimingKind.EveryTick or TimingKind.EveryInterval)
        {
            f.Toggle("Stop repeating at a set time", a.EndSeconds is not null, v => { a.EndSeconds = v ? a.DelaySeconds + 10 : null; BuildProperties(); Change(); });
            if (a.EndSeconds is not null) f.Number("Stop at (seconds from scenario start)", a.EndSeconds.Value, 0, 50000, v => { a.EndSeconds = v; Change(); }, 2);
        }
        if (a.Kind != ActionKind.Variable)
        {
            f.Section("Who");
            f.Choice("Players", ["All players", "Nearest player"], a.Target, v => { a.Target = v; Change(); });
            f.TextField("Only players with tag", a.RequiredTag, v => { a.RequiredTag = v; Change(); }, placeholder: "Optional");
            f.Toggle("Invert: players without this tag", a.UnlessTag, v => { a.UnlessTag = v; Change(); });
        }
        f.Section("Only if");
        f.Toggle("Require a variable condition", a.ConditionVariable.Length > 0, v => { a.ConditionVariable = v ? "phase" : ""; BuildProperties(); Change(); });
        if (a.ConditionVariable.Length > 0)
        {
            f.TextField("Variable", a.ConditionVariable, v => { a.ConditionVariable = v; Change(); });
            f.Choice("Comparison", ["Equals", "Not equal", "At least", "At most"], a.Comparison, v => { a.Comparison = v; Change(); }, display: v => $"{ActionText.Comparison(v)}  {v}");
            f.Number("Value", a.ConditionValue, int.MinValue, int.MaxValue, v => { a.ConditionValue = (int)v; Change(); });
        }
        f.Section("Card");
        f.Note(a.Explain());
        f.Buttons(("Duplicate", Tone.Secondary, Duplicate), ("Delete card", Tone.Danger, RemoveSelected));
        Ui.Apply(properties); properties.ResumeLayout(true);
        if (scroll > 0) f.AutoScrollPosition = new Point(0, scroll);
    }
    private void Change() { ValidateCards(); canvas.RefreshCards(); header?.Invalidate(); UpdatePreview(); Track(true); Edited?.Invoke(); }
    private void UpdatePreview()
    {
        try
        {
            var nodes = Compile();
            preview.Text = string.Join("\r\n\r\n", nodes.Select((n, i) => $"{i + 1:00} · {n.Name}{(n.Conditional ? " (conditional)" : "")}\r\n{n.Command}"));
            budget.Set(Project.Actions.Count, nodes.Count, null);
        }
        catch (Exception e) { budget.Set(Project.Actions.Count, -1, e.Message); preview.Text = e.Message; }
    }
}

// Footer showing card count, compiled block budget and compile errors.
internal sealed class BudgetBar : Control
{
    private int cards, blocks; private string? error;
    public BudgetBar() { Height = Ui.S(46); DoubleBuffered = true; BackColor = Ui.Surface; }
    public void Set(int cardCount, int blockCount, string? message) { cards = cardCount; blocks = blockCount; error = message; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); using var line = new Pen(Ui.Border); g.DrawLine(line, 0, 0, Width, 0);
        int x = Ui.S(14);
        if (error is not null)
        {
            TextRenderer.DrawText(g, "⚠ " + error, Ui.Body, new Rectangle(x, 0, Width - x * 2, Height), Ui.Danger, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
        string label = $"{cards} card{(cards == 1 ? "" : "s")}  →  {blocks} / 128 command blocks";
        var size = TextRenderer.MeasureText(label, Ui.Strong);
        TextRenderer.DrawText(g, label, Ui.Strong, new Rectangle(x, 0, size.Width, Height), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        int barX = x + size.Width + Ui.S(14), barW = Math.Max(Ui.S(60), Math.Min(Ui.S(220), Width - barX - Ui.S(260))), barY = (Height - Ui.S(6)) / 2;
        using (var track = new SolidBrush(Ui.Input)) g.FillRectangle(track, barX, barY, barW, Ui.S(6));
        double ratio = Math.Clamp(blocks / 128.0, 0, 1);
        using (var fill = new SolidBrush(ratio > 0.85 ? Ui.Warning : Ui.Accent)) g.FillRectangle(fill, barX, barY, (int)(barW * ratio), Ui.S(6));
        TextRenderer.DrawText(g, "Run restarts the clock · live edits keep it", Ui.Small, new Rectangle(barX + barW + Ui.S(14), 0, Width - barX - barW - Ui.S(20), Height), Ui.Subtle, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

// Vertical timeline of cards with a time gutter, drag reordering and keyboard navigation.
internal sealed class TimelineCanvas : ScrollableControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public MinecraftAssets? Assets { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public List<ActionSpec> Actions { get; set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Dictionary<int, string> Errors { get; set; } = [];
    public event Action? SelectionChanged;
    public event Action<int, int>? Reorder;
    public event Action<Point>? AddRequested;
    public event Action<Point>? ContextRequested;
    private int dragged = -1, slot = -1, hover = -1; private Point downAt; private bool dragging;
    private static int CardHeight => Ui.S(86);
    private static int Gap => Ui.S(10);
    private static int TopPad => Ui.S(14);
    private static int Gutter => Ui.S(64);
    public TimelineCanvas()
    {
        DoubleBuffered = true; AutoScroll = true; BackColor = Ui.Canvas; TabStop = true; SetStyle(ControlStyles.Selectable, true); Ui.DarkNative(this);
        Ui.Tips.SetToolTip(this, "Click to select · drag to reorder · right-click for actions\nDel delete · Ctrl+D duplicate · Alt+↑/↓ move · Insert add");
    }
    private int IndexAt(int y) { int local = y - AutoScrollPosition.Y - TopPad; if (local < 0) return -1; int i = local / (CardHeight + Gap); return local % (CardHeight + Gap) <= CardHeight ? i : -1; }
    private Rectangle CardBounds(int i) => new(Gutter, TopPad + i * (CardHeight + Gap) + AutoScrollPosition.Y, Math.Max(Ui.S(160), ClientSize.Width - Gutter - Ui.S(14)), CardHeight);
    public void RefreshCards() { AutoScrollMinSize = new Size(0, (Actions.Count + 1) * (CardHeight + Gap) + TopPad * 2); Invalidate(); }
    private void Select(int index) { if (index < 0 || index >= Actions.Count) return; Selected = index; EnsureVisible(index); Invalidate(); SelectionChanged?.Invoke(); }
    private void EnsureVisible(int index)
    {
        var b = CardBounds(index);
        if (b.Top < 0) AutoScrollPosition = new Point(0, -AutoScrollPosition.Y + b.Top - TopPad);
        else if (b.Bottom > ClientSize.Height) AutoScrollPosition = new Point(0, -AutoScrollPosition.Y + b.Bottom - ClientSize.Height + TopPad);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (e.Handled) return;
        if (!e.Alt && e.KeyCode == Keys.Up) { Select(Selected - 1); e.Handled = true; }
        else if (!e.Alt && e.KeyCode == Keys.Down) { Select(Selected + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Home) Select(0); else if (e.KeyCode == Keys.End) Select(Actions.Count - 1);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus(); int index = IndexAt(e.Y);
        if (index >= 0 && index < Actions.Count)
        {
            if (index != Selected) { Selected = index; Invalidate(); SelectionChanged?.Invoke(); }
            if (e.Button == MouseButtons.Right) { ContextRequested?.Invoke(e.Location); return; }
            dragged = index; downAt = e.Location;
        }
        else if (index == Actions.Count && e.Button == MouseButtons.Left) AddRequested?.Invoke(e.Location);
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragged >= 0 && e.Button == MouseButtons.Left)
        {
            if (!dragging && Math.Abs(e.Y - downAt.Y) > Ui.S(6)) { dragging = true; Cursor = Cursors.SizeNS; }
            if (dragging) { int local = e.Y - AutoScrollPosition.Y - TopPad + (CardHeight + Gap) / 2; slot = Math.Clamp(local / (CardHeight + Gap), 0, Actions.Count); Invalidate(); }
        }
        else { int h = IndexAt(e.Y); if (h != hover) { hover = h; Cursor = h >= 0 && h <= Actions.Count ? Cursors.Hand : Cursors.Default; Invalidate(); } }
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging && slot >= 0) { int to = slot > dragged ? slot - 1 : slot; if (to != dragged) Reorder?.Invoke(dragged, to); }
        dragged = slot = -1; dragging = false; Cursor = Cursors.Default; Invalidate(); base.OnMouseUp(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; Ui.Smooth(g);
        using var rail = new Pen(Ui.Border, Ui.S(2));
        if (Actions.Count > 0) g.DrawLine(rail, Gutter / 2 + Ui.S(14), CardBounds(0).Top + CardHeight / 2, Gutter / 2 + Ui.S(14), CardBounds(Actions.Count - 1).Top + CardHeight / 2);
        for (int i = 0; i < Actions.Count; i++)
        {
            var b = CardBounds(i); if (b.Bottom < 0 || b.Top > ClientSize.Height) continue;
            var a = Actions[i]; var hue = ActionText.Hue(a.Kind); bool active = i == Selected, failed = Errors.ContainsKey(i);
            // time gutter
            int dotX = Gutter / 2 + Ui.S(14), dotY = b.Top + CardHeight / 2;
            using (var dot = new SolidBrush(active ? Ui.Accent : Ui.Border)) g.FillEllipse(dot, dotX - Ui.S(5), dotY - Ui.S(5), Ui.S(10), Ui.S(10));
            TextRenderer.DrawText(g, ActionText.Gutter(a), Ui.Small, new Rectangle(0, dotY - Ui.S(28), Gutter + Ui.S(2), Ui.S(18)), active ? Ui.Text : Ui.Subtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            // card
            using (var path = Ui.Rounded(b, Ui.S(6)))
            {
                using var fill = new SolidBrush(active ? Ui.Hover : i == hover ? Color.FromArgb(34, 43, 57) : Ui.Raised); g.FillPath(fill, path);
                using var border = new Pen(failed ? Ui.Danger : active ? Ui.Accent : Ui.Border, active || failed ? Ui.S(2) : 1); g.DrawPath(border, path);
            }
            using (var strip = new SolidBrush(hue)) g.FillRectangle(strip, b.X + 1, b.Y + Ui.S(10), Ui.S(4), b.Height - Ui.S(20));
            var iconBox = new Rectangle(b.X + Ui.S(14), b.Y + Ui.S(12), Ui.S(34), Ui.S(34));
            MinecraftAssets.Draw(g, Assets?.ActionIcon(a), iconBox);
            int textX = iconBox.Right + Ui.S(12), textW = b.Right - textX - Ui.S(10);
            TextRenderer.DrawText(g, $"{i + 1:00}  {ActionText.Name(a.Kind)}", Ui.Strong, new Rectangle(textX, b.Y + Ui.S(10), textW, Ui.S(20)), Ui.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, ActionText.Summary(a), Ui.Body, new Rectangle(textX, b.Y + Ui.S(30), textW, Ui.S(20)), Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            int chipX = b.X + Ui.S(14), chipY = b.Bottom - Ui.S(28);
            foreach (var chip in failed ? ActionText.Chips(a).Prepend("needs attention") : ActionText.Chips(a))
            {
                var size = TextRenderer.MeasureText(chip, Ui.Small); var box = new Rectangle(chipX, chipY, size.Width + Ui.S(12), Ui.S(20));
                if (box.Right > b.Right - Ui.S(8)) break;
                bool warn = chip == "needs attention";
                using (var chipPath = Ui.Rounded(box, Ui.S(10))) { using var chipFill = new SolidBrush(warn ? Ui.DangerDim : Ui.Input); g.FillPath(chipFill, chipPath); }
                TextRenderer.DrawText(g, chip, Ui.Small, box, warn ? Ui.Danger : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                chipX = box.Right + Ui.S(6);
            }
            if (active && Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(b, -Ui.S(4), -Ui.S(4)), Ui.Muted, Ui.Hover);
        }
        // add-card affordance
        var add = CardBounds(Actions.Count); add.Height = Ui.S(48);
        using (var dash = new Pen(Actions.Count == hover ? Ui.Accent : Ui.Border) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        using (var addPath = Ui.Rounded(add, Ui.S(6))) g.DrawPath(dash, addPath);
        TextRenderer.DrawText(g, "+  Add action", Ui.Strong, add, Actions.Count == hover ? Ui.Accent : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (dragging && slot >= 0)
        {
            int y = TopPad + slot * (CardHeight + Gap) + AutoScrollPosition.Y - Gap / 2;
            using var drop = new Pen(Ui.Accent, Ui.S(3)); g.DrawLine(drop, Gutter - Ui.S(8), y, ClientSize.Width - Ui.S(10), y);
        }
    }
}
