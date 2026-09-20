using System.Text.Json;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal static class LifecycleEvidence
{
    public static void Write(string name, object evidence)
    {
        string? directory = Environment.GetEnvironmentVariable("TUNNELDECK_LIFECYCLE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
}
