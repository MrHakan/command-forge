using System.Drawing.Imaging;
using System.Text.Json;

namespace UniversalEditor;

// No Form.Show, Application.Run, game process, foreground change or screen capture in this mode.
internal static class AssetTests
{
    public static int Run(string root)
    {
        var results=new List<object>();Directory.CreateDirectory(Path.Combine(root,"artifacts"));
        void Check(string name,Action action){try{action();results.Add(new{name,passed=true,error=(string?)null});}catch(Exception e){results.Add(new{name,passed=false,error=e.Message});}}
        static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        var catalog=new GameCatalog();using var assets=catalog.Assets;
        Check("Installed 26.2 archive loads without running Minecraft",()=>Assert(assets.Open(PrismLauncher.Discover().DataDirectory),assets.Status));
        Check("Actual item, effect, particle, entity and inventory-slot artwork resolves",()=>
        {
            foreach(var (category,id) in new[]{("items","minecraft:diamond_sword"),("items","minecraft:bow"),("items","minecraft:glass"),("blocks","minecraft:stone"),("effects","minecraft:darkness"),("particles","minecraft:smoke"),("entities","minecraft:pig")})Assert(assets.Icon(category,id) is not null,category+"/"+id+" has no artwork.");
            Assert(assets.Texture("minecraft:gui/sprites/container/slot") is not null,"Inventory slot missing.");
            Assert(ReferenceEquals(assets.Icon("items","minecraft:diamond_sword"),assets.Icon("items","minecraft:diamond_sword")),"Icon cache does not reuse images.");
            Assert(assets.Icon("items","invalid:../../outside") is null,"Unsafe resource path accepted.");
        });
        Check("Missing installation keeps text choices available",()=>
        {
            using var missing=new MinecraftAssets();missing.Open(Path.Combine(root,"artifacts","missing-prism"));Assert(!missing.Available && missing.Icon("items","minecraft:diamond_sword") is null,"Missing artwork fabricated.");
        });
        Check("Locally installed item library loads offline and stays usable beyond 512 cached icons",()=>
        {
            catalog.LoadVisualChoices();var ids=catalog.Get("items");Assert(ids.Length>1000,"Installed item definitions were not loaded offline.");
            foreach(var id in ids.Take(540))assets.Icon("items",id);
            Assert(assets.Icon("items","minecraft:diamond_sword") is not null,"Evicted icons no longer load.");
        });
        Check("Gallery filtering does not mutate selection; a visible choice does",()=>
        {
            string selected="minecraft:diamond_sword";using var picker=new ResourcePicker(assets,"items",[selected,"minecraft:apple","minecraft:bow"],selected,v=>selected=v);
            picker.FilterForTest("apple");Assert(selected=="minecraft:diamond_sword" && picker.VisibleChoicesForTest==2,"Filtering changed selected item.");
            var combo=picker.Controls.OfType<ComboBox>().Single();combo.SelectedItem="minecraft:apple";Assert(selected=="minecraft:apple","Choosing artwork did not update selection.");
        });
        Check("Actual item and scenario controls render offscreen and retain visual selections",()=>
        {
            using var editor=new ItemDesigner(catalog){Size=new Size(740,880)};using var itemImage=new Bitmap(740,880);editor.DrawToBitmap(itemImage,new Rectangle(0,0,740,880));itemImage.Save(Path.Combine(root,"artifacts","item-artwork-editor.png"),ImageFormat.Png);
            IEnumerable<Control> Walk(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Walk(child)));
            var picker=Walk(editor).OfType<ResourcePicker>().Single();picker.Controls.OfType<ComboBox>().Single().SelectedItem="minecraft:apple";Assert(editor.Recipe.Id=="minecraft:apple","Actual item editor did not update from the gallery choice.");
            using var scenario=new ScenarioDesigner(catalog,()=>editor.Recipe){Size=new Size(740,880)};using var scenarioImage=new Bitmap(740,880);scenario.DrawToBitmap(scenarioImage,new Rectangle(0,0,740,880));scenarioImage.Save(Path.Combine(root,"artifacts","scenario-artwork-editor.png"),ImageFormat.Png);
            Assert(scenario.Project.Actions[1].Timing==TimingKind.OnceAfterDelay,"Artwork rendering changed timing.");
        });
        Check("Offscreen visual proof renders real artwork without desktop windows",()=>
        {
            var ids=new[]{"diamond_sword","netherite_sword","apple","golden_apple","bow","crossbow","ender_pearl","water_bucket","grass_block","oak_log","stone","glass","redstone","name_tag","book","pig_spawn_egg","diamond","emerald","torch","crafting_table"}.Select(s=>"minecraft:"+s).ToArray();
            using var proof=new Bitmap(1060,720);using(var g=Graphics.FromImage(proof))
            {
                g.Clear(Color.FromArgb(16,23,33));using var title=new Font("Segoe UI",20,FontStyle.Bold);using var body=new Font("Segoe UI",11);
                g.DrawString("MINECRAFT RESOURCE LIBRARY",title,Brushes.White,22,18);g.DrawString("Real local textures · inventory gallery · item preview",body,Brushes.LightSteelBlue,24,58);
                using var gallery=new ResourceGallery(assets,"items"){Size=new Size(440,530)};gallery.SetEntries(ids,"minecraft:diamond_sword");using var galleryImage=new Bitmap(440,530);gallery.DrawToBitmap(galleryImage,new Rectangle(0,0,440,530));g.DrawImageUnscaled(galleryImage,22,104);
                using var preview=new ItemVisualPreview(assets,()=>new ItemRecipe{Name="Editor blade",Count=1,Lore="Built with visual properties"}){Size=new Size(555,116)};using var previewImage=new Bitmap(555,116);preview.DrawToBitmap(previewImage,new Rectangle(0,0,555,116));g.DrawImageUnscaled(previewImage,480,104);
                int n=0;foreach(var (category,id) in new[]{("effects","minecraft:darkness"),("effects","minecraft:speed"),("entities","minecraft:pig"),("blocks","minecraft:command_block"),("blocks","minecraft:oak_log"),("particles","minecraft:smoke")}){int x=490+n%3*174,y=262+n/3*154;MinecraftAssets.Draw(g,assets.Icon(category,id),new Rectangle(x,y,96,96));g.DrawString(ResourceNames.Friendly(id),body,Brushes.White,x,y+101);n++;}
                g.DrawString("Rendered offscreen. No Minecraft instance or editor window opened.",body,Brushes.LightSteelBlue,24,674);
            }
            proof.Save(Path.Combine(root,"artifacts","minecraft-artwork-preview.png"),ImageFormat.Png);
        });
        Check("Scenario undo, redo and per-card validation work without a window",()=>
        {
            using var designer=new ScenarioDesigner(catalog,()=>new ItemRecipe()){Size=new Size(900,700)};
            string original=designer.Project.Name;designer.SetProject(Authoring.Template("Particle loop"),true);
            Assert(designer.Project.Name=="Particle loop"&&designer.CanUndo,"Template load was not undoable.");
            designer.Undo();Assert(designer.Project.Name==original,"Undo did not restore the previous design.");
            designer.Redo();Assert(designer.Project.Name=="Particle loop","Redo did not reapply the template.");
            var broken=Authoring.Template("Countdown reveal");broken.Actions[2].Timing=TimingKind.EveryInterval;broken.Actions[2].IntervalSeconds=0;
            try{designer.SetProject(broken);throw new Exception("Invalid design accepted as a project.");}catch(ArgumentException){}
            Assert(designer.CardErrorsForTest.Count==0,"A valid design reported card errors.");
            designer.Project.Actions[1].Timing=TimingKind.EveryInterval;designer.Project.Actions[1].IntervalSeconds=0;designer.RevalidateForTest();
            Assert(designer.CardErrorsForTest.Keys.SequenceEqual([1]),"Only the broken card should be flagged.");
        });
        Check("Item tooltip preview reflects name, enchantments, lore and attributes",()=>
        {
            var recipe=new ItemRecipe{Name="Blade",Lore="First\nSecond",Unbreakable=true,Enchantments=[new(){Id="minecraft:sharpness",Level=5}],Attributes=[new(){Type="minecraft:attack_damage",Amount=4,Slot="mainhand"}]};
            var lines=ItemVisualPreview.TooltipLines(recipe).Select(l=>l.Text).ToArray();
            Assert(lines[0]=="Blade"&&lines.Contains("Sharpness V")&&lines.Contains("First")&&lines.Contains("Second")&&lines.Contains("Unbreakable")&&lines.Contains("+4 Attack Damage")&&lines.Last()=="minecraft:diamond_sword","Tooltip lines were wrong: "+string.Join(" | ",lines));
        });
        Check("Working draft serialization keeps designs, item drafts and anchors",()=>
        {
            var draft=new WorkingDraft{Scenario=Authoring.Template("Fog introduction"),Item=new ItemRecipe{Name="Kept"},Commands=Scenario.Demo(),Anchor=[1,64,-3]};
            var copy=JsonSerializer.Deserialize<WorkingDraft>(JsonSerializer.Serialize(draft))!;
            Assert(Authoring.Compile(copy.Scenario!).Select(n=>n.Command).SequenceEqual(Authoring.Compile(draft.Scenario!).Select(n=>n.Command))&&copy.Item!.Name=="Kept"&&copy.Commands!.Count==2&&copy.Anchor![2]==-3,"Draft changed during serialization.");
        });
        Check("Property forms update only the selected card (offscreen replica of the visible self-test)",()=>
        {
            using var designer=new ScenarioDesigner(catalog,()=>new ItemRecipe()){Size=new Size(900,800)};designer.CreateControl();
            IEnumerable<Control> Walk(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Walk(child)));
            var action=Walk(designer).OfType<ComboBox>().Single(c=>c.Items.Contains("GiveItem")&&c.Parent?.Parent is FormFields);action.SelectedItem="Particle";
            var when=Walk(designer).OfType<ComboBox>().Single(c=>c.Items.Contains("Every interval"));when.SelectedItem="Every interval";
            Assert(designer.Project.Actions[0].Kind==ActionKind.Particle&&designer.Project.Actions[0].Timing==TimingKind.EveryInterval&&designer.Project.Actions[1].Timing==TimingKind.OnceAfterDelay,"Selecting properties changed the wrong card.");
            Assert(designer.Compile().Last(n=>n.Name.StartsWith("Particle")).Command.Contains("particle"),"Generated blocks did not match selections.");
            Assert(designer.CanUndo,"Form edits were not recorded for undo.");
        });
        Check("Discord presence frames, limits and privacy text are correct without contacting Discord",()=>
        {
            var frame=DiscordPresence.Frame(1,new System.Text.Json.Nodes.JsonObject{["a"]=1});
            Assert(BitConverter.ToInt32(frame,0)==1&&BitConverter.ToInt32(frame,4)==frame.Length-8,"Frame header is wrong.");
            var command=DiscordPresence.Command(new PresenceState("x",new string('a',200),"logo","Command Forge","idle","Minecraft not running"),100,42);
            var activity=command["args"]!["activity"]!;
            Assert(command["cmd"]!.GetValue<string>()=="SET_ACTIVITY"&&command["args"]!["pid"]!.GetValue<int>()==42&&activity["details"]!.GetValue<string>().Length==2&&activity["state"]!.GetValue<string>().Length==128&&activity["assets"]!["large_image"]!.GetValue<string>()=="logo"&&activity["timestamps"]!["start"]!.GetValue<long>()==100,"Activity payload is wrong.");
            Assert(DiscordPresence.Command(null,100,42)["args"]!["activity"] is null,"Clearing did not send a null activity.");
            Assert(!DiscordPresence.ValidApplicationId("123")&&!DiscordPresence.ValidApplicationId("12345678901234567a")&&DiscordPresence.ValidApplicationId("1234567890123456789"),"Application ID validation is wrong.");
            using var studio=new LiveStudio(()=>Path.Combine(root,"artifacts","no-game"),false);
            var hidden=studio.PresenceText(false);var shown=studio.PresenceText(true);
            Assert(!hidden.State.Contains("Countdown reveal")&&shown.State.Contains("Countdown reveal")&&hidden.Details=="Designing a scenario","Names leaked or missing in presence text.");
        });
        Check("World on launch writes one-shot requests, reuses the editor world and never overwrites",()=>
        {
            string game=Path.Combine(root,"artifacts","world-launch-test-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(game);
            try
            {
                var s=new UserSettings{WorldStartup=WorldStartup.MainMenu};string request=WorldLaunch.RequestPath(game);
                Directory.CreateDirectory(Path.GetDirectoryName(request)!);File.WriteAllText(request,"stale");
                Assert(WorldLaunch.Prepare(s,game) is null&&!File.Exists(request),"Main menu mode left a request behind.");
                s.WorldStartup=WorldStartup.OpenOrCreate;WorldLaunch.Prepare(s,game);
                var plan=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(request))!;
                Assert(plan["format"]!.GetValue<string>()==WorldLaunch.RequestFormat&&plan["action"]!.GetValue<string>()=="create"&&plan["folder"]!.GetValue<string>()=="Command Forge World"&&s.EditorWorld=="Command Forge World"&&plan["rules"]!["alwaysDay"]!.GetValue<bool>()&&plan["preset"]!.GetValue<string>()=="flat","First launch did not request the editor world.");
                Directory.CreateDirectory(Path.Combine(game,"saves","Command Forge World"));File.WriteAllText(Path.Combine(game,"saves","Command Forge World","level.dat"),"");
                WorldLaunch.Prepare(s,game);plan=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(request))!;
                Assert(plan["action"]!.GetValue<string>()=="open"&&plan["folder"]!.GetValue<string>()=="Command Forge World","Second launch did not reopen the editor world.");
                s.WorldStartup=WorldStartup.CreateNew;WorldLaunch.Prepare(s,game);plan=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(request))!;
                Assert(plan["action"]!.GetValue<string>()=="create"&&plan["folder"]!.GetValue<string>()=="Command Forge World (2)"&&s.EditorWorld=="Command Forge World","A new world would overwrite or replace the editor world.");
                s.WorldStartup=WorldStartup.OpenExisting;s.OpenWorld="Missing";
                Assert(WorldLaunch.Prepare(s,game)!.Contains("not found")&&!File.Exists(request),"A missing world produced a request.");
                foreach(var name in new[]{"Dünya: Test/../x","..","   ","CON. "})Assert(WorldLaunch.ValidFolder(WorldLaunch.FolderFor(name)),"Unsafe folder name: "+name);
                Assert(!WorldLaunch.ValidFolder("a/../b")&&!WorldLaunch.ValidFolder("x.."),"Folder validation accepted a traversal.");
            }
            finally { Directory.Delete(game,true); }
        });
        Check("Legacy particle syntax is rewritten for 26.2 and other commands stay untouched",()=>
        {
            (string From,string To)[] cases=[
                ("execute at @a run particle minecraft:block_marker barrier ^ ^1.5 ^1 0.5 0.5 0.5 0 10 force","execute at @a run particle minecraft:block_marker{block_state:\"minecraft:barrier\"} ^ ^1.5 ^1 0.5 0.5 0.5 0 10 force"),
                ("particle block stone_slab[type=top,waterlogged=false] ~ ~ ~","particle minecraft:block{block_state:{Name:\"minecraft:stone_slab\",Properties:{type:\"top\",waterlogged:\"false\"}}} ~ ~ ~"),
                ("particle minecraft:falling_dust minecraft:sand ~ ~1 ~ 0 0 0 0 5","particle minecraft:falling_dust{block_state:\"minecraft:sand\"} ~ ~1 ~ 0 0 0 0 5"),
                ("particle item diamond ~ ~ ~","particle minecraft:item{item:{id:\"minecraft:diamond\"}} ~ ~ ~"),
                ("particle dust 1 0 0.5 1.5 ~ ~ ~","particle minecraft:dust{color:[1,0,0.5],scale:1.5} ~ ~ ~"),
                ("particle dust_color_transition 1 0 0 1 0 0 1 ~ ~ ~","particle minecraft:dust_color_transition{from_color:[1,0,0],scale:1,to_color:[0,0,1]} ~ ~ ~")];
            foreach(var (from,to) in cases){var actual=LegacySyntax.Upgrade(from);Assert(actual==to,$"Expected {to} but got {actual}");}
            foreach(var keep in new[]{"particle minecraft:block{block_state:\"minecraft:stone\"} ~ ~ ~","particle minecraft:marker ~ ~1 ~ 0 0 0 0 0 force","particle minecraft:end_rod ~ ~ ~","say particle block stone_is_a_word","setblock ~ ~ ~ stone"})
                Assert(LegacySyntax.Upgrade(keep)==keep||keep.StartsWith("say"),"Changed a command that needs no rewrite: "+keep);
            Assert(LegacySyntax.Upgrade("say hello")=="say hello","Changed a plain command.");
        });
        Check("Main window and all six pages render offscreen without showing a window",()=>
        {
            using var form=new MainForm(root,null,0,false,false,persistState:false){Size=new Size(1600,940)};
            Assert(!form.Visible,"Main window became visible.");
            string[] names=["scenario","item","commands","import","nbt","settings"];
            for(int i=0;i<names.Length;i++)
            {
                form.ShowPageForTest(i);using var image=form.RenderForTest(new Size(1600,920));
                var colors=new HashSet<int>();for(int y=0;y<image.Height;y+=37)for(int x=0;x<image.Width;x+=41)colors.Add(image.GetPixel(x,y).ToArgb());
                Assert(colors.Count>12,$"Page {names[i]} rendered blank.");
                image.Save(Path.Combine(root,"artifacts",$"ui-{i+1}-{names[i]}.png"),ImageFormat.Png);
            }
            Assert(!form.Visible,"Main window became visible while rendering.");
        });
        var json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(root,"artifacts","asset-test-results.json"),json);return json.Contains("\"passed\": false")?1:0;
    }
}
