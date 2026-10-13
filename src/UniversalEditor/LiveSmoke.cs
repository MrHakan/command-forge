using System.Text.Json;
using System.Text.Json.Nodes;

namespace UniversalEditor;

internal static class LiveSmoke
{
    public static int Run(string root, string gameDirectory)
    {
        var results = new List<object>(); var c = new LiveConnection(gameDirectory);
        var artifact = Path.Combine(root, "artifacts", "live-smoke-results.json");
        void Save() => File.WriteAllText(artifact, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        void Check(string name, Action action)
        { try { action(); results.Add(new { name, passed = true, error = (string?)null }); } catch (Exception ex) { results.Add(new { name, passed = false, error = ex.Message }); } Save(); }
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        JsonObject Wait(Func<JsonObject, bool> ready, int seconds = 12)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            do { c.Poll(); if (c.Ready && ready(c.State!)) return c.State!; Thread.Sleep(100); } while (DateTime.UtcNow < deadline);
            throw new IOException("Timed out waiting for the real game: " + c.State?["message"]);
        }
        JsonObject Send(JsonObject request)
        { string id = c.Send(request); return Wait(s => s["requestId"]?.GetValue<string>() == id); }
        JsonObject Execute(string command, string player) => Send(new JsonObject { ["operation"] = "execute", ["playerId"] = player, ["command"] = command });
        string player = ""; int x = 0, y = 0, z = 0; string objective = "ue_" + Guid.NewGuid().ToString("N")[..8];
        bool fixtureApproved = false;
        JsonObject Install(string head, bool run) => new()
        {
            ["operation"] = "install", ["playerId"] = player, ["x"] = x, ["y"] = y, ["z"] = z, ["running"] = run,
            ["nodes"] = new JsonArray(new JsonObject { ["command"] = head, ["conditional"] = false },
                new JsonObject { ["command"] = "execute at @a run particle minecraft:end_rod ~ ~1 ~ 0.2 0.2 0.2 0.01 1", ["conditional"] = true })
        };
        string first = $"scoreboard players add #ticks {objective} 1", edited = $"scoreboard players add #ticks {objective} 2";
        try
        {
            var marker = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameDirectory))!, ".universal-editor-managed");
            if (!File.Exists(marker) || File.ReadAllText(marker) != "26.2") throw new IOException("Smoke tests require the editor-owned 26.2 instance.");
            var initial = Wait(s => s["players"]!.AsArray().Count == 1 && s["paused"]?.GetValue<bool>() == false);
            var world = initial["world"]?.GetValue<string>() ?? "";
            if (!world.StartsWith("UniversalEditor-Smoke-", StringComparison.Ordinal) || !Directory.Exists(Path.Combine(gameDirectory, "saves", world)))
                throw new IOException("Smoke tests require a currently open disposable editor smoke world.");
            fixtureApproved = true;
            var p = initial["players"]![0]!; player = p["id"]!.GetValue<string>();
            x = p["x"]!.GetValue<int>() + 4; y = p["y"]!.GetValue<int>() + 1; z = p["z"]!.GetValue<int>() + 4;
            Check("Real 26.2 integrated server is connected and ticking", () => Assert(c.Ready, "Bridge was offline"));
            Check("One-shot setup uses the game command engine", () => Assert(Execute($"scoreboard objectives add {objective} dummy", player)["accepted"]!.GetValue<bool>(), "Setup rejected"));
            Check("Stage creates two physical, inactive command blocks", () =>
            {
                var response = Send(Install(first, false)); Assert(response["accepted"]!.GetValue<bool>(), response["message"]!.GetValue<string>());
                var blocks = response["blocks"]!.AsArray(); Assert(blocks.Count == 2 && blocks.All(b => b!["automatic"]!.GetValue<bool>() == false), "Blocks were not inactive");
                Assert(blocks[0]!["command"]!.GetValue<string>() == first, "Game read-back differs from draft");
            });
            Check("Repeating head and conditional chain execute in game", () =>
            {
                var response = Send(Install(first, true)); Assert(response["accepted"]!.GetValue<bool>(), response["message"]!.GetValue<string>());
                Wait(s => s["blocks"]!.AsArray().All(b => b!["success"]!.GetValue<int>() > 0));
                var score = Execute($"scoreboard players get #ticks {objective}", player); Assert(score["accepted"]!.GetValue<bool>(), "Repeating score could not be read");
            });
            Check("Live replacement updates actual block commands", () =>
            { var response = Send(Install(edited, true)); Assert(response["accepted"]!.GetValue<bool>() && response["blocks"]![0]!["command"]!.GetValue<string>() == edited, "Edit was not applied"); });
            Check("Invalid syntax preserves the running chain", () =>
            { var response = Send(Install("this_command_does_not_exist", true)); Assert(!response["accepted"]!.GetValue<bool>(), "Invalid syntax accepted"); Assert(response["blocks"]![0]!["command"]!.GetValue<string>() == edited, "Failed edit changed existing command"); });
            Check("In-game data merge appears in bridge read-back", () =>
            { var response = Execute($"data merge block {x} {y} {z} {{Command:\"say Edited in game\"}}", player); Assert(response["accepted"]!.GetValue<bool>(), "In-game edit rejected"); Assert(response["blocks"]![0]!["command"]!.GetValue<string>() == "say Edited in game", "In-game command change was not observed"); });
            Check("Modern item components replace the real main-hand slot", () =>
            {
                var response = Send(new JsonObject { ["operation"] = "item", ["playerId"] = player, ["count"] = 1,
                    ["item"] = "minecraft:diamond_sword[minecraft:custom_name={text:\"Editor blade\",color:\"aqua\"},minecraft:enchantments={\"minecraft:sharpness\":5}]" });
                Assert(response["accepted"]!.GetValue<bool>(), response["message"]!.GetValue<string>());
                var held = response["players"]![0]!; Assert(held["itemId"]!.GetValue<string>() == "minecraft:diamond_sword" && held["itemSnbt"]!.GetValue<string>().Contains("Editor blade"), "Held item codec read-back is wrong");
            });
            Check("Invalid item component preserves the held item", () =>
            {
                var before = c.State!["players"]![0]!["itemSnbt"]!.GetValue<string>();
                var response = Send(new JsonObject { ["operation"] = "item", ["playerId"] = player, ["count"] = 1, ["item"] = "minecraft:diamond_sword[minecraft:unknown_editor_component=1]" });
                Assert(!response["accepted"]!.GetValue<bool>(), "Invalid item component accepted"); Assert(response["players"]![0]!["itemSnbt"]!.GetValue<string>() == before, "Failed item edit changed the slot");
            });
            Check("STOP clears and deactivates both physical blocks", () =>
            { var response = Send(new JsonObject { ["operation"] = "stop" }); Assert(response["accepted"]!.GetValue<bool>(), "Stop rejected"); Assert(response["blocks"]!.AsArray().All(b => b!["command"]!.GetValue<string>() == "" && !b["automatic"]!.GetValue<bool>()), "A block remained active"); });
            JsonObject Design(DesignerProject project,bool run,bool reset=true)=>new()
            {
                ["operation"]="install",["playerId"]=player,["x"]=x,["y"]=y,["z"]=z,["running"]=run,["timelineObjective"]=project.Objective,["resetTimeline"]=reset,
                ["nodes"]=new JsonArray(Authoring.Compile(project).Select(n=>(JsonNode)new JsonObject{["command"]=n.Command,["conditional"]=false}).ToArray())
            };
            Check("Every visual preset validates and stages with the real 26.2 parser",()=>
            {
                foreach(string name in new[]{"Countdown reveal","Fog introduction","Particle loop","Mob showcase"}){var response=Send(Design(Authoring.Template(name),false));Assert(response["accepted"]!.GetValue<bool>(),name+": "+response["message"]);Assert(!response["armed"]!.GetValue<bool>(),"Staging armed a preset.");}
            });
            Check("Form-generated item properties and typed custom data survive game round-trip",()=>
            {
                var recipe=new ItemRecipe{Name="Form \"blade\"",Lore="First line\nSecond line",Unbreakable=true,Glint=false,Enchantments=[new(){Id="minecraft:sharpness",Level=7},new(){Id="minecraft:unbreaking",Level=3}],Attributes=[new(){Amount=12.5m}]};
                recipe.Preserved["minecraft:custom_data"]=new JsonObject{["flag"]=1,["bytes"]=new JsonArray(1,-2)};
                recipe.OriginalComponentSnbt["minecraft:custom_data"]="{flag:1b,bytes:[B;1b,-2b]}";
                var response=Send(new JsonObject{["operation"]="item",["playerId"]=player,["count"]=1,["item"]=recipe.Argument()});Assert(response["accepted"]!.GetValue<bool>(),response["message"]!.GetValue<string>());
                var actual=response["players"]![0]!;var json=actual["itemJson"]!.AsObject();Assert(json["components"]!["minecraft:enchantments"]!["minecraft:sharpness"]!.GetValue<int>()==7,"Enchantment failed.");
                var restored=ItemRecipe.Read(json,actual["itemSnbt"]!.GetValue<string>());Assert(restored.Unbreakable && restored.Attributes.Count==1 && restored.Lore==recipe.Lore,"Form read-back lost fields.");
                response=Send(new JsonObject{["operation"]="item",["playerId"]=player,["count"]=1,["item"]=restored.Argument()});Assert(response["accepted"]!.GetValue<bool>(),response["message"]!.GetValue<string>());
                Assert(response["players"]![0]!["itemSnbt"]!.GetValue<string>().Contains("[B;1B,-2B]",StringComparison.OrdinalIgnoreCase),"Custom byte array type was lost.");
            });
            Check("Timeline delay, variable branches, interval and window execute in game",()=>
            {
                var p=new DesignerProject{Actions=[new(){Kind=ActionKind.Variable,VariableName="phase",VariableValue=1},new(){Kind=ActionKind.Variable,VariableName="hits",VariableValue=0},new(){Kind=ActionKind.Variable,VariableName="wrong",VariableValue=0},new(){Kind=ActionKind.Variable,VariableName="hits",VariableOperation="Add",VariableValue=1,Timing=TimingKind.EveryInterval,IntervalSeconds=0.25m,DelaySeconds=0.5m,EndSeconds=1.5m,ConditionVariable="phase",ConditionValue=1},new(){Kind=ActionKind.Variable,VariableName="wrong",VariableOperation="Add",VariableValue=1,Timing=TimingKind.EveryTick,ConditionVariable="phase",ConditionValue=1,Comparison="Not equal"},new(){Kind=ActionKind.Variable,VariableName="phase",VariableValue=2,Timing=TimingKind.OnceAfterDelay,DelaySeconds=2}]};
                var response=Send(Design(p,true));Assert(response["accepted"]!.GetValue<bool>(),response["message"]!.GetValue<string>());
                Wait(s=>s["blocks"]![0]!["success"]!.GetValue<int>()>0);
                var deadline=DateTime.UtcNow.AddSeconds(3);while(DateTime.UtcNow<deadline){c.Poll();Thread.Sleep(100);}
                var score=Execute($"scoreboard players get #v_hits {p.Objective}",player);Assert(score["commandResult"]?.GetValue<int>()==5,"Timed interval did not execute exactly five times: "+score["message"]);
                score=Execute($"scoreboard players get #v_wrong {p.Objective}",player);Assert(score["commandResult"]?.GetValue<int>()>0,"False branch did not become active after phase change.");
                score=Execute($"scoreboard players get #v_phase {p.Objective}",player);Assert(score["commandResult"]?.GetValue<int>()==2,"Delayed phase change failed.");
                int before=Execute($"scoreboard players get #clock {p.Objective}",player)["commandResult"]!.GetValue<int>();response=Send(Design(p,true,false));Assert(response["accepted"]!.GetValue<bool>(),"Live update rejected.");
                int after=Execute($"scoreboard players get #clock {p.Objective}",player)["commandResult"]!.GetValue<int>();Assert(after>=before,"Live edit reset the clock.");
                response=Send(Design(p,true,true));Assert(response["accepted"]!.GetValue<bool>(),"Restart rejected.");after=Execute($"scoreboard players get #clock {p.Objective}",player)["commandResult"]!.GetValue<int>();Assert(after<before,"Explicit restart did not reset clock.");
            });
            Check("Selectable tag action and target filtering execute in game",()=>
            {
                var p=new DesignerProject{Actions=[new(){Kind=ActionKind.Tag,Text="ue_smoke_target",Option="add"},new(){Kind=ActionKind.Message,Presentation="Chat",Text="Tagged",RequiredTag="ue_smoke_target"},new(){Kind=ActionKind.Message,Presentation="Chat",Text="Excluded",RequiredTag="ue_smoke_target",UnlessTag=true}]};
                var response=Send(Design(p,true));Assert(response["accepted"]!.GetValue<bool>(),response["message"]!.GetValue<string>());Wait(s=>s["blocks"]![0]!["success"]!.GetValue<int>()>0);
                Assert(Execute("execute if entity @s[tag=ue_smoke_target]",player)["accepted"]!.GetValue<bool>(),"Tag was not added.");
                Assert(!Execute("execute if entity @s[tag=!ue_smoke_target]",player)["accepted"]!.GetValue<bool>(),"Excluded branch reported success.");
                Execute("tag @s remove ue_smoke_target",player);
            });
            Check("Lost editor heartbeat stops the repeating chain", () =>
            {
                var response = Send(Install(first, true)); Assert(response["accepted"]!.GetValue<bool>(), "Restart rejected");
                Thread.Sleep(5500); // Deliberately do not Poll: no host heartbeat for this interval.
                c.Poll(); Assert(c.State!["blocks"]!.AsArray().All(b => b!["command"]!.GetValue<string>() == "" && !b["automatic"]!.GetValue<bool>()), "Heartbeat loss did not stop the chain");
            });
        }
        catch (Exception ex) { results.Add(new { name = "Smoke fixture", passed = false, error = ex.Message }); }
        finally
        { try { c.Poll(); if (fixtureApproved && c.Ready) Send(new JsonObject { ["operation"] = "stop" }); } catch (Exception) { } Save(); }
        return File.ReadAllText(artifact).Contains("\"passed\": false") ? 1 : 0;
    }
}
