using ShellCommand.Util;
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
        public string Name { get; set; }
        public string Command { get; set; }
        public string Match { get; set; }
        public bool RunAsAdmin { get; set; }
        public string Icon { get; set; }

        public static string ExpandEnvVars(string input, string workingDir)
        {
            var output = Environment.ExpandEnvironmentVariables(input);
            output = output.Replace(Env.VAR_DIR, workingDir.Replace("\\", "/"));
            return output;
        }

        public void Execute(string workingDir)
        {
            var exCommand = ExpandEnvVars(Command, workingDir);
            var (com, arg) = Cmd.SplitCommandArg(exCommand);
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
            if (!string.IsNullOrEmpty(Icon))
            {
                var exIcon = ExpandEnvVars(Icon, workingDir);
                item.Image = NativeLoader.LocaImage(exIcon);
            }
            return item;
        }
    }
}
