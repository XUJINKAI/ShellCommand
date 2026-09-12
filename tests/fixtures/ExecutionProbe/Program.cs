using System.Text.Json;
var output = Environment.GetEnvironmentVariable("SC_PROBE_DEST") ?? throw new InvalidOperationException("Missing destination");
File.WriteAllText(output, JsonSerializer.Serialize(new { Args = args, Cwd = Environment.CurrentDirectory, Value = Environment.GetEnvironmentVariable("SC_PROBE_VALUE") }));
for (var i = 0; i < 512; i++) { Console.Out.Write(new string('o', 4096)); Console.Error.Write(new string('e', 4096)); }
