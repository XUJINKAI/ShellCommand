using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public bool IsMatch(string workingDir)
        {
            if (string.IsNullOrEmpty(Match))
                return true;

            bool reverse = false;
            string pattern = Match;
            if (Match.StartsWith("!"))
            {
                reverse = true;
                pattern = Match.Substring(1);
            }

            var exist = Directory.GetFiles(workingDir, pattern).Any() || Directory.GetDirectories(workingDir, pattern).Any();
            return exist ^ reverse;
        }

        public ToolStripItem ToMenuItem(string workingDir)
        {
            if(string.IsNullOrEmpty(Command) && Name == Env.VAR_SEPNAME)
            {
                return new ToolStripSeparator();
            }

            var display = string.IsNullOrEmpty(Name) ? Command : Name;
            var item = new ToolStripMenuItem(display)
            {
                Enabled = IsMatch(workingDir),
            };
            item.Click += (sender, args) =>
            {
                Execute(workingDir);
            };
            return item;
        }
    }
}
