using System.Diagnostics;
using System.Text.Json;

namespace UniversalEditor;

internal static class SelfTests
{
    // headless skips the two checks that open desktop windows (native embedding and the shown designer form).
    public static int Run(string root, bool headless = false)
    {
        var results = new List<object>(); var skipped = new List<string>();
        string[] windowed = ["Native child window attaches, resizes and detaches across processes", "Visual property selections update the model without changing other cards"];
        var directory = Path.Combine(root, "artifacts", "self-test"); Directory.CreateDirectory(directory);
        void Check(string name, Action action)
        {
            if (headless && windowed.Contains(name)) { skipped.Add(name); return; }
            try { action(); results.Add(new { name, passed = true, error = (string?)null }); }
            catch (Exception ex) { results.Add(new { name, passed = false, error = ex.Message }); }
        }
        var tag = new NbtTag(NbtType.Compound, "root", new List<NbtTag>
        {
            new(NbtType.Byte, "byte", (sbyte)-128), new(NbtType.Short, "short", short.MinValue),
            new(NbtType.Int, "int", int.MaxValue), new(NbtType.Long, "long", long.MinValue),
            new(NbtType.Float, "float", -1.25f), new(NbtType.Double, "double", Math.PI),
            new(NbtType.String, "text", "null\0; emoji 🐉; surrogate \ud800"),
            new(NbtType.ByteArray, "bytes", new byte[] { 0, 128, 255 }),
            new(NbtType.IntArray, "ints", new[] { int.MinValue, 0, int.MaxValue }),
            new(NbtType.LongArray, "longs", new[] { long.MinValue, 0, long.MaxValue }),
            new(NbtType.List, "list", new List<NbtTag> { new(NbtType.Int, "", 42) }) { ElementType = NbtType.Int },
            new(NbtType.Compound, "nested", new List<NbtTag> { new(NbtType.String, "unknown_preserved", "yes") })
        });
        void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
        Check("All 12 payload types and Java modified UTF-8 round-trip", () =>
        {
            var bytes = NbtCodec.Write(tag); var parsed = NbtCodec.Read(bytes);
            Assert(bytes.SequenceEqual(NbtCodec.Write(parsed)), "Binary round-trip changed bytes.");
            Assert(((List<NbtTag>)parsed.Value)[6].DisplayValue() == ((List<NbtTag>)tag.Value)[6].DisplayValue(), "String code units changed.");
        });
        Check("External big-endian NBT fixture", () =>
        {
            byte[] bytes = [10, 0, 0, 3, 0, 1, 120, 0, 0, 0, 42, 0];
            var parsed = NbtCodec.Read(bytes); Assert((int)((List<NbtTag>)parsed.Value)[0].Value == 42, "Big-endian value was misread.");
            Assert(bytes.SequenceEqual(NbtCodec.Write(parsed)), "Fixture was altered.");
        });
        foreach (var compression in Enum.GetValues<NbtCompression>())
            Check($"{compression} file preserves NBT and rejects overwrite", () =>
            {
                var path = Path.Combine(directory, Guid.NewGuid() + ".dat");
                NbtCodec.SaveCopy(new NbtDocument(tag, compression), path);
                var parsed = NbtCodec.ReadFile(path);
                Assert(parsed.Compression == compression && NbtCodec.Write(parsed.Root).SequenceEqual(NbtCodec.Write(tag)), "Compressed file changed its content.");
                try { NbtCodec.SaveCopy(parsed, path); throw new Exception("Existing file was overwritten."); } catch (IOException) { }
            });
        Check("Truncated, unknown and trailing data are rejected", () =>
        {
            foreach (var bytes in new byte[][] { [10, 0], [13, 0, 0], [1, 0, 0, 42, 0] })
            {
                try { NbtCodec.Read(bytes); throw new Exception("Invalid input was accepted."); }
                catch (InvalidDataException) { } catch (EndOfStreamException) { }
            }
        });
        Check("Signed numeric edits and type range failures", () =>
        {
            var value = new NbtTag(NbtType.Byte, "", (sbyte)0); value.SetText("-128"); Assert((sbyte)value.Value == -128, "Signed byte edit failed.");
            try { value.SetText("128"); throw new Exception("Out-of-range byte accepted."); } catch (OverflowException) { }
            Assert((sbyte)value.Value == -128, "Failed edit mutated value.");
        });
        Check("103 releases include 26.2 and 26.3", () =>
        {
            var versions = VersionCatalog.Load(root); Assert(versions.Count == 103, "Missing releases.");
            Assert(versions.Any(v => v.Id == "26.2" && v.JavaMajor == 25 && v.ProtocolId == 776), "26.2 metadata incorrect.");
            Assert(versions[0].Id == "26.3", "Newest release ordering incorrect.");
        });
        Check("Managed instance memory validation and ownership guard", () =>
        {
            var path = Path.Combine(directory, "prism-" + Guid.NewGuid()); Directory.CreateDirectory(path);
            var launcher = new PrismLauncher(path, Environment.ProcessPath!);
            var version = new GameVersion("26.2", 25, 776, false);
            try { launcher.Prepare(version, 4096, 1024); throw new Exception("Invalid memory accepted."); } catch (ArgumentException) { }
            var foreign = Path.Combine(path, "instances", "universal-editor-26.2"); Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "instance.cfg"), "untouched");
            try { launcher.Prepare(version, 1024, 4096); throw new Exception("Foreign instance accepted."); } catch (IOException) { }
            Assert(File.ReadAllText(Path.Combine(foreign, "instance.cfg")) == "untouched", "Foreign instance was changed.");
            var ownVersion = new GameVersion("26.1", 25, 775, false);
            var id = launcher.Prepare(ownVersion, 1024, 3072);
            var config = File.ReadAllText(Path.Combine(path, "instances", id, "instance.cfg"));
            Assert(config.Contains("MaxMemAlloc=3072") && config.Contains("PreLaunchCommand=\n"), "RAM/config values incorrect.");
        });
        Check("Native child window attaches, resizes and detaches across processes", () =>
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(Program).Assembly.GetName().Name + ".dll"));
            info.ArgumentList.Add("--probe-window");
            using var child = Process.Start(info) ?? throw new Exception("Probe process did not start.");
            using var host = new Form { Width = 900, Height = 600, Text = "Command Forge embedding test" };
            using var panel = new NativeViewport { Dock = DockStyle.Fill }; host.Controls.Add(panel); host.Show();
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (child.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
                { Application.DoEvents(); Thread.Sleep(50); child.Refresh(); }
                var handle = child.MainWindowHandle; Assert(handle != IntPtr.Zero, "Probe window did not appear.");
                var oldParent = NativeMethods.GetParent(handle); var oldStyle = NativeMethods.GetWindowLongPtrW(handle, -16);
                panel.Attach(new GameWindow(handle, child.Id, "probe")); Application.DoEvents();
                Assert(NativeMethods.GetParent(handle) == panel.Handle, "Wrong parent after attach.");
                host.Width += 180; Application.DoEvents(); NativeMethods.GetWindowRect(handle, out var rect);
                Assert(rect.Right - rect.Left == panel.Width && rect.Bottom - rect.Top == panel.Height, "Resize did not match viewport.");
                panel.Detach(); Assert(NativeMethods.GetParent(handle) == oldParent && NativeMethods.GetWindowLongPtrW(handle, -16) == oldStyle, "Original window state not restored.");
            }
            finally { if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(3000)) child.Kill(); } }
        });
        Check("Nested installer strings decode without executing commands", () =>
        {
            const string fixture = "summon falling_block ~ ~ ~ {Passengers:[{id:command_block_minecart,Command:\"scoreboard objectives add demo dummy\"},{id:command_block_minecart,Command:\"setblock ~ ~ ~ repeating_command_block[facing=south]{Command:'title @a actionbar {text:\\\"Hello\\\"}',auto:1}\"}]}";
            var imported = Scenario.Import(fixture);
            Assert(imported.Nodes.Count == 1 && imported.InstallerCommands.Count == 1, "Installer/block separation incorrect.");
            Assert(imported.Nodes[0].Command == "title @a actionbar {text:\"Hello\"}", "Nested quotes were corrupted.");
            Assert(Scenario.ExtractCommands("{Note:'Command: fake',Command:'say ok'}").SequenceEqual(new[] { "say ok" }), "A string literal was interpreted as a field.");
            try { Scenario.Import("{Command:'unterminated}"); throw new Exception("Malformed installer accepted."); } catch (InvalidDataException) { }
        });
        Check("Live IPC requires a fresh session and allows emergency stop to supersede", () =>
        {
            var path = Path.Combine(directory, "live-" + Guid.NewGuid()); var c = new LiveConnection(path);
            c.Poll(); Assert(!c.Ready, "Missing bridge was marked online.");
            Directory.CreateDirectory(c.DirectoryPath);
            var statePath = Path.Combine(c.DirectoryPath, "state.json");
            var state = new System.Text.Json.Nodes.JsonObject { ["session"] = "test-session", ["version"] = "26.2", ["connected"] = true,
                ["heartbeat"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["requestId"] = "" };
            File.WriteAllText(statePath, state.ToJsonString()); c.Poll(); Assert(c.Ready, "Fresh bridge was offline.");
            try { c.Send(new System.Text.Json.Nodes.JsonObject { ["operation"] = "install", ["command"] = new string('界', 400000) }); throw new Exception("Oversized request accepted."); } catch (IOException) { }
            Assert(c.PendingId is null, "Rejected oversized request blocked later requests.");
            var first = c.Send(new System.Text.Json.Nodes.JsonObject { ["operation"] = "install" });
            try { c.Send(new System.Text.Json.Nodes.JsonObject { ["operation"] = "item" }); throw new Exception("Concurrent mutation accepted."); } catch (IOException) { }
            var stop = c.Send(new System.Text.Json.Nodes.JsonObject { ["operation"] = "stop" }); Assert(stop != first, "Stop was not submitted.");
            state["requestId"] = stop; File.WriteAllText(statePath, state.ToJsonString()); c.Poll(); Assert(c.PendingId is null, "Acknowledgement did not resolve pending request.");
            state["heartbeat"] = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds(); File.WriteAllText(statePath, state.ToJsonString()); c.Poll();
            Assert(!c.Ready, "Stale heartbeat was accepted.");
            try { c.Send(new System.Text.Json.Nodes.JsonObject { ["operation"] = "item" }); throw new Exception("Offline mutation accepted."); } catch (IOException) { }
        });
        Check("Countdown timing is sequential and interval offsets are explicit", () =>
        {
            var countdown=Authoring.Compile(Authoring.Template("Countdown reveal"));
            Assert(new[]{1,21,41,61}.Select((tick,i)=>countdown[i+1].Command.Contains($"matches {tick} as")).All(v=>v),"Countdown times changed.");
            var project=new DesignerProject {Actions=[new() {Kind=ActionKind.Particle,Timing=TimingKind.EveryInterval,DelaySeconds=3,IntervalSeconds=0.25m,EndSeconds=10}]};
            var nodes=Authoring.Compile(project);
            Assert(nodes.Any(n=>n.Command.EndsWith(" 5")) && nodes.Any(n=>n.Command.EndsWith(" 61")) && nodes.Last().Command.Contains("matches 61..201"),"Interval/window ticks are incorrect.");
            project.Actions[0].IntervalSeconds=0;try{Authoring.Compile(project);throw new Exception("Zero interval accepted.");}catch(ArgumentException){}
        });
        Check("Variable branches compile to executable mutually exclusive conditions", () =>
        {
            var p=new DesignerProject {Actions=[new(){Kind=ActionKind.Variable,VariableValue=1},new(){ConditionVariable="phase",ConditionValue=1},new(){ConditionVariable="phase",ConditionValue=1,Comparison="Not equal"}]};
            var nodes=Authoring.Compile(p);
            Assert(nodes[1].Command.Contains($"run scoreboard players set #v_phase {p.Objective} 1") && !nodes[1].Command.Contains("as @a"),"Shared variable is multiplied by players.");
            Assert(nodes[2].Command.Contains($"if score #v_phase {p.Objective} matches 1") && nodes[3].Command.Contains($"unless score #v_phase {p.Objective} matches 1"),"Branches have no actual condition.");
            var loaded=JsonSerializer.Deserialize<DesignerProject>(JsonSerializer.Serialize(p))!;Assert(Authoring.Compile(loaded).Select(n=>n.Command).SequenceEqual(nodes.Select(n=>n.Command)),"Saved design changed behavior.");
        });
        Check("Item forms escape text, keep unknown typed SNBT and modifier identities", () =>
        {
            var item=System.Text.Json.Nodes.JsonNode.Parse("""{"id":"minecraft:diamond_sword","count":1,"components":{"minecraft:custom_data":{"flag":1,"bytes":[1,-2]},"minecraft:attribute_modifiers":[{"id":"test:original","type":"minecraft:attack_damage","amount":2.5,"operation":"add_value","slot":"mainhand"}]}}""")!.AsObject();
            var r=ItemRecipe.Read(item,"{id:\"minecraft:diamond_sword\",count:1,components:{\"minecraft:custom_data\":{flag:1b,bytes:[B;1b,-2b]},\"minecraft:attribute_modifiers\":[]}}");
            r.Name="Quote \" and slash \\";r.Lore="Line 1\nLine 2";
            string argument=r.Argument();Assert(argument.Contains("flag:1b,bytes:[B;1b,-2b]") && argument.Contains("test:original") && r.Components()["minecraft:lore"]!.AsArray().Count==2,"Typed data/identity/lore changed.");
            Assert(r.Components()["minecraft:custom_name"]!["text"]!.GetValue<string>()==r.Name,"Name escaped incorrectly.");
            r.Enchantments=[new(),new()];try{r.Argument();throw new Exception("Duplicate enchantment accepted.");}catch(ArgumentException){}
        });
        Check("Designer block budget includes interval helper commands", () =>
        {
            var p=new DesignerProject {Actions=Enumerable.Range(0,26).Select(_=>new ActionSpec {Kind=ActionKind.Particle,Timing=TimingKind.EveryInterval}).ToList()};
            try{Authoring.Compile(p);throw new Exception("131-block design accepted.");}catch(ArgumentException){}
            try{SnbtSlices.Compound("{x:[B;1b}");throw new Exception("Malformed preserved SNBT accepted.");}catch(InvalidDataException){}
        });
        Check("Bridge deployment is readable, rejects broken ZIP and skips unchanged files", () =>
        {
            string jar=Path.Combine(root,"artifacts","universal-editor-bridge-26.2.jar"),target=Path.Combine(directory,Guid.NewGuid()+".jar");
            PrismLauncher.DeployBridge(jar,target);var modified=File.GetLastWriteTimeUtc(target);PrismLauncher.DeployBridge(jar,target);Assert(File.GetLastWriteTimeUtc(target)==modified,"An identical bridge was rewritten.");
            using(var zip=System.IO.Compression.ZipFile.OpenRead(target))Assert(zip.GetEntry("fabric.mod.json") is not null,"Deployed ZIP unreadable.");
            var before=File.ReadAllBytes(target);string broken=Path.Combine(directory,Guid.NewGuid()+".txt");File.WriteAllText(broken,"broken ZIP");try{PrismLauncher.DeployBridge(broken,target);throw new Exception("Broken bridge accepted.");}catch(IOException){}
            Assert(before.SequenceEqual(File.ReadAllBytes(target)),"Rejected archive replaced the bridge.");
        });
        Check("Visual property selections update the model without changing other cards",()=>
        {
            using var host=new Form {Width=750,Height=850};using var designer=new ScenarioDesigner(new GameCatalog(),()=>new ItemRecipe());host.Controls.Add(designer);host.Show();Application.DoEvents();
            Assert(designer.Project.Actions[1].Timing==TimingKind.OnceAfterDelay,"Building forms altered a different card.");
            IEnumerable<Control> Walk(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Walk(child)));
            var action=Walk(designer).OfType<ComboBox>().Single(c=>c.Items.Contains("GiveItem") && c.Parent?.Parent is FormFields);action.SelectedItem="Particle";Application.DoEvents();
            var when=Walk(designer).OfType<ComboBox>().Single(c=>c.Items.Contains("Every interval"));when.SelectedItem="Every interval";Application.DoEvents();
            Assert(designer.Project.Actions[0].Kind==ActionKind.Particle && designer.Project.Actions[0].Timing==TimingKind.EveryInterval,"Selecting properties did not update the card.");
            Assert(designer.Compile().Last(n=>n.Name.StartsWith("Particle")).Command.Contains("particle"),"Generated blocks did not match selections.");
        });
        var json = JsonSerializer.Serialize(headless ? new { results, skippedWindowTests = skipped } : (object)results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "artifacts", headless ? "self-test-headless-results.json" : "self-test-results.json"), json);
        return json.Contains("\"passed\": false") ? 1 : 0;
    }
}
