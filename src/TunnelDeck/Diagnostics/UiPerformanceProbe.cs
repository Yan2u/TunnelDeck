using System.Diagnostics;
using System.Text.Json;

namespace TunnelDeck.Diagnostics;

/// <summary>Opt-in timings for isolated native tests. No resource names, endpoints or credentials are recorded.</summary>
internal static class UiPerformanceProbe
{
    private const int MaximumSamplesPerOperation = 512;
    private static readonly Dictionary<string, List<double>> Samples = new(StringComparer.Ordinal);
    private static string? _outputPath;

    public static void Initialize(bool isolated)
    {
        if (!isolated) { return; }
        string? path = Environment.GetEnvironmentVariable("TUNNELDECK_UI_TIMINGS");
        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && !path.Any(char.IsControl))
        { _outputPath = Path.GetFullPath(path); }
    }

    public static Measurement Measure(string operation) => new(operation, _outputPath is null ? 0 : Stopwatch.GetTimestamp());

    public static void Save()
    {
        if (_outputPath is null) { return; }
        var operations = Samples.ToDictionary(pair => pair.Key, pair =>
        {
            double[] ordered = [.. pair.Value.Order()];
            return new
            {
                count = ordered.Length,
                p50Ms = ordered[(int)Math.Ceiling(ordered.Length * 0.50) - 1],
                p95Ms = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
                maximumMs = ordered[^1],
            };
        });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
            File.WriteAllText(_outputPath, JsonSerializer.Serialize(new
            {
                capturedAtUtc = DateTimeOffset.UtcNow,
                scope = "Synchronous UI handlers; excludes UIA/IPC, network waits and animation completion",
                operations,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A diagnostic output failure must not prevent application shutdown. The test requires the report.
        }
    }

    internal readonly struct Measurement(string operation, long startedAt) : IDisposable
    {
        public void Dispose()
        {
            if (startedAt == 0) { return; }
            if (!Samples.TryGetValue(operation, out List<double>? samples)) { Samples[operation] = samples = []; }
            if (samples.Count < MaximumSamplesPerOperation) { samples.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds); }
        }
    }
}
