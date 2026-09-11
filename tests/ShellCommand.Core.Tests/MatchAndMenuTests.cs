using ShellCommand.Core;
namespace ShellCommand.Core.Tests;
public sealed class MatchAndMenuTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sc-core"));
    private static readonly string Source = Path.Combine(Root, "config", "global.yaml");
    private static readonly ResolveEnvironment Environment = new(Root, Root, new Dictionary<string, string> { ["TEST"] = "${directory}" });
    private static MenuDefinition Command(string id, string title = "Test") => new(id, title, Source, Run: new("tool.exe", []));
    [Fact] public void UnknownFactsRemainUnknownUnderNot()
    {
        var condition = new Condition("not", Children: [new("exists", ".git")]);
        Assert.Equal(Truth.Unknown, Conditions.Evaluate(condition, new(Root, []), null));
        Assert.Equal(Truth.True, Conditions.Evaluate(condition, new(Root, []), []));
    }
    [Fact] public void LocalOverrideSuppressesGlobalEvenWhenDisabled()
    {
        var global = new MenuConfig([Command("one"), Command("two")], []);
        var local = new MenuConfig([Command("one") with { Enabled = false }], []);
        var menu = MenuResolver.Resolve(global, local, [], new(Root, []), Environment);
        Assert.Equal(2, menu.Items.Count(i => i.Kind == "action")); // two + settings
        Assert.DoesNotContain(menu.Items, i => i.Plan?.SourcePath == Source && i.Plan.Title == "one");
    }
    [Fact] public void ReplacementUsesLocalPositionAndSource()
    {
        var localSource = Path.Combine(Root, ".shellcommand.yaml");
        var global = new MenuConfig([Command("same", "old"), Command("other", "other")], []);
        var local = new MenuConfig([Command("same", "new") with { SourcePath = localSource }], []);
        var menu = MenuResolver.Resolve(global, local, [], new(Root, []), Environment);
        Assert.Equal("new", menu.Items[0].Title); Assert.Equal(localSource, menu.Items[0].Plan!.SourcePath);
        Assert.DoesNotContain(menu.Items, i => i.Title == "old");
    }
    [Fact] public void VariablesExpandOnceAndArgumentsStaySeparate()
    {
        var path = Path.Combine(Root, "中文 & one's file.txt");
        var context = new MenuContext(Root, [new(path, false)]);
        var node = Command("a") with { Run = new("tool", ["${selection.paths}", "--cwd=${directory}", "$${literal}", "${env:TEST}"]) };
        var plan = MenuResolver.BuildPlan(node, context, Environment);
        Assert.Equal(new[] { path, "--cwd=" + Root, "${literal}", "${directory}" }, plan.Actions[0].Args);
    }
    [Fact] public void EachFreezesASeparatePlanForEverySelection()
    {
        var paths = new[] { Path.Combine(Root, "A B.txt"), Path.Combine(Root, "two.txt") };
        var node = Command("each") with { Run = new("tool", ["${item.path}"], "${item.parent}", Mode: "each") };
        var plan = MenuResolver.BuildPlan(node, new(null, paths.Select(p => new SelectionItem(p, false)).ToArray()), Environment);
        Assert.Equal(2, plan.Actions.Count); Assert.All(plan.Actions, a => Assert.Equal(Root, a.Cwd));
        Assert.Equal(paths[1], plan.Actions[1].Args![0]);
    }
    [Fact] public void CrossDirectorySelectionDoesNotInventCwd()
    {
        Assert.Throws<InvalidOperationException>(() => MenuResolver.BuildPlan(Command("a"), new(null, [new(Path.Combine(Root, "f"), false)]), Environment));
    }
    [Fact] public void SelectionRuleRequiresEveryItemToMatch()
    {
        var rule = new Condition("selection", Selection: new(["file"], Extensions: [".txt"]));
        Assert.Equal(Truth.False, Conditions.Evaluate(rule, new(Root, [new("a.txt", false), new("b.png", false)]), []));
        Assert.Equal(Truth.True, Conditions.Evaluate(rule, new(Root, [new("A.TXT", false)]), []));
    }
    [Fact] public void AncestorConditionsAndEmptyGroupsAreRespected()
    {
        var group = new MenuDefinition("group", "Group", Source, When: new("exists", ".git"), Items: [Command("child")]);
        var menu = MenuResolver.Resolve(new([group], []), null, [], new(Root, []), Environment);
        Assert.Single(menu.Items); Assert.Equal("设置…", menu.Items[0].Title);
    }
    [Fact] public void ScriptTextIsNeverInterpolated()
    {
        var node = new MenuDefinition("s", "Script", Source, Script: new("powershell", "Write-Output '${directory}'"));
        var action = MenuResolver.BuildPlan(node, new(Root, []), Environment).Actions[0];
        Assert.Equal("Write-Output '${directory}'", action.Text); Assert.Equal(Root, action.Env!["SC_DIRECTORY"]);
    }
    [Theory] [InlineData("*.TXT", "readme.txt", true)] [InlineData("a?c", "ac", false)]
    public void WildcardsAreCaseInsensitive(string pattern, string value, bool expected) => Assert.Equal(expected, Conditions.WildcardMatch(pattern, value));
}
