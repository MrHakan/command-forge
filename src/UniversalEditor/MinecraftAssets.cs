using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace UniversalEditor;

// Read the user's installed assets in place; do not download or redistribute game artwork.
internal sealed class MinecraftAssets : IDisposable
{
    private ZipArchive? archive;
    private readonly Dictionary<string, Bitmap?> icons = [];
    private readonly Dictionary<string, Bitmap?> textures = [];
    public string Version { get; private set; } = "26.2";
    public bool Available => archive is not null;
    public string Status { get; private set; } = "Launch/download this version in Prism to enable its artwork.";
    private string? source;

    public bool Open(string prismDirectory, string version = "26.2")
    {
        if (!Regex.IsMatch(version, @"\A[A-Za-z0-9][A-Za-z0-9.-]*\z")) return false;
        string path = Path.Combine(prismDirectory,"libraries","com","mojang","minecraft",version,$"minecraft-{version}-client.jar");
        if (source == path && Available) return false;
        Clear(); source=path; Version=version;
        try { var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);try{archive=new ZipArchive(stream,ZipArchiveMode.Read);}catch{stream.Dispose();throw;} Status=$"Minecraft {version} · local textures"; return true; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException) { Status=$"Minecraft {version} artwork is not available locally."; return false; }
    }
    private static string? PathFor(string resource, string folder, string extension)
    {
        if (!Regex.IsMatch(resource, @"\A[a-z0-9_.-]+:[a-z0-9_/-]+\z") || resource.Contains("..",StringComparison.Ordinal)) return null;
        var parts=resource.Split(':'); return $"assets/{parts[0]}/{folder}/{parts[1]}{extension}";
    }
    private JsonObject? Json(string resource,string folder)
    {
        var path=PathFor(resource,folder,".json"); var entry=path is null ? null : archive?.GetEntry(path);
        if(entry is null || entry.Length>1024*1024) return null;
        using var stream=entry.Open(); return JsonNode.Parse(stream)?.AsObject();
    }
    public Bitmap? Texture(string resource)
    {
        if(textures.TryGetValue(resource,out var cached)){textures.Remove(resource);textures[resource]=cached;return cached;}
        Trim(textures);
        Bitmap? result=null;
        try
        {
            var path=PathFor(resource,"textures",".png");var entry=path is null?null:archive?.GetEntry(path);
            if(entry is not null && entry.Length<=4*1024*1024)
            {
                using var input=entry.Open();using var memory=new MemoryStream();input.CopyTo(memory);var bytes=memory.ToArray();
                if(bytes.Length>=24 && bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
                {
                    int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
                    if(width is >0 and <=1024 && height is >0 and <=16384 && (long)width*height<=1024*1024)
                    {memory.Position=0;using var image=Image.FromStream(memory);result=new Bitmap(width,Math.Min(width,height),PixelFormat.Format32bppArgb);using var g=Graphics.FromImage(result);g.DrawImage(image,new Rectangle(0,0,result.Width,result.Height),new Rectangle(0,0,result.Width,result.Height),GraphicsUnit.Pixel);}
                }
            }
        }
        catch(Exception e) when(e is IOException or ArgumentException or ExternalException or InvalidDataException) { }
        textures[resource]=result;return result;
    }
    public Bitmap? Icon(string category,string id)
    {
        string key=category+"/"+id;if(icons.TryGetValue(key,out var cached)){icons.Remove(key);icons[key]=cached;return cached;}
        Trim(icons);
        Bitmap? result=null;
        try
        {
            if(category=="effects") result=Copy(Texture(id.Replace(":",":mob_effect/")) ?? Texture(id.Replace(":",":gui/sprites/mob_effect/")));
            else if(category=="entities") result=Copy(Icon("items",id+"_spawn_egg"));
            else if(category is "items" or "blocks") result=Item(id,category=="blocks");
            else if(category=="particles" && Json(id,"particles")?["textures"] is JsonArray frames && frames.FirstOrDefault() is JsonValue frame)result=Copy(Texture(frame.GetValue<string>().Replace(":",":particle/")));
            else if(category=="sounds")result=Copy(Texture("minecraft:block/note_block"));
        }
        catch(Exception e) when(e is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or InvalidDataException) { }
        icons[key]=result;return result;
    }
    private static Bitmap? Copy(Bitmap? image)=>image is null?null:new Bitmap(image);
    private static void Trim(Dictionary<string,Bitmap?> cache){if(cache.Count<512)return;var oldest=cache.First();cache.Remove(oldest.Key);oldest.Value?.Dispose();}
    public string[] ResourceIds(string folder,string extension)
    {
        string prefix=$"assets/minecraft/{folder}/";
        return archive?.Entries.Where(e=>e.FullName.StartsWith(prefix,StringComparison.Ordinal)&&e.FullName.EndsWith(extension,StringComparison.Ordinal)&&!e.FullName[prefix.Length..].Contains('/'))
            .Select(e=>"minecraft:"+e.FullName[prefix.Length..^extension.Length]).Order().ToArray()??[];
    }
    public Bitmap? ActionIcon(ActionSpec a)=>a.Kind switch
    {
        ActionKind.GiveItem=>Icon("items",a.Item.Id),ActionKind.SetBlock=>Icon("blocks",a.Resource),ActionKind.Summon=>Icon("entities",a.Resource),ActionKind.Effect=>Icon("effects",a.Resource),ActionKind.Particle=>Icon("particles",a.Resource),
        ActionKind.Message=>Icon("items","minecraft:book"),ActionKind.Teleport=>Icon("items","minecraft:ender_pearl"),ActionKind.Sound=>Icon("blocks","minecraft:note_block"),ActionKind.Time=>CopyOrBorrowTexture("minecraft:item/clock_00"),ActionKind.Weather=>Icon("items","minecraft:water_bucket"),ActionKind.Variable=>Icon("items","minecraft:redstone"),ActionKind.Tag=>Icon("items","minecraft:name_tag"),_=>null
    };
    private Bitmap? CopyOrBorrowTexture(string id)=>Texture(id);
    private static string Resource(string id,string contextNamespace)=>id.Contains(':')?id:contextNamespace+":"+id;
    private static string? Model(JsonNode? node,int depth=0)
    {
        if(node is not JsonObject obj||depth>12)return null;
        string? type=obj["type"]?.GetValue<string>();
        if(type=="minecraft:model")return obj["model"]?.GetValue<string>();
        // Static inventory defaults for conditional/range/select definitions; never claim animated state rendering.
        foreach(string branch in new[]{"on_false","fallback"}){var found=Model(obj[branch],depth+1);if(found is not null)return found;}
        if(obj["models"] is JsonArray children)foreach(var child in children){var found=Model(child,depth+1);if(found is not null)return found;}
        return null;
    }
    private Dictionary<string,string> ModelTextures(string model,HashSet<string> seen,int depth=0)
    {
        var result=new Dictionary<string,string>();if(depth>=16||!seen.Add(model))return result;
        var json=Json(model,"models");if(json is null)return result;string ns=model.Split(':')[0];
        if(json["parent"] is JsonValue parent)result=ModelTextures(Resource(parent.GetValue<string>(),ns),seen,depth+1);
        if(json["textures"] is JsonObject values)foreach(var p in values)
        {
            string? value=p.Value is JsonObject sprite?sprite["sprite"]?.GetValue<string>():p.Value?.GetValue<string>();
            if(value is not null)result[p.Key]=value.StartsWith('#')?value:Resource(value,ns);
        }
        return result;
    }
    private Bitmap? Item(string id,bool block)
    {
        var parts=id.Split(':');if(parts.Length!=2)return null;
        var definition=Json(id,"items")?["model"];
        string? model=Model(definition);
        model ??=parts[0]+":"+(block?"block/":"item/")+parts[1];
        var map=ModelTextures(model,[]);
        string? Resolve(string key)
        {for(int i=0;i<16;i++){if(!map.TryGetValue(key,out var value))return null;if(!value.StartsWith('#'))return value;key=value[1..];}return null;}
        if(Resolve("layer0") is string layer)
        {
            var baseTexture=Texture(layer);if(baseTexture is null)return null;var sprite=new Bitmap(64,64);using var g=Graphics.FromImage(sprite);Pixel(g);g.DrawImage(baseTexture,new Rectangle(0,0,64,64));
            for(int i=1;i<5;i++)if(Resolve("layer"+i) is string overlay&&Texture(overlay) is Bitmap img)g.DrawImage(img,new Rectangle(0,0,64,64));return sprite;
        }
        if(model.Contains(":block/",StringComparison.Ordinal))
        {
            var top=Texture(Resolve("up")??Resolve("top")??Resolve("all")??Resolve("particle")??$"{parts[0]}:block/{parts[1]}");
            var side=Texture(Resolve("north")??Resolve("side")??Resolve("all")??Resolve("particle")??$"{parts[0]}:block/{parts[1]}");
            if(top is not null&&side is not null && IsCube(model,[]))
            {
                Color? tint=Tint(definition);using var tintedTop=tint is null?null:Tinted(top,tint.Value);using var combinedSide=new Bitmap(side);
                if(tint is not null && Resolve("overlay") is string overlay && Texture(overlay) is Bitmap overlayTexture){using var tintedOverlay=Tinted(overlayTexture,tint.Value);using var g=Graphics.FromImage(combinedSide);g.DrawImageUnscaled(tintedOverlay,0,0);}
                return Cube(tintedTop??top,combinedSide);
            }
            return Copy(side??top); // Unsupported 3D models show a texture preview, not an invented cube.
        }
        return Copy(Texture($"{parts[0]}:item/{parts[1]}"));
    }
    private Color? Tint(JsonNode? definition)
    {
        if(definition?["tints"] is not JsonArray tints || tints.FirstOrDefault() is not JsonObject tint)return null;
        if(tint["type"]?.GetValue<string>()=="minecraft:grass" && Texture("minecraft:colormap/grass") is Bitmap map)
        {double temperature=Math.Clamp(tint["temperature"]?.GetValue<double>()??0.5,0,1),downfall=Math.Clamp(tint["downfall"]?.GetValue<double>()??1,0,1)*temperature;return map.GetPixel((int)((1-temperature)*(map.Width-1)),(int)((1-downfall)*(map.Height-1)));}
        if(tint["value"] is JsonValue value && value.TryGetValue<int>(out var rgb))return Color.FromArgb(255,(rgb>>16)&255,(rgb>>8)&255,rgb&255);
        return null;
    }
    private static Bitmap Tinted(Bitmap source,Color tint)
    {
        var result=new Bitmap(source.Width,source.Height);for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++){var pixel=source.GetPixel(x,y);result.SetPixel(x,y,Color.FromArgb(pixel.A,pixel.R*tint.R/255,pixel.G*tint.G/255,pixel.B*tint.B/255));}return result;
    }
    private bool IsCube(string model,HashSet<string> seen,int depth=0)
    {
        if(depth>16||!seen.Add(model))return false;
        if(model.EndsWith("/cube",StringComparison.Ordinal))return true;
        var obj=Json(model,"models");if(obj is null)return false;
        if(obj["elements"] is JsonArray elements)
            return elements.Count>0 && elements.All(e=>e?["rotation"] is null && e?["from"]?.ToJsonString()=="[0,0,0]" && e?["to"]?.ToJsonString()=="[16,16,16]");
        return obj["parent"] is JsonValue parent&&IsCube(Resource(parent.GetValue<string>(),model.Split(':')[0]),seen,depth+1);
    }
    private static Bitmap Cube(Bitmap top,Bitmap side)
    {
        var result=new Bitmap(64,64);using var g=Graphics.FromImage(result);Pixel(g);
        g.DrawImage(top,new Point[]{new(32,3),new(61,18),new(3,18)});
        g.DrawImage(side,new Point[]{new(3,18),new(32,33),new(3,47)});
        g.DrawImage(side,new Point[]{new(32,33),new(61,18),new(32,62)});
        using var shade=new SolidBrush(Color.FromArgb(58,0,0,0));g.FillPolygon(shade,[new(32,33),new(61,18),new(61,47),new(32,62)]);return result;
    }
    internal static void Pixel(Graphics g){g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.SmoothingMode=SmoothingMode.None;}
    internal static void Draw(Graphics g,Image? icon,Rectangle bounds)
    {if(icon is null)return;Pixel(g);int side=Math.Min(bounds.Width,bounds.Height);g.DrawImage(icon,new Rectangle(bounds.X+(bounds.Width-side)/2,bounds.Y+(bounds.Height-side)/2,side,side));}
    private void Clear(){archive?.Dispose();archive=null;foreach(var b in icons.Values)b?.Dispose();foreach(var b in textures.Values)b?.Dispose();icons.Clear();textures.Clear();}
    public void Dispose()=>Clear();
}
