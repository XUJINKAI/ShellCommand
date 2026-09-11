using System.Security.Cryptography;
using System.Text.Json;
using ShellCommand.Broker;

namespace ShellCommand.Broker.Tests;

public sealed class DeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-deploy-" + Guid.NewGuid().ToString("N"));
    public DeploymentTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private string MakePackage()
    {
        var source = Path.Combine(_root, "download");
        Directory.CreateDirectory(source);
        var files = new List<PackageFile>();
        foreach (var name in DeploymentPackage.RequiredFiles)
        {
            var path = Path.Combine(source, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture: " + name);
            files.Add(new(name, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        }
        File.WriteAllText(Path.Combine(source, "build-manifest.json"), JsonSerializer.Serialize(new PackageManifest(PipeProtocol.Version, files)));
        return source;
    }

    [Fact]
    public void StageSurvivesSourceRemovalAndDoesNotChangeConfiguration()
    {
        var source = MakePackage();
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(data, "config"));
        var config = Path.Combine(data, "config", "global.shellcommand.yaml");
        File.WriteAllText(config, "user data, even invalid YAML");
        var staged = DeploymentPackage.Stage(source, data);
        Assert.Equal(staged, DeploymentPackage.Stage(source, data));
        Directory.Delete(source, true);
        Assert.Equal(Path.GetFileName(staged), DeploymentPackage.Validate(staged));
        Assert.Equal("user data, even invalid YAML", File.ReadAllText(config));
    }

    [Fact]
    public void InvalidResourceDoesNotPublishRunner()
    {
        var source = MakePackage();
        File.AppendAllText(Path.Combine(source, "ShellCommand.Explorer.dll"), "tampered");
        Assert.Throws<InvalidDataException>(() => DeploymentPackage.Stage(source, Path.Combine(_root, "data")));
        Assert.False(Directory.Exists(Path.Combine(_root, "data", "runner")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:/escape")]
    [InlineData("/absolute")]
    [InlineData("Assets/../../escape")]
    public void RejectsEscapingPaths(string path)
    {
        var source = MakePackage();
        var manifestPath = Path.Combine(source, "build-manifest.json");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(manifestPath))!;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Files = manifest.Files.Append(new(path, "00")).ToArray() }));
        Assert.Throws<InvalidDataException>(() => DeploymentPackage.Validate(source));
    }
}
