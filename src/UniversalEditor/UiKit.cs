using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace UniversalEditor;

internal enum Tone { Primary, Secondary, Danger, Ghost, Success, Warning, Info, Muted }

// Design tokens and factory helpers shared by every screen. Controls inherit the ambient
// background/foreground from their container; only inputs and buttons are styled explicitly.
internal static class Ui
{
    public static readonly Color Canvas = Color.FromArgb(15, 19, 26);
    public static readonly Color Surface = Color.FromArgb(22, 28, 38);
    public static readonly Color Raised = Color.FromArgb(30, 38, 51);
    public static readonly Color Hover = Color.FromArgb(39, 49, 65);
    public static readonly Color Input = Color.FromArgb(35, 44, 58);
    public static readonly Color Border = Color.FromArgb(50, 62, 80);
    public static readonly Color Text = Color.FromArgb(232, 237, 244);
    public static readonly Color Muted = Color.FromArgb(150, 163, 182);
    public static readonly Color Subtle = Color.FromArgb(106, 120, 140);
    public static readonly Color Accent = Color.FromArgb(74, 204, 150);
    public static readonly Color AccentDim = Color.FromArgb(32, 78, 64);
    public static readonly Color AccentInk = Color.FromArgb(8, 32, 24);
    public static readonly Color Danger = Color.FromArgb(232, 88, 88);
    public static readonly Color DangerDim = Color.FromArgb(78, 30, 34);
    public static readonly Color Warning = Color.FromArgb(240, 182, 76);
    public static readonly Color WarningDim = Color.FromArgb(74, 56, 24);
    public static readonly Color Info = Color.FromArgb(96, 164, 244);
    public static readonly Color InfoDim = Color.FromArgb(28, 48, 78);

    private static readonly float scale = Measure();
    private static float Measure() { try { using var g = Graphics.FromHwnd(IntPtr.Zero); return g.DpiX / 96f; } catch (Exception) { return 1f; } }
    public static int S(int pixels) => (int)Math.Round(pixels * scale);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Label = new("Segoe UI Semibold", 8.75f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 14f);
    public static readonly Font Mono = MonoFont();
    private static Font MonoFont()
    {
        foreach (var name in new[] { "Cascadia Mono", "Cascadia Code", "Consolas" })
            if (FontFamily.Families.Any(f => f.Name == name)) return new Font(name, 9.5f);
        return new Font(FontFamily.GenericMonospace, 9.5f);
    }

