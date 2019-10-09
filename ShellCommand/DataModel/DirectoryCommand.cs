using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using XJK.SysX;
using XJK.SysX.CommandHelper;

namespace ShellCommand.DataModel
{
    public class DirectoryCommand
    {
        public string Command { get; set; }
        public string Name { get; set; }
        public string Match { get; set; }
        public bool RunAsAdmin { get; set; }

        public void Execute(string workingDir)
        {
            var repcommand = Command.Replace(Env.VAR_DIR, workingDir.Replace("\\", "/"));
            var (com, arg) = Cmd.SplitCommandArg(repcommand);
            ProcessInfoChain ProcessInfoChain = ProcessInfoChain.New(com, arg);
            if (RunAsAdmin)
            {
                ProcessInfoChain.Privilege = Privilege.Admin;
            }
            ProcessInfoChain.Set(p => p.WorkingDirectory = workingDir);
            try
            {
                var result = ProcessInfoChain.Excute(true);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"{com} {arg}", ex.Message);
            }
        }

        public ToolStripMenuItem ToMenuItem(string workingDir)
        {
            var item = new ToolStripMenuItem(string.IsNullOrEmpty(Name) ? Command : Name);
            item.Click += (sender, args) =>
            {
                Execute(workingDir);
            };
            if (!string.IsNullOrEmpty(Match))
            {
                var matchPath = Path.Combine(workingDir, Match);
                if (!File.Exists(matchPath) && !Directory.Exists(matchPath))
                {
                    item.Enabled = false;
                }
            }
            return item;
        }
    }
}
