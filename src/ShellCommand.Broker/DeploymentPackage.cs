using System.Security.Cryptography;
using System.Text.Json;

namespace ShellCommand.Broker;

public sealed record PackageFile(string Path, string Sha256);
public sealed record PackageManifest(int Protocol, IReadOnlyList<PackageFile> Files);

/// <summary>Pure file staging; package registration belongs to the Windows app.</summary>
public static class DeploymentPackage
{
    public static readonly IReadOnlyList<string> RequiredFiles = Array.AsReadOnly(new[]
    {
        "ShellCommand.exe", "ShellCommand.Broker.exe", "ShellCommand.Explorer.dll",
        "AppxManifest.xml", "Assets/StoreLogo.png", "Assets/Square150x150Logo.png", "Assets/Square44x44Logo.png"
    });

    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11");

    public static string Validate(string directory)
    {
        var manifestPath = Path.Combine(directory, "build-manifest.json");
        using var input = File.OpenRead(manifestPath);
        if (input.Length > 64 * 1024) throw new InvalidDataException("发布清单过大。");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(input) ?? throw new InvalidDataException("缺少发布清单。");
        if (manifest.Protocol != PipeProtocol.Version || manifest.Files.Count is < 7 or > 32)
            throw new InvalidDataException("组件协议或发布清单不匹配。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || file.Path.Contains('\\') || file.Path.Contains(':') ||
                file.Path.Split('/').Any(p => p is "" or "." or "..") || !paths.Add(file.Path))
                throw new InvalidDataException("发布清单包含非法或重复路径。");
            var path = Path.Combine(directory, file.Path);
            // Reject junctions/symlinks at each component, including the source root.
            for (var current = new FileInfo(path) as FileSystemInfo; current is not null; current = Directory.GetParent(current.FullName))
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("安装资源不能是链接。");
            using var resource = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(resource)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("文件校验失败：" + file.Path);
        }
        if (RequiredFiles.Any(p => !paths.Contains(p))) throw new InvalidDataException("安装资源不完整。");
        input.Position = 0;
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    public static string Stage(string source, string dataRoot)
    {
        var build = Validate(source);
        var destination = Path.Combine(dataRoot, "runner", build);
        if (Directory.Exists(destination))
        {
            if (Validate(destination) != build) throw new InvalidDataException("已有 runner 校验失败，请关闭集成后清理该版本。");
            return destination;
        }
        var staging = Path.Combine(dataRoot, "runner", ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(Path.Combine(source, "build-manifest.json")))!;
            foreach (var file in manifest.Files)
            {
                var target = Path.Combine(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(source, file.Path), target);
            }
            File.Copy(Path.Combine(source, "build-manifest.json"), Path.Combine(staging, "build-manifest.json"));
            if (Validate(staging) != build) throw new InvalidDataException("复制后的文件校验失败。");
            Directory.Move(staging, destination);
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
