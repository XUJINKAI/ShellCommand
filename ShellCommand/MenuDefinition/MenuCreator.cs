﻿using ShellCommand.DataModel;
using ShellCommand.Util;
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
    static class MenuCreator
    {
        public static ContextMenuStrip Create(string workingDir, string directoryFilePath, string globalFilePath)
        {
            var menu = new ContextMenuStrip();
            var container = new ToolStripMenuItem(Env.MenuDisplay);

            var list = new List<ToolStripItem>();

            if (File.Exists(directoryFilePath))
            {
                list.AddParseDirectoryCommandInto(directoryFilePath, workingDir);
                list.AddSeparator();
            }

            if (File.Exists(globalFilePath))
            {
                list.AddParseGlobalCommandInto(globalFilePath, workingDir);
            }

            list.Add(GetOpenAppMenuItem());
            if (!File.Exists(directoryFilePath))
            {
                list.AddSeparator();
                list.Add(GetCreateDirecotyCommandFileMenuItem(directoryFilePath));
            }

            container.DropDownItems.AddRange(list.ToArray());
            menu.Items.Add(container);
            return menu;
        }


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

        public static ToolStripMenuItem GetCreateDirecotyCommandFileMenuItem(string path)
        {
            var item = new ToolStripMenuItem(Env.CreateFolderSpecificFileText);
            item.Click += (sender, args) =>
            {
                var o = new DirectoryCommand[] 
                {
                    new DirectoryCommand(){ Name = "Custom Command", Command = "cmd" },
                };
                Yaml.SaveYaml(path, o);
                File.SetAttributes(path, FileAttributes.Hidden);
            };
            item.Image = NativeLoader.GetShell32Icon(70);
            return item;
        }

        public static ToolStripMenuItem GetOpenGlobalSettingMenuItem()
        {
            var item = new ToolStripMenuItem(Env.OpenGlobalSettingFileText);
            item.Click += (sender, args) =>
            {
                var path = Path.Combine(Reg.GetAppFolder(), Env.GlobalSettingFileName);
                Cmd.RunAsInvoker(path, "");
            };
            return item;
        }

        public static ToolStripMenuItem GetOpenAppMenuItem()
        {
            var item = new ToolStripMenuItem(Env.OpenAppText);
            var exe = Reg.GetExePath();
            item.Click += (sender, args) =>
            {
                Cmd.RunAsInvoker(exe, "");
            };
            item.Image = NativeLoader.GetShell32Icon(2);
            return item;
        }

    }
}
