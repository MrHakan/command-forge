using System.Text.Json.Nodes;

namespace UniversalEditor;

internal sealed class LiveConnection
{
    public string DirectoryPath { get; }
    public JsonObject? State { get; private set; }
    public string? PendingId { get; private set; }
    private long heartbeatWritten;
    public bool Ready => State is not null && State["connected"]?.GetValue<bool>() == true &&
        State["version"]?.GetValue<string>() == "26.2" && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (State["heartbeat"]?.GetValue<long>() ?? 0) is >= -1000 and < 4000;
    public LiveConnection(string instanceGameDirectory) => DirectoryPath = Path.Combine(Path.GetFullPath(instanceGameDirectory), ".universal-editor");
    public void Poll()
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "state.json");
            if (!File.Exists(path)) { State = null; return; }
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("Bridge state exceeds 2 MB.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var previousSession = State?["session"]?.GetValue<string>();
            State = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(stream);
            if (previousSession != State?["session"]?.GetValue<string>()) PendingId = null;
            if (PendingId == State?["requestId"]?.GetValue<string>()) PendingId = null;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Ready && now - heartbeatWritten >= 500)
            {
                var lease = new JsonObject { ["session"] = State!["session"]!.GetValue<string>(), ["heartbeat"] = now };
                var temporary = Path.Combine(DirectoryPath, "editor-heartbeat.tmp");
                File.WriteAllText(temporary, lease.ToJsonString()); File.Move(temporary, Path.Combine(DirectoryPath, "editor-heartbeat.json"), true); heartbeatWritten = now;
            }
        }
        catch (IOException) { State = null; }
        catch (System.Text.Json.JsonException) { State = null; }
    }
    public string Send(JsonObject request)
    {
        if (!Ready) throw new IOException("No current 26.2 singleplayer bridge heartbeat. Open a world in the bridge-enabled instance.");
        if (PendingId is not null && request["operation"]?.GetValue<string>() != "stop") throw new IOException("Waiting for the previous game acknowledgement.");
        request["session"] = State!["session"]!.GetValue<string>();
        var id = Guid.NewGuid().ToString(); request["requestId"] = id;
        var json = request.ToJsonString();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new IOException("Request exceeds the bridge's 1 MB limit. Reduce command text or chain size.");
        Directory.CreateDirectory(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, "request-" + id + ".tmp");
        File.WriteAllText(temporary, json); File.Move(temporary, Path.Combine(DirectoryPath, "request.json"), true);
        PendingId = id; return id;
    }
}
