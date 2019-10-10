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
    static class MenuItemsBuilder
    {
        public static void AddSeparator(List<ToolStripItem> result)
        {
            if (result.Count > 0 && !(result.Last() is ToolStripSeparator))
            {
                result.Add(new ToolStripSeparator());
            }
        }

        public static void ParseDirectoryCommandInto(List<ToolStripItem> result, string path, string workingDir)
        {
            try
            {
                var entries = Util.Yaml.LoadYaml<DirectoryCommand[]>(path);
                foreach (var entry in entries)
                {
                    var item = entry.ToMenuItem(workingDir);
                    if (item is ToolStripSeparator)
                    {
                        AddSeparator(result);
                    }
                    else
                    {
                        result.Add(item);
                    }
                }
            }
            catch { }
        }

        public static void ParseGlobalCommandInto(List<ToolStripItem> result, string path, string workingDir)
        {
            try
            {
                var config = Util.Yaml.LoadYaml<GlobalConfig>(path);
                foreach (var entry in config.GlobalCommands)
                {
                    var item = entry.ToMenuItem(workingDir);
                    if (item is ToolStripSeparator)
                    {
                        AddSeparator(result);
                    }
                    else
                    {
                        if (item.Enabled)
                            result.Add(item);
                    }
                }
                AddSeparator(result);

                if (config.Functions.CopyPath)
                {
                    result.Add(BuildinMenuItems.GetCopyPathMenuItem(workingDir));
                }
                if (config.Functions.EditGlobal)
                {
                    result.Add(BuildinMenuItems.OpenGlobalSetting());
                }
            }
            catch { }
        }
    }
}
