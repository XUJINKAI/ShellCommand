using ShellCommand.Broker;
using ShellCommand.Core;
namespace ShellCommand.Broker.Tests;
public sealed class PreparationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-prep-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "config", "global.shellcommand.yaml");
    public PreparationTests() => Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
    public void Dispose() => Directory.Delete(_root, true);
    private PreparedSnapshot Prepare() => Preparation.Prepare(new(_root, _root, _root));
    [Fact] public void InvalidEditsKeepGoodV2ButDeletionClearsIt()
    {
        File.WriteAllText(Config, "version: 2\nmenu:\n - {id: a, title: A, copy: hello}\n");
        Assert.Single(Prepare().Global.Config!.Menu);
        File.WriteAllText(Config, "version: 2\nmenu: [broken");
        var bad = Prepare(); Assert.Single(bad.Global.Config!.Menu); Assert.NotEmpty(bad.Global.Diagnostics);
        File.Delete(Config); var deleted = Prepare(); Assert.Null(deleted.Global.Config); Assert.True(deleted.Global.Missing);
    }
    [Fact] public void OversizedAndInvalidUtf8EditsRetainGoodSource()
    {
        File.WriteAllText(Config, "version: 2\nmenu:\n - {id: a, title: A, copy: hello}\n");
        Prepare();
        File.WriteAllBytes(Config, [255, 254, 255]);
        Assert.Single(Prepare().Global.Config!.Menu);
        File.WriteAllText(Config, new string('x', 256 * 1024 + 1));
        var snapshot = Prepare();
        Assert.Single(snapshot.Global.Config!.Menu); Assert.NotEmpty(snapshot.Global.Diagnostics);
    }
    [Fact] public void IncludesPublishAsOneTreeWithDefinitionRelativePaths()
    {
        var nested = Path.Combine(_root, "config", "menus"); Directory.CreateDirectory(nested);
        File.WriteAllText(Config, "version: 2\ninclude: [menus/dev.yaml]\nmenu: []\n");
        var child = Path.Combine(nested, "dev.yaml");
        File.WriteAllText(child, "version: 2\nmenu:\n - {id: a, title: A, open: '${config_dir}/readme.txt'}\n");
        var prepared = Prepare(); Assert.Equal(child, prepared.Global.Config!.Menu[0].SourcePath);
        File.WriteAllText(child, "version: 2\ninclude: [../global.shellcommand.yaml]\nmenu: []");
        var bad = Prepare(); Assert.Single(bad.Global.Config!.Menu); Assert.NotEmpty(bad.Global.Diagnostics);
    }
    [Fact] public void PreviewDoesNotPublishUnsavedContentToPersistentLkg()
    {
        File.WriteAllText(Config, "version: 2\nmenu:\n - {id: a, title: Saved, copy: hello}\n"); Prepare();
        var preview = Preparation.Prepare(new(_root, _root, _root, Config, "version: 2\nmenu:\n - {id: a, title: Unsaved, copy: hello}\n"));
        Assert.Equal("Unsaved", preview.Global.Config!.Menu[0].Title);
        File.WriteAllText(Config, "invalid"); Assert.Equal("Saved", Prepare().Global.Config!.Menu[0].Title);
    }
    [Fact] public void FullConfigToLaunchPlanPreservesArguments()
    {
        File.WriteAllText(Config, "version: 2\nmenu:\n - id: a\n   title: Tool\n   when: {context: selection}\n   run:\n     exe: tool.exe\n     args: ['${selection.paths}']\n");
        var path = Path.Combine(_root, "中文 & 'trailing' .txt"); var prepared = Prepare();
        var menu = MenuResolver.Resolve(prepared.Global.Config, null, prepared.Facts, new(_root, [new(path, false)]), new(_root, _root, new Dictionary<string, string>()));
        var plan = menu.Items[0].Plan!; var tokens = new ActionTokenStore(); var token = tokens.Issue(plan);
        Assert.True(tokens.TryConsume(token, out var frozen, out _)); Assert.Equal(path, frozen!.Actions[0].Args![0]); Assert.Equal(_root, frozen.Actions[0].Cwd);
    }
}
