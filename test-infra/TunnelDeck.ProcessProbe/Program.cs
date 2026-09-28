using System.Text.Json;

// A bounded argv receiver for terminal tests. It never interprets or executes an argument.
string output = Path.Combine(AppContext.BaseDirectory, "argv.json");
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { arguments = args, processId = Environment.ProcessId }));
