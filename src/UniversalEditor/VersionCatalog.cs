using System.Text.Json;

namespace UniversalEditor;

internal sealed record GameVersion(string Id, int JavaMajor, int? ProtocolId, bool HasSchema)
{
    public override string ToString() => Id;
}

internal static class VersionCatalog
{
    public static IReadOnlyList<GameVersion> Load(string root)
    {
        var research = Path.Combine(root, "docs", "research");
        using var coverage = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "release-coverage.json")));
        var profiles = coverage.RootElement.EnumerateArray().ToDictionary(p => p.GetProperty("id").GetString()!, p => p.Clone());
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "mojang-version-manifest.json")));
        return manifest.RootElement.GetProperty("versions").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "release")
            .OrderByDescending(p => p.GetProperty("releaseTime").GetDateTimeOffset())
            .Select(p =>
            {
                var id = p.GetProperty("id").GetString()!;
                var profile = profiles[id];
                var protocol = profile.GetProperty("protocolId");
                return new GameVersion(id, profile.GetProperty("javaMajor").GetInt32(),
                    protocol.ValueKind == JsonValueKind.Null ? null : protocol.GetInt32(),
                    profile.GetProperty("datasets").GetProperty("protocol").GetProperty("status").GetString() == "source-data-parsed-not-runtime-tested");
            }).ToArray();
    }
}
