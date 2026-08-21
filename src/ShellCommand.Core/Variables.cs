namespace ShellCommand.Core;

public static class VariableExpander
{
    public static string Expand(string command, string workingDirectory, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        environment ??= Environment.GetEnvironmentVariable;
        return System.Text.RegularExpressions.Regex.Replace(command, "%([^%]+)%", match =>
        {
            var name = match.Groups[1].Value;
            if (name.Equals("DIR", StringComparison.OrdinalIgnoreCase)) return workingDirectory;
            return environment(name) ?? match.Value;
        });
    }
}
