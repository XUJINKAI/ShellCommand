using ShellCommand.Config.Yaml;
using ShellCommand.Core;

namespace ShellCommand.Config.Tests;

public class ConfigParserTests
{
    [Fact]
    public void ParsesLegacyDirectoryConfiguration()
    {
        var result = ConfigParser.ParseDirectory("""
            - Name: Open Terminal
              Command: wt.exe -d "%DIR%"
              Match: .git
              RunAsAdmin: false
              Icon: "%PROGRAMFILES%/WindowsTerminal/wt.exe"
            - Name: ---
            """);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.Value!.Commands.Count);
        Assert.Equal("Open Terminal", result.Value.Commands[0].Name);
    }

    [Fact]
    public void RejectsUnknownFieldAndPreservesLineDiagnostic()
    {
        var result = ConfigParser.ParseDirectory("- Name: Test\n  Command: test.exe\n  Comand: typo\n");
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "UNKNOWN_FIELD");
        Assert.Equal(3, diagnostic.Line);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RejectsAnchorsAliasesAndTooManyCommands()
    {
        var anchor = ConfigParser.ParseDirectory("- &command\n  Name: Test\n  Command: test.exe\n");
        Assert.Contains(anchor.Diagnostics, d => d.Code == "UNSUPPORTED_YAML_FEATURE");
        var commands = string.Join(Environment.NewLine, Enumerable.Range(0, 101).Select(i => $"- Command: test{i}.exe"));
        var tooMany = ConfigParser.ParseDirectory(commands);
        Assert.Contains(tooMany.Diagnostics, d => d.Code == "TOO_MANY_COMMANDS");
    }

    [Fact]
    public void ParsesGlobalFunctionsWithDefaults()
    {
        var result = ConfigParser.ParseGlobal("GlobalCommands:\n  - Command: wt.exe\nFunctions:\n  CopyPath: true\n");
        Assert.True(result.IsValid);
        Assert.True(result.Value!.Functions.CopyPath);
        Assert.False(result.Value.Functions.EditGlobal);
    }
}
