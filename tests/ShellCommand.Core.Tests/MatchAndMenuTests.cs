using ShellCommand.Core;

namespace ShellCommand.Core.Tests;

public class MatchAndMenuTests
{
    private static readonly string[] Entries = { ".git", "README.md", "ShellCommand.sln" };
    [Theory]
    [InlineData(".git", true)]
    [InlineData("!.git", false)]
    [InlineData(".git<&&>README.md", true)]
    [InlineData("*.sln", true)]
    [InlineData("*.cs", false)]
    public void MatchUsesDirectChildFacts(string expressionText, bool expected)
    {
        Assert.True(MatchExpression.TryParse(expressionText, out var expression, out _));
        Assert.Equal(expected, expression!.IsMatch(new SetDirectoryFacts(Entries)));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a<&&>")]
    public void MatchRejectsPathTraversalAndEmptyTerms(string expression)
        => Assert.False(MatchExpression.TryParse(expression, out _, out _));

    [Fact]
    public void ResolverHidesMismatchesAndNormalizesSeparators()
    {
        Assert.True(MatchExpression.TryParse(".git", out var git, out _));
        var local = new DirectoryConfig(new CommandDefinition[]
        {
            new("---", null),
            new("Only Git", "git status", git),
            new("---", null),
            new("---", null)
        });
        var global = new GlobalConfig(new[] { new CommandDefinition("Global", "wt.exe") }, new GlobalFunctions(CopyPath: true));
        var menu = MenuResolver.Resolve(global, local, new SetDirectoryFacts(Array.Empty<string>()), @"C:\work", new BuiltInCapabilities(true, false));
        Assert.DoesNotContain(menu.Items, item => item is ResolvedItem.Action action && action.Title == "Only Git");
        Assert.DoesNotContain(menu.Items.Take(1), item => item is ResolvedItem.Separator);
        Assert.DoesNotContain(menu.Items.Zip(menu.Items.Skip(1)), pair => pair.First is ResolvedItem.Separator && pair.Second is ResolvedItem.Separator);
        Assert.Contains(menu.Items, item => item is ResolvedItem.Action action && action.Title == "Copy Folder Path");
        Assert.Equal(MenuResolver.OpenAppTitle, ((ResolvedItem.Action)menu.Items[^1]).Title);
    }

    [Fact]
    public void VariableExpansionReplacesDirAndEnvironmentButKeepsUnknown()
    {
        var result = VariableExpander.Expand("tool \"%DIR%\" %KNOWN% %UNKNOWN%", @"C:\Code\A B", name => name == "KNOWN" ? "value" : null);
        Assert.Equal("tool \"C:\\Code\\A B\" value %UNKNOWN%", result);
    }
}
