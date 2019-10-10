using ShellCommand.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XJK.SysX;

namespace ShellCommand.MenuDefinition
{
    static class BuildinMenuItems
    {
        public static ToolStripMenuItem GetCopyPathMenuItem(string workingDir)
        {
            var item = new ToolStripMenuItem("Copy Folder Path");
            item.Click += (sender, arg) =>
            {
                var path = workingDir.Replace("\\", "/");
                Clipboard.SetText(path);
                MessageBox.Show(path, "Copy!");
            };
            return item;
        }

        public static ToolStripMenuItem InitDirectoryFile(string path)
        {
            var item = new ToolStripMenuItem(Env.CreateFolderSpecificFileText);
            item.Click += (sender, args) =>
            {
                Util.Yaml.SaveYaml(path, DefaultSetting.GetDirectoryCommand());
            };
            return item;
        }

        public static ToolStripMenuItem OpenGlobalSetting()
        {
            var item = new ToolStripMenuItem(Env.OpenGlobalSettingFileText);
            item.Click += (sender, args) =>
            {
                var path = Path.Combine(Env.GetAppFolder(), Env.GlobalSettingFileName);
                Cmd.RunAsInvoker(path, "");
            };
            return item;
        }

        public static ToolStripMenuItem OpenApp()
        {
            var item = new ToolStripMenuItem(Env.OpenAppText);
            item.Click += (sender, args) =>
            {
                Cmd.RunAsInvoker(Util.Reg.GetExePath(), "");
            };
            return item;
        }

    }
}
