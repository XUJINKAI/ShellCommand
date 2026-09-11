using ShellCommand.Config.Yaml;
namespace ShellCommand.Config.Tests;
public sealed class ConfigParserTests
{
    private static string Source => Path.Combine(Path.GetTempPath(), "global.yaml");
    [Fact] public void ParsesV2StructuredActionsAndDisabledStubs()
    {
        var result = ConfigParser.Parse("version: 2\nmenu:\n - id: terminal\n   title: 终端\n   run: { exe: wt.exe, args: ['-d', '${directory}'] }\n - {id: disabled, enabled: false}\n", Source);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(2, result.Value!.Menu.Count);
        Assert.Equal("${directory}", result.Value.Menu[0].Run!.Args[1]);
    }
    [Theory]
    [InlineData("GlobalCommands: []")]
    [InlineData("menu: []")]
    [InlineData("version: 1\nmenu: []")]
    [InlineData("version: 2\nmenu: []\nmenu: []")]
    [InlineData("version: 2\nmenu: &x [*x]")]
    [InlineData("version: 2\nmenu: !!seq []")]
    [InlineData("version: 2\n? [a,b]\n: value\nmenu: []")]
    public void RejectsLegacyAndUnsafeYaml(string text) => Assert.False(ConfigParser.Parse(text, Source).IsValid);
    [Theory]
    [InlineData("run: {exe: tool, args: 'not an array'}")]
    [InlineData("run: {exe: tool, args: ['prefix${selection.paths}']}")]
    [InlineData("run: {exe: tool, args: ['${missing}']}")]
    [InlineData("run: {exe: tool, args: ['${item.path}']}")]
    [InlineData("run: {exe: tool, admin: true, output: window}")]
    [InlineData("run: {exe: tool, admin: true, env: {A: b}}")]
    [InlineData("script: {shell: bash, text: hello}")]
    [InlineData("when: {exists: '../bad'}\n   copy: hello")]
    [InlineData("when: {not: {exists: '*.txt'}, exists: '.git'}\n   copy: hello")]
    [InlineData("copy: true")]
    [InlineData("copy: hello\n   open: file.txt")]
    public void RejectsInvalidFieldsWithSourceDiagnostics(string action)
    {
        var result = ConfigParser.Parse("version: 2\nmenu:\n - id: test\n   title: Test\n   " + action, Source);
        Assert.False(result.IsValid); Assert.All(result.Diagnostics, d => Assert.Equal(Source, d.SourcePath));
    }
    [Fact] public void RejectsDeepYamlBeforeConstructingGraph()
    {
        var yaml = "version: 2\nmenu: " + new string('[', 1000) + new string(']', 1000);
        Assert.False(ConfigParser.Parse(yaml, Source).IsValid);
    }
    [Fact] public void AcceptsLiteralScriptAndSelectionCopy()
    {
        var result = ConfigParser.Parse("version: 2\nmenu:\n - id: script\n   title: Script\n   script: {text: 'Write-Output ${anything}', shell: powershell}\n - id: copy\n   title: Copy\n   copy: {values: '${selection.paths}', separator: \"\\r\\n\"}\n", Source);
        Assert.True(result.IsValid); Assert.Equal("\r\n", result.Value!.Menu[1].Copy!.Separator);
    }
}