    public static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Primary or Tone.Success => Accent, Tone.Danger => Danger, Tone.Warning => Warning, Tone.Info => Info, Tone.Muted => Subtle, _ => Muted
    };
    public static Color ToneFill(Tone tone) => tone switch
    {
        Tone.Primary or Tone.Success => AccentDim, Tone.Danger => DangerDim, Tone.Warning => WarningDim, Tone.Info => InfoDim, _ => Raised
    };

    public static Button Button(string text, Tone tone = Tone.Secondary, string? tip = null, Action? click = null)
    {
        var button = new UiButton
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(S(40), S(32)),
            Padding = new Padding(S(8), 0, S(8), 0), Margin = new Padding(0, 0, S(6), S(6)), FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand, UseVisualStyleBackColor = false, Font = tone == Tone.Primary ? Strong : Body, UseMnemonic = false
        };
        Style(button, tone); button.EnabledChanged += (_, _) => Style(button, tone);
        if (tip is not null) Tips.SetToolTip(button, tip);
        if (click is not null) button.Click += (_, _) => click();
        return button;
    }
    public static void Style(Button button, Tone tone)
    {
        var flat = button.FlatAppearance; flat.BorderSize = 1;
        if (!button.Enabled) { button.BackColor = Surface; button.ForeColor = Subtle; flat.BorderColor = Border; return; }
        (Color back, Color fore, Color border, Color hover) = tone switch
        {
            Tone.Primary => (Accent, AccentInk, Accent, Color.FromArgb(104, 222, 172)),
            Tone.Danger => (DangerDim, Color.FromArgb(255, 214, 214), Danger, Color.FromArgb(104, 38, 42)),
            Tone.Ghost => (Surface, Muted, Surface, Hover),
            _ => (Raised, Text, Border, Hover)
        };
        button.BackColor = back; button.ForeColor = fore; flat.BorderColor = border; flat.MouseOverBackColor = hover; flat.MouseDownBackColor = hover;
    }

    public static readonly ToolTip Tips = new() { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 100 };

    public static Label Caption(string text, Font? font = null, Color? color = null) =>
        new() { Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, Margin = new Padding(0, 0, 0, S(4)), UseMnemonic = false };

    public static TextBox TextInput(string text = "", bool multiline = false, bool mono = false) => new()
    {
        Text = text, Multiline = multiline, BorderStyle = BorderStyle.FixedSingle, BackColor = Input, ForeColor = Text,
        Font = mono ? Mono : Body, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, AcceptsReturn = multiline
    };

    public static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals = 0) => new()
    {
        Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.05m, Value = Math.Clamp(value, min, max),
        BorderStyle = BorderStyle.FixedSingle, BackColor = Input, ForeColor = Text
    };

    // Applies native dark scrollbars/inputs to a control tree. Safe to call repeatedly.
    public static void Apply(Control root)
    {
        switch (root)
        {
            case TextBox or NumericUpDown: root.BackColor = Input; root.ForeColor = Text; break;
            case ComboBox combo when combo is not DarkCombo: combo.BackColor = Input; combo.ForeColor = Text; combo.FlatStyle = FlatStyle.Flat; break;
            case TreeView or ListBox: root.ForeColor = Text; break;
            case CheckBox check: check.ForeColor = Text; check.FlatStyle = FlatStyle.Flat; check.FlatAppearance.BorderColor = Border; check.FlatAppearance.CheckedBackColor = AccentDim; break;
        }
        if (root is TextBoxBase or TreeView or ListBox or ScrollableControl or ComboBox or NumericUpDown) DarkNative(root);
        foreach (Control child in root.Controls) Apply(child);
    }
    public static void DarkNative(Control control)
    {
        static void Theme(object? sender, EventArgs e)
        {
            if (sender is not Control c) return;
            try { _ = SetWindowTheme(c.Handle, c is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer", null); } catch (Exception) { }
            if (c is NumericUpDown n) foreach (Control part in n.Controls) try { _ = SetWindowTheme(part.Handle, "DarkMode_Explorer", null); } catch (Exception) { }
        }
        if (control.IsHandleCreated) Theme(control, EventArgs.Empty);
        control.HandleCreated -= Theme; control.HandleCreated += Theme;
    }
    public static void DarkTitleBar(Form form)
    {
        form.HandleCreated += (_, _) => { try { int on = 1; _ = DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)); } catch (Exception) { } };
    }

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath(); float d = Math.Max(1, radius * 2);
        if (radius <= 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    // Sets a proportional splitter position once the container has a real size; later user drags are kept.
    public static void InitialSplit(SplitContainer split, Func<int, int> distance)
    {
        bool done = false;
        split.SizeChanged += (_, _) =>
        {
            if (done || split.Width < S(500)) return;
            try { split.SplitterDistance = Math.Clamp(distance(split.Width), split.Panel1MinSize, split.Width - split.Panel2MinSize - split.SplitterWidth); done = true; } catch (Exception) { }
        };
    }
    public static void Smooth(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }

    public static ContextMenuStrip Menu() => new() { Renderer = new DarkMenuRenderer(), BackColor = Raised, ForeColor = Text, Font = Body, ShowImageMargin = true, ImageScalingSize = new Size(S(20), S(20)) };

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string? app, string? idList);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

