using System.ComponentModel;
using System.Globalization;

namespace UniversalEditor;

internal static class ResourceNames
{
    public static string Friendly(string id)=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id[(id.IndexOf(':')+1)..].Replace('_',' '));
}
internal sealed class ResourceGallery : ScrollableControl
{
    private readonly MinecraftAssets assets;
    private readonly string category;
    private readonly ToolTip tooltip=new();
    private string[] visible=[];
    private string selected="";
    private int columns=4;
    private int hover=-1;
    private bool revealPending;
    private static int CellW=>Ui.S(74);
    private static int CellH=>Ui.S(90);
    private static int Tile=>Ui.S(62);
    public event Action<string>? Picked;
    public ResourceGallery(MinecraftAssets assets,string category)
    {
        this.assets=assets;this.category=category;DoubleBuffered=true;AutoScroll=true;BackColor=Ui.Canvas;Ui.DarkNative(this);
        MouseDown+=(_,e)=>{int index=Index(e.Location);if(index>=0&&index<visible.Length){selected=visible[index];Invalidate();Picked?.Invoke(selected);Focus();}};
        MouseMove+=(_,e)=>{int index=Index(e.Location);if(index==hover)return;hover=index;Invalidate();tooltip.SetToolTip(this,index>=0&&index<visible.Length?ResourceNames.Friendly(visible[index])+"\n"+visible[index]:"");};
        MouseLeave+=(_,_)=>{hover=-1;Invalidate();};
        KeyDown+=(_,e)=>{int i=Array.IndexOf(visible,selected);int delta=e.KeyCode switch{Keys.Right=>1,Keys.Left=>-1,Keys.Down=>columns,Keys.Up=>-columns,_=>0};if(delta==0||visible.Length==0)return;i=Math.Clamp(i+delta,0,visible.Length-1);selected=visible[i];Picked?.Invoke(selected);EnsureVisible(i);Invalidate();e.Handled=true;};
        GotFocus+=(_,_)=>Invalidate();LostFocus+=(_,_)=>Invalidate();
        SizeChanged+=(_,_)=>LayoutCells();TabStop=true;
    }
    public void SetEntries(IEnumerable<string> entries,string value){visible=entries.ToArray();selected=value;AutoScrollPosition=Point.Empty;revealPending=true;LayoutCells();}
    public void SelectResource(string value){selected=value;int i=Array.IndexOf(visible,value);if(i>=0)EnsureVisible(i);Invalidate();}
    private void EnsureVisible(int index)
    {
        int top=Ui.S(8)+index/columns*CellH,scroll=-AutoScrollPosition.Y;
        if(top<scroll)AutoScrollPosition=new Point(0,top-Ui.S(8));else if(top+CellH>scroll+ClientSize.Height)AutoScrollPosition=new Point(0,top+CellH-ClientSize.Height+Ui.S(8));
    }
    private int Index(Point point){int x=point.X-Ui.S(8),y=point.Y-AutoScrollPosition.Y-Ui.S(8);if(x<0||y<0||x/CellW>=columns)return -1;return y/CellH*columns+x/CellW;}
    protected override bool IsInputKey(Keys keyData)=> (keyData&Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    private void LayoutCells(){columns=Math.Max(1,(ClientSize.Width-Ui.S(16))/CellW);AutoScrollMinSize=new Size(0,((visible.Length+columns-1)/columns)*CellH+Ui.S(16));if(revealPending&&ClientSize.Width>CellW*2&&ClientSize.Height>CellH){revealPending=false;int i=Array.IndexOf(visible,selected);if(i>=0)EnsureVisible(i);}Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;using var border=new Pen(Ui.Border);using var selectedBorder=new Pen(Ui.Accent,Ui.S(2));using var hoverBorder=new Pen(Ui.Muted);using var tile=new SolidBrush(Ui.Raised);using var missingFont=new Font("Segoe UI",18,FontStyle.Bold);
        int first=Math.Max(0,(-AutoScrollPosition.Y-Ui.S(8))/CellH*columns),last=Math.Min(visible.Length,first+(ClientSize.Height/CellH+3)*columns);
        var slot=assets.Texture("minecraft:gui/sprites/container/slot");
        for(int i=first;i<last;i++)
        {
            var rect=new Rectangle(Ui.S(8)+i%columns*CellW+(CellW-Tile)/2,Ui.S(8)+i/columns*CellH+AutoScrollPosition.Y,Tile,Tile);g.FillRectangle(tile,rect);MinecraftAssets.Draw(g,slot,rect);
            var icon=assets.Icon(category,visible[i]);if(icon is not null)MinecraftAssets.Draw(g,icon,Rectangle.Inflate(rect,-Ui.S(8),-Ui.S(8)));else TextRenderer.DrawText(g,"?",missingFont,rect,Ui.Subtle,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            g.DrawRectangle(visible[i]==selected?selectedBorder:i==hover?hoverBorder:border,rect);
            TextRenderer.DrawText(g,ResourceNames.Friendly(visible[i]),Ui.Small,new Rectangle(rect.X-Ui.S(5),rect.Bottom+Ui.S(3),CellW,Ui.S(18)),visible[i]==selected?Ui.Text:Ui.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPrefix);
        }
        if(visible.Length==0)TextRenderer.DrawText(g,"No matches. Try a shorter name or the resource ID.",Ui.Body,ClientRectangle,Ui.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        if(Focused){using var focus=new Pen(Ui.Border);g.DrawRectangle(focus,0,0,ClientSize.Width-1,ClientSize.Height-1);}
    }
    protected override void Dispose(bool disposing){if(disposing)tooltip.Dispose();base.Dispose(disposing);}
}

internal sealed class ResourcePicker : UserControl
{
    private readonly ComboBox names=new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,DrawMode=DrawMode.OwnerDrawFixed,FlatStyle=FlatStyle.Flat,ItemHeight=Ui.S(30),DropDownHeight=Ui.S(360),DropDownWidth=Ui.S(380),BackColor=Ui.Input,ForeColor=Ui.Text};
    private readonly TextBox search=Ui.TextInput();
    private readonly Label count=new(){Dock=DockStyle.Right,AutoSize=false,Width=Ui.S(110),TextAlign=ContentAlignment.MiddleRight,Font=Ui.Small,ForeColor=Ui.Subtle};
    private readonly ResourceGallery gallery;
    private readonly string[] entries;
    private string selected;
    private bool filtering;
    public ResourcePicker(MinecraftAssets assets,string category,IEnumerable<string> entries,string selected,Action<string> change)
    {
        this.entries=entries.Distinct().ToArray();this.selected=selected;Height=Ui.S(300);Width=Ui.S(310);
        gallery=new ResourceGallery(assets,category){Dock=DockStyle.Fill};
        search.PlaceholderText="Search by name or ID…";search.Dock=DockStyle.Fill;Ui.DarkNative(names);
        var searchLine=new Panel{Dock=DockStyle.Top,Height=search.Height};searchLine.Controls.Add(search);searchLine.Controls.Add(count);
        Controls.Add(gallery);Controls.Add(new Panel{Dock=DockStyle.Top,Height=Ui.S(6)});Controls.Add(searchLine);Controls.Add(new Panel{Dock=DockStyle.Top,Height=Ui.S(6)});Controls.Add(names);
        names.Items.AddRange(this.entries.Cast<object>().ToArray());if(!names.Items.Contains(selected))names.Items.Add(selected);names.SelectedItem=selected;gallery.SetEntries(this.entries,selected);Count(this.entries.Length);
        names.DrawItem+=(_,e)=>
        {
            if(e.Index<0||e.Index>=names.Items.Count||names.Items[e.Index] is not string id)return;bool active=(e.State&DrawItemState.Selected)!=0&&(e.State&DrawItemState.ComboBoxEdit)==0;
            using var background=new SolidBrush(active?Ui.AccentDim:Ui.Input);e.Graphics.FillRectangle(background,e.Bounds);MinecraftAssets.Draw(e.Graphics,assets.Icon(category,id),new Rectangle(e.Bounds.X+Ui.S(4),e.Bounds.Y+Ui.S(2),e.Bounds.Height-Ui.S(4),e.Bounds.Height-Ui.S(4)));
            int x=e.Bounds.X+e.Bounds.Height+Ui.S(6);
            TextRenderer.DrawText(e.Graphics,ResourceNames.Friendly(id),Ui.Strong,new Rectangle(x,e.Bounds.Y,e.Bounds.Width-x,e.Bounds.Height),Ui.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            var idWidth=TextRenderer.MeasureText(ResourceNames.Friendly(id),Ui.Strong).Width+Ui.S(10);
            TextRenderer.DrawText(e.Graphics,id,Ui.Small,new Rectangle(x+idWidth,e.Bounds.Y,Math.Max(0,e.Bounds.Right-x-idWidth-Ui.S(4)),e.Bounds.Height),Ui.Subtle,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        };
        names.SelectedIndexChanged+=(_,_)=>{if(filtering||names.SelectedItem is not string id)return;this.selected=id;gallery.SelectResource(id);change(id);};
        gallery.Picked+=id=>{names.SelectedItem=id;if(names.SelectedItem is null){filtering=true;names.Items.Add(id);filtering=false;names.SelectedItem=id;}};
        search.TextChanged+=(_,_)=>
        {
            var query=search.Text.Trim().Replace('_',' ');
            var results=this.entries.Where(v=>v.Replace('_',' ').Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();filtering=true;
            names.BeginUpdate();names.Items.Clear();names.Items.AddRange(results.Cast<object>().ToArray());if(!names.Items.Contains(this.selected))names.Items.Add(this.selected);names.SelectedItem=this.selected;names.EndUpdate();filtering=false;
            gallery.SetEntries(results,this.selected);Count(results.Length);
        };
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Down){gallery.Focus();e.Handled=e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){search.Clear();e.Handled=e.SuppressKeyPress=true;}};
    }
    private void Count(int shown)=>count.Text=shown==entries.Length?$"{entries.Length} available":$"{shown} of {entries.Length}";
    internal void FilterForTest(string text)=>search.Text=text;
    internal int VisibleChoicesForTest=>names.Items.Count;
}

// Large item slot plus an approximation of Minecraft's tooltip for the current recipe.
internal sealed class ItemVisualPreview : Control
{
    private readonly MinecraftAssets assets;
    private readonly Func<ItemRecipe> recipe;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string? Error { get; set; }
    public ItemVisualPreview(MinecraftAssets assets,Func<ItemRecipe> recipe){this.assets=assets;this.recipe=recipe;Height=Ui.S(176);Dock=DockStyle.Top;DoubleBuffered=true;}
    internal static List<(string Text,Color Color,FontStyle Style)> TooltipLines(ItemRecipe r)
    {
        var lines=new List<(string,Color,FontStyle)>();var style=(r.Bold?FontStyle.Bold:FontStyle.Regular)|(r.Italic?FontStyle.Italic:FontStyle.Regular);
        lines.Add(r.Name.Length>0?(r.Name,McColors.Of(r.Color),style):(ResourceNames.Friendly(r.Id),Color.White,FontStyle.Regular));
        foreach(var e in r.Enchantments)lines.Add(($"{ResourceNames.Friendly(e.Id)} {ActionText.Roman(e.Level)}",McColors.Of("gray"),FontStyle.Regular));
        if(r.Lore.Length>0)foreach(var line in r.Lore.Replace("\r","").Split('\n'))lines.Add((line,McColors.Of(r.Color),style));
        if(r.Unbreakable)lines.Add(("Unbreakable",McColors.Of("blue"),FontStyle.Regular));
        foreach(var group in r.Attributes.GroupBy(a=>a.Slot))
        {
            lines.Add(("",Color.White,FontStyle.Regular));lines.Add(($"When {(group.Key=="any"?"in any slot":"on "+ItemDesigner.SlotName(group.Key))}:",McColors.Of("gray"),FontStyle.Regular));
            foreach(var a in group)
            {
                bool percent=a.Operation!="add_value";decimal shown=percent?a.Amount*100:a.Amount;
                lines.Add(($"{(shown>=0?"+":"")}{shown.ToString("0.###",CultureInfo.InvariantCulture)}{(percent?"%":"")} {ResourceNames.Friendly(a.Type)}",McColors.Of(shown>=0?"blue":"red"),FontStyle.Regular));
            }
        }
        if(r.Damage is int damage && damage>0)lines.Add(($"Damage taken: {damage}",McColors.Of("gray"),FontStyle.Regular));
        lines.Add((r.Id,McColors.Of("dark_gray"),FontStyle.Regular));
        if(r.Preserved.Count>0)lines.Add(($"+{r.Preserved.Count} preserved component{(r.Preserved.Count==1?"":"s")}",McColors.Of("dark_gray"),FontStyle.Regular));
        return lines;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;var r=recipe();g.Clear(Parent?.BackColor??Ui.Surface);
        var slot=new Rectangle(Ui.S(16),Ui.S(16),Ui.S(104),Ui.S(104));MinecraftAssets.Draw(g,assets.Texture("minecraft:gui/sprites/container/slot"),slot);
        var icon=assets.Icon("items",r.Id);
        if(icon is not null)MinecraftAssets.Draw(g,icon,Rectangle.Inflate(slot,-Ui.S(14),-Ui.S(14)));else{using var border=new Pen(Ui.Border);g.DrawRectangle(border,slot);TextRenderer.DrawText(g,"?",Ui.Title,slot,Ui.Subtle,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        if(r.Count>1){using var countFont=new Font("Segoe UI",13,FontStyle.Bold);var countBox=new Rectangle(slot.X,slot.Bottom-Ui.S(34),slot.Width-Ui.S(10),Ui.S(28));TextRenderer.DrawText(g,r.Count.ToString(CultureInfo.InvariantCulture),countFont,new Rectangle(countBox.X+2,countBox.Y+2,countBox.Width,countBox.Height),Color.FromArgb(63,63,63),TextFormatFlags.Right|TextFormatFlags.Bottom);TextRenderer.DrawText(g,r.Count.ToString(CultureInfo.InvariantCulture),countFont,countBox,Color.White,TextFormatFlags.Right|TextFormatFlags.Bottom);}
        TextRenderer.DrawText(g,assets.Available?$"Minecraft {assets.Version} artwork":"Artwork unavailable",Ui.Small,new Rectangle(slot.X-Ui.S(4),slot.Bottom+Ui.S(8),slot.Width+Ui.S(8),Ui.S(32)),Ui.Subtle,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        // tooltip box
        var lines=TooltipLines(r);int lineHeight=Ui.S(20);int x=slot.Right+Ui.S(20),available=Math.Max(Ui.S(40),Width-x-Ui.S(12));
        int maxLines=Math.Max(1,(Height-Ui.S(40))/lineHeight);bool clipped=lines.Count>maxLines;if(clipped)lines=[..lines.Take(maxLines-1),("…",McColors.Of("dark_gray"),FontStyle.Regular)];
        var fonts=new Dictionary<FontStyle,Font>();Font FontFor(FontStyle s){if(!fonts.TryGetValue(s,out var f))fonts[s]=f=new Font("Segoe UI",10f,s);return f;}
        int textWidth=lines.Max(l=>TextRenderer.MeasureText(l.Text.Length==0?" ":l.Text,FontFor(l.Style)).Width);
        var box=new Rectangle(x,Ui.S(14),Math.Min(available,textWidth+Ui.S(24)),lines.Count*lineHeight+Ui.S(16));
        using(var back=new SolidBrush(Color.FromArgb(242,16,0,16)))g.FillRectangle(back,box);
        using(var outer=new Pen(Color.FromArgb(16,0,16)))g.DrawRectangle(outer,box);
        using(var inner=new System.Drawing.Drawing2D.LinearGradientBrush(box,Color.FromArgb(80,0,255),Color.FromArgb(40,0,127),90f)){using var pen=new Pen(inner,Ui.S(2));g.DrawRectangle(pen,Rectangle.Inflate(box,-Ui.S(3),-Ui.S(3)));}
        for(int i=0;i<lines.Count;i++)
        {
            var (text,color,style)=lines[i];var rect=new Rectangle(box.X+Ui.S(11),box.Y+Ui.S(8)+i*lineHeight,box.Width-Ui.S(18),lineHeight);
            var shadow=Color.FromArgb(color.R/4,color.G/4,color.B/4);
            TextRenderer.DrawText(g,text,FontFor(style),new Rectangle(rect.X+1,rect.Y+1,rect.Width,rect.Height),shadow,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g,text,FontFor(style),rect,color,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
        foreach(var f in fonts.Values)f.Dispose();
        string footer=Error is not null?"⚠ "+Error:"Approximate tooltip · the game renders the final item";
        TextRenderer.DrawText(g,footer,Ui.Small,new Rectangle(x,box.Bottom+Ui.S(6),available,Ui.S(18)),Error is not null?Ui.Danger:Ui.Subtle,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
}
