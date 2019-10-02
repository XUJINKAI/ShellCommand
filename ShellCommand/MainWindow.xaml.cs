using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using XJK.SysX;

namespace ShellCommand
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        const string ARG_INSTALL = "--install";
        const string ARG_UNINSTALL = "--uninstall";

        public MainWindow()
        {
            InitializeComponent();
            if(Environment.Is64BitOperatingSystem != Environment.Is64BitProcess)
            {
                MessageBox.Show($"Please run x64 version instead.");
                App.Current.Shutdown();
            }

            if (XJK.ENV.IsAdministrator()) Title += " (Admin)";
            if (Environment.GetCommandLineArgs().Length == 2)
            {
                switch (Environment.GetCommandLineArgs()[1])
                {
                    case ARG_INSTALL:
                        Install(null, null);
                        App.Current.Shutdown();
                        return;
                    case ARG_UNINSTALL:
                        Uninstall(null, null);
                        App.Current.Shutdown();
                        return;
                }
            }
        }

        private void Install(object sender, RoutedEventArgs e)
        {
            if (XJK.ENV.IsAdministrator())
            {
                AdminInstall();
            }
            else
            {
                Cmd.RunAsAdmin(XJK.ENV.EntryLocation, ARG_INSTALL);
            }
        }

        private void Uninstall(object sender, RoutedEventArgs e)
        {
            if (XJK.ENV.IsAdministrator())
            {
                AdminUninstall();
            }
            else
            {
                Cmd.RunAsAdmin(XJK.ENV.EntryLocation, ARG_UNINSTALL);
            }
        }

        private void AdminInstall()
        {
            var configpath = System.IO.Path.Combine(XJK.ENV.BaseDirectory, Env.GlobalSettingFileName);
            if (!File.Exists(configpath))
            {
                var setting = DataModel.DefaultSetting.GetGlobal();
                Util.Yaml.SaveYaml(configpath, setting);
            }
            Util.Reg.SetExePath(XJK.ENV.EntryLocation);
            Util.Reg.SetLogPath();
            Cmd.RunAsInvoker(Env.GetSrmPath(), "install ShellCommand.exe -codebase");
        }
           
        private void AdminUninstall()
        {
            Cmd.RunAsInvoker(Env.GetSrmPath(), "uninstall ShellCommand.exe");
            Util.Reg.DeleteSubKey();
        }

        private void OpenAppSettingFolder(object sender, RoutedEventArgs e)
        {
            Cmd.RunAsInvoker("explorer", Env.GetAppFolder());
        }

        private void EditGlobalSettingFile(object sender, RoutedEventArgs e)
        {
            var configpath = System.IO.Path.Combine(XJK.ENV.BaseDirectory, Env.GlobalSettingFileName);
            Cmd.RunAsInvoker(configpath, "");
        }
    }
}