// Flat button that stays legible when disabled (WinForms ignores ForeColor for disabled flat buttons).
internal sealed class UiButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled) { base.OnPaint(e); return; }
        var g = e.Graphics; g.Clear(Ui.Surface);
        using (var border = new Pen(Ui.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.FromArgb(124, 137, 156), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new Colors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Ui.Text : Ui.Subtle; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Ui.Muted; base.OnRenderArrow(e); }
    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Ui.Raised;
        public override Color ImageMarginGradientBegin => Ui.Raised;
        public override Color ImageMarginGradientMiddle => Ui.Raised;
        public override Color ImageMarginGradientEnd => Ui.Raised;
        public override Color MenuBorder => Ui.Border;
        public override Color MenuItemBorder => Ui.Hover;
        public override Color MenuItemSelected => Ui.Hover;
        public override Color MenuItemSelectedGradientBegin => Ui.Hover;
        public override Color MenuItemSelectedGradientEnd => Ui.Hover;
        public override Color SeparatorDark => Ui.Border;
        public override Color SeparatorLight => Ui.Raised;
    }
}

// Owner-drawn dropdown so the closed box and the list both follow the dark palette.
internal class DarkCombo : ComboBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Func<object, Image?>? IconFor { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Func<object, Color?>? SwatchFor { get; set; }
    public DarkCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat;
        ItemHeight = Ui.S(24); BackColor = Ui.Input; ForeColor = Ui.Text; IntegralHeight = false; MaxDropDownItems = 16;
        Ui.DarkNative(this);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics; bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0, active = (e.State & DrawItemState.Selected) != 0 && !edit;
        using (var back = new SolidBrush(active ? Ui.AccentDim : Ui.Input)) g.FillRectangle(back, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var item = Items[e.Index]!; int x = e.Bounds.X + Ui.S(6);
        if (SwatchFor?.Invoke(item) is Color swatch)
        {
            var box = new Rectangle(x, e.Bounds.Y + (e.Bounds.Height - Ui.S(12)) / 2, Ui.S(12), Ui.S(12));
            using var brush = new SolidBrush(swatch); g.FillRectangle(brush, box); using var pen = new Pen(Ui.Border); g.DrawRectangle(pen, box); x += Ui.S(20);
        }
        if (IconFor?.Invoke(item) is Image icon)
        {
            int side = e.Bounds.Height - Ui.S(4); MinecraftAssets.Draw(g, icon, new Rectangle(x, e.Bounds.Y + Ui.S(2), side, side)); x += side + Ui.S(6);
        }
        TextRenderer.DrawText(g, GetItemText(item), Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x - Ui.S(2), e.Bounds.Height),
            Enabled ? Ui.Text : Ui.Subtle, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

// Horizontal segmented tabs drawn with an accent underline. Replaces the unthemeable TabControl headers.
internal sealed class TabStrip : Control
{
    private readonly List<string> items = [];
    private int selected, hover = -1;
    public event Action<int>? SelectedChanged;
    public TabStrip()
    {
        DoubleBuffered = true; Height = Ui.S(38); Dock = DockStyle.Top; Font = Ui.Strong; TabStop = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
    }
    public IReadOnlyList<string> Items => items;
    public void Add(string text) { items.Add(text); Invalidate(); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected { get => selected; set { value = Math.Clamp(value, 0, Math.Max(0, items.Count - 1)); if (value == selected) return; selected = value; Invalidate(); SelectedChanged?.Invoke(value); } }
    private List<(int Index, Rectangle Bounds)> Layouts()
    {
        var result = new List<(int, Rectangle)>(); int x = Ui.S(4);
        for (int i = 0; i < items.Count; i++)
        {
            int width = TextRenderer.MeasureText(items[i], Font).Width + Ui.S(24);
            result.Add((i, new Rectangle(x, 0, width, Height))); x += width;
        }
        return result;
    }
    private int IndexAt(Point p) { foreach (var (i, b) in Layouts()) if (b.Contains(p)) return i; return -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int next = IndexAt(e.Location); if (next != hover) { hover = next; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int i = IndexAt(e.Location); if (i >= 0) Selected = i; Focus(); base.OnMouseDown(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Left) Selected--; else if (e.KeyCode == Keys.Right) Selected++; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor);
        using var line = new Pen(Ui.Border); g.DrawLine(line, 0, Height - 1, Width, Height - 1);
        foreach (var (i, b) in Layouts())
        {
            bool active = i == selected;
            if (i == hover && !active) { using var hoverFill = new SolidBrush(Ui.Hover); g.FillRectangle(hoverFill, b.X, b.Y + Ui.S(4), b.Width, b.Height - Ui.S(8)); }
            TextRenderer.DrawText(g, items[i], Font, b, active ? Ui.Text : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (active) { using var accent = new SolidBrush(Ui.Accent); g.FillRectangle(accent, b.X + Ui.S(8), b.Bottom - Ui.S(3), b.Width - Ui.S(16), Ui.S(3)); }
            if (active && Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(b, -Ui.S(3), -Ui.S(6)), Ui.Muted, BackColor);
        }
    }
}

// Container with a TabStrip header; pages are created once and switched by visibility.
internal sealed class TabbedPanel : Panel
{
    private readonly Panel body = new() { Dock = DockStyle.Fill };
    private readonly List<Control> pages = [];
    public TabStrip Strip { get; } = new();
    public event Action<int>? SelectedChanged;
    public TabbedPanel()
    {
        Dock = DockStyle.Fill; Controls.Add(body); Controls.Add(Strip);
        Strip.SelectedChanged += i => { ShowPage(i); SelectedChanged?.Invoke(i); };
    }
    public void Add(string title, Control page) { page.Dock = DockStyle.Fill; page.Visible = pages.Count == Strip.Selected; pages.Add(page); body.Controls.Add(page); Strip.Add(title); }
    public int Count => pages.Count;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected { get => Strip.Selected; set { Strip.Selected = value; ShowPage(Strip.Selected); } }
    private void ShowPage(int index) { for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == index; }
}

// Rounded status chip with a colored dot, used for game and live-bridge state.
internal sealed class StatusPill : Control
{
    private Tone tone = Tone.Muted;
    public StatusPill() { DoubleBuffered = true; Font = Ui.Label; Height = Ui.S(28); Margin = new Padding(Ui.S(8), Ui.S(12), 0, 0); }
    public void Set(string text, Tone value, string? detail = null)
    {
        Text = text; tone = value; Width = TextRenderer.MeasureText(text, Font).Width + Ui.S(36);
        Ui.Tips.SetToolTip(this, detail ?? text); Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Ui.Surface); Ui.Smooth(g);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Ui.Rounded(bounds, bounds.Height / 2)) { using var fill = new SolidBrush(Ui.ToneFill(tone)); g.FillPath(fill, path); using var border = new Pen(Color.FromArgb(120, Ui.ToneColor(tone))); g.DrawPath(border, path); }
        using (var dot = new SolidBrush(Ui.ToneColor(tone))) g.FillEllipse(dot, Ui.S(11), (Height - Ui.S(8)) / 2f, Ui.S(8), Ui.S(8));
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Ui.S(25), 0, Width - Ui.S(29), Height), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

// Non-modal notification strip. Replaces message boxes so errors never steal focus from the game.
internal sealed class Banner : Panel
{
    private readonly Label message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Body, UseMnemonic = false, Padding = new Padding(Ui.S(14), 0, 0, 0) };
    private readonly Button close;
    private readonly System.Windows.Forms.Timer hide = new();
    private Tone tone = Tone.Info;
    public string Message => message.Text;
    public Banner()
    {
        Dock = DockStyle.Top; Height = Ui.S(40); Visible = false; DoubleBuffered = true;
        close = Ui.Button("✕", Tone.Ghost, "Dismiss", () => Visible = false); close.Dock = DockStyle.Right; close.Margin = Padding.Empty; close.AutoSize = false; close.Width = Ui.S(40);
        Controls.Add(message); Controls.Add(close);
        hide.Tick += (_, _) => { hide.Stop(); Visible = false; };
    }
    public void Show(string text, Tone value)
    {
        tone = value; message.Text = text; message.ForeColor = Ui.Text; BackColor = Ui.ToneFill(value);
        close.BackColor = BackColor; close.FlatAppearance.BorderColor = BackColor;
        Height = Math.Max(Ui.S(40), TextRenderer.MeasureText(text, Ui.Body, new Size(Math.Max(200, Width - Ui.S(80)), 0), TextFormatFlags.WordBreak).Height + Ui.S(18));
        Visible = true; Invalidate();
        hide.Stop(); if (value is Tone.Success or Tone.Info) { hide.Interval = 7000; hide.Start(); }
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var strip = new SolidBrush(Ui.ToneColor(tone)); e.Graphics.FillRectangle(strip, 0, 0, Ui.S(4), Height); }
    protected override void Dispose(bool disposing) { if (disposing) hide.Dispose(); base.Dispose(disposing); }
}

// Panel with a raised background, 1px border and an optional title/description header.
internal sealed class Card : Panel
{
    private readonly Label? description;
    public Card(string? title = null, string? text = null)
    {
        BackColor = Ui.Raised; Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), Ui.S(14)); Margin = new Padding(0, 0, 0, Ui.S(12));
        if (text is not null) Controls.Add(description = new Label { Text = text, Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(40), ForeColor = Ui.Muted, Font = Ui.Body, UseMnemonic = false });
        if (title is not null) Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = Ui.S(28), Font = Ui.Heading, ForeColor = Ui.Text, UseMnemonic = false });
    }
    // When set, the card grows to fit its title and description (for text-only cards).
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool FitText { get; set; }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (description is null) return;
        description.Height = TextRenderer.MeasureText(description.Text, Ui.Body, new Size(Math.Max(100, ClientSize.Width - Padding.Horizontal), 0), TextFormatFlags.WordBreak).Height + Ui.S(10);
        if (FitText) { int want = Padding.Vertical + Ui.S(28) + description.Height; if (Height != want) Height = want; }
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var border = new Pen(Ui.Border); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1); }
}

