using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ShellCommand.Core;
namespace ShellCommand.Broker;

internal static class IconCache
{
    public static string Prepare(IconDefinition definition, string source, string data, string app)
    {
        if (!OperatingSystem.IsWindows()) return "";
        var file = definition.File;
        var index = definition.Index;
        if (file is null)
        {
            file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
            index = definition.Builtin switch { "folder" => 3, "file" => 0, "copy" => 134, "settings" => 21, "git" => 137, "code" => 2, _ => 16 };
        }
        else
        {
            file = VariableExpander.Expand(file, new(null, []), source, new(app, data, Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().ToDictionary(p => (string)p.Key, p => (string)p.Value!, StringComparer.OrdinalIgnoreCase)));
            file = Path.GetFullPath(file, Path.GetDirectoryName(source)!);
            if (file.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidOperationException("图标不支持网络路径。");
        }
        using var input = File.OpenRead(file);
        if (input.Length > 64 * 1024 * 1024) throw new InvalidDataException("图标资源过大。");
        var hash = Convert.ToHexString(SHA256.HashData(input));
        var folder = Path.Combine(data, "cache", "icons"); Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, hash + "-" + index + ".ico");
        if (File.Exists(target)) return target;
        var icons = new IntPtr[1];
        if (PrivateExtractIcons(file, index, 32, 32, icons, null, 1, 0) != 1 || icons[0] == IntPtr.Zero) throw new InvalidOperationException("无法提取图标。");
        try
        {
            if (!GetIconInfo(icons[0], out var info)) throw new InvalidOperationException("图标信息不可用。");
            try
            {
                var header = new BitmapInfo { Size = 40, Width = 32, Height = 32, Planes = 1, Bits = 32, ImageSize = 4096 };
                var pixels = new byte[4096]; var mask = new byte[128];
                var dc = GetDC(IntPtr.Zero);
                try
                {
                    if (GetDIBits(dc, info.Color, 0, 32, pixels, ref header, 0) == 0) throw new InvalidOperationException("图标位图不可用。");
                    var maskHeader = new BitmapInfo { Size = 40, Width = 32, Height = 32, Planes = 1, Bits = 1, ImageSize = 128, White = 0x00ffffff };
                    GetDIBits(dc, info.Mask, 0, 32, mask, ref maskHeader, 0);
                }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write((byte)32); writer.Write((byte)32); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(40 + pixels.Length + mask.Length); writer.Write(22);
                writer.Write(40); writer.Write(32); writer.Write(64); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(0); writer.Write(pixels.Length + mask.Length); writer.Write(new byte[16]);
                writer.Write(pixels); writer.Write(mask); writer.Flush();
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, bytes.ToArray()); File.Move(temporary, target, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            finally { if (info.Color != IntPtr.Zero) DeleteObject(info.Color); if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask); }
        }
        finally { DestroyIcon(icons[0]); }
        return target;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int Icon; public uint X; public uint Y; public IntPtr Mask; public IntPtr Color; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width; public int Height; public ushort Planes; public ushort Bits;
        public uint Compression; public uint ImageSize; public int X; public int Y; public uint Colors; public uint Important; public uint Black; public uint White;
    }
    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode)] private static extern uint PrivateExtractIcons(string file, int index, int width, int height, [Out] IntPtr[] icons, [Out] uint[]? ids, uint count, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfo info, uint usage);
}
