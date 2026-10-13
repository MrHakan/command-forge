using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UniversalEditor;

// What the editor shows on the user's Discord profile. Image keys refer to art assets uploaded to the
// Discord application (Developer Portal → Rich Presence → Art Assets).
internal sealed record PresenceState(string Details, string State, string LargeImage, string LargeText, string SmallImage, string SmallText);

// Minimal Discord Rich Presence client over the local IPC pipe (\\.\pipe\discord-ipc-N). No network access,
// no token handling: Discord itself attaches the activity to the signed-in user. All I/O runs on a background
// thread; the UI only hands over the latest state.
internal sealed class DiscordPresence : IDisposable
{
    public const string DefaultApplicationId = "1559628127410331810"; // "Command Forge" application
    private const int Handshake = 0, FrameOp = 1, Close = 2;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(4); // Discord allows 5 updates / 20 s
    private readonly string applicationId;
    private readonly long startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread worker;
    private readonly object gate = new();
    private PresenceState? wanted, sent;
    private NamedPipeClientStream? pipe;
    private DateTime lastSend = DateTime.MinValue, nextConnect = DateTime.MinValue;
    private volatile bool disposed;
    public string Status { get; private set; } = "Starting…";
    public bool Connected => pipe?.IsConnected == true;

    // Handshake only: proves Discord is running and accepts the application ID without setting any activity.
    internal static (bool Ok, string Message) Check(string applicationId)
    {
        if (!ValidApplicationId(applicationId)) return (false, "Invalid application ID format.");
        for (int i = 0; i < 10; i++)
        {
            using var candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { candidate.Connect(300); }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { continue; }
            try
            {
                var frame = Frame(Handshake, new JsonObject { ["v"] = 1, ["client_id"] = applicationId });
                candidate.Write(frame); candidate.Flush();
                var header = new byte[8]; ReadFully(candidate, header);
                int op = BinaryPrimitives.ReadInt32LittleEndian(header), length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (length is < 0 or > 1024 * 1024) return (false, "Unexpected Discord frame.");
                var body = new byte[length]; ReadFully(candidate, body);
                var reply = JsonNode.Parse(body) as JsonObject;
                if (op != Close && reply?["evt"]?.GetValue<string>() == "READY") return (true, "Discord accepted the application ID (pipe discord-ipc-" + i + ").");
                return (false, "Discord rejected the application ID: " + (reply?["message"]?.GetValue<string>() ?? "unknown error"));
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or OperationCanceledException) { return (false, "Discord handshake failed: " + e.Message); }
        }
        return (false, "Discord is not running (no discord-ipc pipe).");
    }
    private static void ReadFully(Stream stream, byte[] buffer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.ReadAsync(buffer.AsMemory(offset), timeout.Token).AsTask().GetAwaiter().GetResult();
            if (read == 0) throw new IOException("Discord closed the connection.");
            offset += read;
        }
    }

    public static bool ValidApplicationId(string? id) => id is not null && Regex.IsMatch(id, @"\A\d{17,20}\z");

    public DiscordPresence(string applicationId)
    {
        if (!ValidApplicationId(applicationId)) throw new ArgumentException("A Discord application ID is 17–20 digits.");
        this.applicationId = applicationId;
        worker = new Thread(Run) { IsBackground = true, Name = "Discord presence" };
        worker.Start();
    }

    public void Update(PresenceState state) { lock (gate) wanted = state; signal.Set(); }

    private void Run()
    {
        while (!disposed)
        {
            signal.WaitOne(TimeSpan.FromSeconds(5));
            if (disposed) break;
            try
            {
                if (!Connected && DateTime.UtcNow >= nextConnect) Connect();
                if (!Connected) continue;
                PresenceState? target; lock (gate) target = wanted;
                if (target is null || target == sent) continue;
                var wait = lastSend + MinInterval - DateTime.UtcNow;
                for (var until = DateTime.UtcNow + wait; DateTime.UtcNow < until && !disposed;) Thread.Sleep(100);
                if (disposed) break;
                lock (gate) target = wanted;
                Send(target!); sent = target; lastSend = DateTime.UtcNow;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or TimeoutException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                Drop(Connected ? "Discord connection lost; retrying." : "Discord is not running.");
            }
        }
    }

    private void Connect()
    {
        nextConnect = DateTime.UtcNow.AddSeconds(15);
        for (int i = 0; i < 10 && !disposed; i++)
        {
            var candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { candidate.Connect(150); }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { candidate.Dispose(); continue; }
            pipe = candidate;
            Write(Handshake, new JsonObject { ["v"] = 1, ["client_id"] = applicationId });
            var (op, reply) = Read();
            if (op == Close || reply?["evt"]?.GetValue<string>() != "READY")
            { Drop("Discord rejected the application ID: " + (reply?["message"]?.GetValue<string>() ?? reply?["data"]?["message"]?.GetValue<string>() ?? "unknown error")); nextConnect = DateTime.UtcNow.AddMinutes(1); return; }
            sent = null; Status = "Connected to Discord.";
            return;
        }
        Status = "Discord is not running.";
    }

    private void Send(PresenceState state)
    {
        Write(FrameOp, Command(state, startedAt, Environment.ProcessId));
        var (_, reply) = Read();
        if (reply?["evt"]?.GetValue<string>() == "ERROR") Status = "Discord refused the update: " + reply["data"]?["message"]?.GetValue<string>();
        else Status = "Showing activity on Discord.";
    }

    internal static JsonObject Command(PresenceState? state, long startedAt, int processId)
    {
        JsonNode? activity = null;
        if (state is not null)
            activity = new JsonObject
            {
                ["details"] = Fit(state.Details), ["state"] = Fit(state.State),
                ["timestamps"] = new JsonObject { ["start"] = startedAt },
                ["assets"] = new JsonObject { ["large_image"] = state.LargeImage, ["large_text"] = Fit(state.LargeText), ["small_image"] = state.SmallImage, ["small_text"] = Fit(state.SmallText) },
                ["instance"] = false
            };
        return new JsonObject { ["cmd"] = "SET_ACTIVITY", ["nonce"] = Guid.NewGuid().ToString(), ["args"] = new JsonObject { ["pid"] = processId, ["activity"] = activity } };
    }

    // Discord requires 2–128 characters for text fields.
    internal static string Fit(string text)
    {
        text = text.Trim();
        if (text.Length < 2) text = (text + "  ")[..2];
        return text.Length <= 128 ? text : text[..127] + "…";
    }

    internal static byte[] Frame(int op, JsonNode payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    private void Write(int op, JsonNode payload)
    {
        var data = Frame(op, payload);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        pipe!.WriteAsync(data, timeout.Token).AsTask().GetAwaiter().GetResult();
        pipe.Flush();
    }

    private (int Op, JsonObject? Body) Read()
    {
        var header = ReadExactly(8);
        int op = BinaryPrimitives.ReadInt32LittleEndian(header), length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > 1024 * 1024) throw new IOException("Unexpected Discord frame.");
        return (op, JsonNode.Parse(ReadExactly(length)) as JsonObject);
    }

    private byte[] ReadExactly(int count)
    {
        var buffer = new byte[count]; int offset = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (offset < count)
        {
            int read = pipe!.ReadAsync(buffer.AsMemory(offset), timeout.Token).AsTask().GetAwaiter().GetResult();
            if (read == 0) throw new IOException("Discord closed the connection.");
            offset += read;
        }
        return buffer;
    }

    private void Drop(string status)
    {
        try { pipe?.Dispose(); } catch (IOException) { }
        pipe = null; sent = null; Status = status;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; signal.Set();
        worker.Join(TimeSpan.FromSeconds(2));
        // Clear the activity right away instead of waiting for Discord to notice the closed process.
        try { if (Connected) { Write(FrameOp, Command(null, startedAt, Environment.ProcessId)); } }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
        Drop("Stopped.");
        signal.Dispose();
    }
}
