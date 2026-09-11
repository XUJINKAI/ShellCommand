using Microsoft.Win32;
using ShellCommand.Broker;
namespace ShellCommand.Broker.Tests;
public sealed class MenuJournalTests
{
    [Fact] public void WindowsJournalRestoresExactValueAndRejectsCorruptionAndExternalChanges()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "ShellCommandTest" + Guid.NewGuid().ToString("N");
        var registration = @"*\shell\" + name;
        var keyPath = @"Software\Classes\" + registration;
        var state = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(state);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("ProgrammaticAccessOnly", new byte[] { 1, 2, 255 }, RegistryValueKind.Binary);
            var entry = new MenuEntry(name, name, MenuEntryType.StaticVerb, MenuScope.Files, MenuEntryState.Enabled, "HKCU", registration, null, null, true);
            var manager = new ContextMenuManager(state);
            Assert.True(manager.Disable(entry).Success);
            Assert.True(manager.Disable(entry).Success);
            Assert.True(manager.Restore(entry).Success);
            Assert.Equal(RegistryValueKind.Binary, key.GetValueKind("ProgrammaticAccessOnly"));
            Assert.Equal(new byte[] { 1, 2, 255 }, (byte[])key.GetValue("ProgrammaticAccessOnly")!);
            Assert.True(manager.Disable(entry).Success);
            key.SetValue("ProgrammaticAccessOnly", "external");
            Assert.False(manager.Restore(entry).Success);
            Assert.Equal("external", key.GetValue("ProgrammaticAccessOnly"));
            File.WriteAllText(Path.Combine(state, "menu-journal.json"), "[null]");
            Assert.False(manager.Disable(entry).Success);
            Assert.Equal("external", key.GetValue("ProgrammaticAccessOnly"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); Directory.Delete(state, true); }
    }
}