// Left navigation rail with real Minecraft icons and keyboard hints.
internal sealed class NavRail : Control
{
    internal sealed record Entry(string Title, string Subtitle, string Shortcut, Func<Image?> Icon);
    private readonly List<Entry> entries = [];
    private int selected, hover = -1;
    private static int RowHeight => Ui.S(58);
    private static int Top0 => Ui.S(12);
    public event Action<int>? SelectedChanged;
    public NavRail() { DoubleBuffered = true; Dock = DockStyle.Left; Width = Ui.S(208); BackColor = Ui.Surface; Cursor = Cursors.Hand; TabStop = true; SetStyle(ControlStyles.Selectable, true); }
    public void Add(Entry entry) { entries.Add(entry); Invalidate(); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected { get => selected; set { value = Math.Clamp(value, 0, entries.Count - 1); if (value == selected) return; selected = value; Invalidate(); SelectedChanged?.Invoke(value); } }
    private int IndexAt(Point p) { int i = (p.Y - Top0) / RowHeight; return p.Y >= Top0 && i < entries.Count ? i : -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != hover) { hover = i; Invalidate(); Ui.Tips.SetToolTip(this, i >= 0 ? $"{entries[i].Title} · {entries[i].Shortcut}" : ""); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int i = IndexAt(e.Location); if (i >= 0) Selected = i; Focus(); base.OnMouseDown(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Up) Selected--; else if (e.KeyCode == Keys.Down) Selected++; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor);
        using var border = new Pen(Ui.Border); g.DrawLine(border, Width - 1, 0, Width - 1, Height);
        for (int i = 0; i < entries.Count; i++)
        {
            var row = new Rectangle(Ui.S(8), Top0 + i * RowHeight, Width - Ui.S(17), RowHeight - Ui.S(6));
            bool active = i == selected;
            if (active || i == hover)
            {
                Ui.Smooth(g); using var path = Ui.Rounded(row, Ui.S(6)); using var fill = new SolidBrush(active ? Ui.Raised : Ui.Hover); g.FillPath(fill, path);
                if (active) { using var accent = new SolidBrush(Ui.Accent); g.FillRectangle(accent, row.X, row.Y + Ui.S(10), Ui.S(3), row.Height - Ui.S(20)); }
                if (active && Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(row, -2, -2), Ui.Muted, Ui.Raised);
            }
            var icon = new Rectangle(row.X + Ui.S(12), row.Y + (row.Height - Ui.S(30)) / 2, Ui.S(30), Ui.S(30));
            MinecraftAssets.Draw(g, entries[i].Icon(), icon);
            int textX = icon.Right + Ui.S(12);
            TextRenderer.DrawText(g, entries[i].Title, Ui.Strong, new Rectangle(textX, row.Y + Ui.S(8), row.Right - textX, Ui.S(20)), active ? Ui.Text : Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, entries[i].Subtitle, Ui.Small, new Rectangle(textX, row.Y + Ui.S(28), row.Right - textX, Ui.S(18)), Ui.Subtle, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        TextRenderer.DrawText(g, "Ctrl+1–6 switch pages", Ui.Small, new Rectangle(Ui.S(18), Height - Ui.S(30), Width - Ui.S(24), Ui.S(20)), Ui.Subtle, TextFormatFlags.NoPrefix);
    }
}

// Per-user preferences, stored outside the project folder.
internal sealed class UserSettings
{
    public int MinRam { get; set; } = 1024;
    public int MaxRam { get; set; } = 4096;
    public string? PrismExecutable { get; set; }
    public string? PrismData { get; set; }
    public string? Release { get; set; }
    public int Page { get; set; }
    public int? Splitter { get; set; }
    public bool ActivityOpen { get; set; }
    public bool DiscordEnabled { get; set; } = true;
    public bool DiscordShowNames { get; set; }
    public string? DiscordApplicationId { get; set; }
    public WorldStartup WorldStartup { get; set; } = WorldStartup.OpenOrCreate;
    public string? EditorWorld { get; set; }
    public string? OpenWorld { get; set; }
    public string NewWorldName { get; set; } = "Command Forge World";
    public string NewWorldPreset { get; set; } = "flat";
    public string NewWorldGameMode { get; set; } = "creative";
    public string NewWorldDifficulty { get; set; } = "peaceful";
    public string NewWorldSeed { get; set; } = "";
    public bool NewWorldStructures { get; set; }
    public bool AlwaysDay { get; set; } = true;
    public bool ClearWeather { get; set; } = true;
    public bool NoMobSpawning { get; set; } = true;
    public bool QuietCommandBlocks { get; set; } = true;
    public bool KeepWorldRunning { get; set; } = true;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommandForge");
    // Before the rename to Command Forge, settings and drafts lived in %LOCALAPPDATA%\UniversalEditor. Copy them once;
    // the old folder is left in place because launcher-session caches still live there.
    public static void MigrateLegacyFolder()
    {
        string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniversalEditor");
        foreach (var name in new[] { "settings.json", "draft.json" })
        {
            string from = Path.Combine(legacy, name), to = Path.Combine(Folder, name);
            try { if (File.Exists(from) && !File.Exists(to)) { Directory.CreateDirectory(Folder); File.Copy(from, to); } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    private static string FilePath => Path.Combine(Folder, "settings.json");
    public static UserSettings Load()
    {
        try { if (File.Exists(FilePath) && new FileInfo(FilePath).Length < 64 * 1024) return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }
    public void Save() => AtomicWrite(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    internal static void AtomicWrite(string path, string text)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp"; File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

// Autosaved working state: scenario design, standalone item draft, advanced command list and anchor.
internal sealed class WorkingDraft
{
    public string Format { get; set; } = "universal-editor-draft-1";
    public DesignerProject? Scenario { get; set; }
    public ItemRecipe? Item { get; set; }
    public List<ScenarioNode>? Commands { get; set; }
    public int[]? Anchor { get; set; }
    public string? ProjectPath { get; set; }
    private static string FilePath => Path.Combine(UserSettings.Folder, "draft.json");
    public static WorkingDraft? Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 4 * 1024 * 1024) return null;
            var draft = JsonSerializer.Deserialize<WorkingDraft>(File.ReadAllText(FilePath));
            return draft?.Format == "universal-editor-draft-1" ? draft : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }
    public void Save() => UserSettings.AtomicWrite(FilePath, JsonSerializer.Serialize(this));
}
