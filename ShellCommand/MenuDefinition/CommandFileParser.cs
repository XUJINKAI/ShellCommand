using ShellCommand.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XJK.SysX;
using XJK.SysX.CommandHelper;

namespace ShellCommand.MenuDefinition
{
    static class CommandFileParser
    {
        public static List<ToolStripMenuItem> ParseDirectoryCommand(string path, string workingDir)
        {
            var result = new List<ToolStripMenuItem>();
            try
            {
                var entries = Util.Yaml.LoadYaml<DirectoryCommand[]>(path);
                foreach (var entry in entries)
                {
                    var item = entry.ToMenuItem(workingDir);
                    result.Add(item);
                }
            }
            catch { }
            return result;
        }

        public static List<ToolStripMenuItem> ParseGlobalCommand(string path, string workingDir)
        {
            var result = new List<ToolStripMenuItem>();
            try
            {
                var config = Util.Yaml.LoadYaml<GlobalConfig>(path);
                foreach (var entry in config.GlobalCommands)
                {
                    var item = entry.ToMenuItem(workingDir);
                    if (item.Enabled)
                        result.Add(item);
                }
                if (config.Functions.CopyPath)
                {
                    result.Add(BuildinMenuItems.GetCopyPathMenuItem(workingDir));
                }
            }
            catch { }
            return result;
        }
    }
}
