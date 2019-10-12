using ShellCommand.DataModel;
using ShellCommand.MenuDefinition;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using XJK;
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

#if DEBUG
        public Visibility DEBUG => Visibility.Visible;
#else
        public Visibility DEBUG => Visibility.Collapsed;
#endif

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            if(Environment.Is64BitOperatingSystem != Environment.Is64BitProcess)
            {
                MessageBox.Show($"Please run x64 version instead.");
                App.Current.Shutdown();
            }

            if (ENV.IsAdministrator()) Title += " (Admin)";
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
            if (ENV.IsAdministrator())
            {
                AdminInstall();
            }
            else
            {
                Cmd.RunAsAdmin(ENV.EntryLocation, ARG_INSTALL);
            }

            var globalfile = Path.Combine(ENV.BaseDirectory, Env.GlobalSettingFileName);
            if (!File.Exists(globalfile))
            {
                ResetGlobalSettingFile(false);
            }
        }

        private void Uninstall(object sender, RoutedEventArgs e)
        {
            if (ENV.IsAdministrator())
            {
                AdminUninstall();
            }
            else
            {
                Cmd.RunAsAdmin(ENV.EntryLocation, ARG_UNINSTALL);
            }
        }

        private void AdminInstall()
        {
            Util.Reg.SetExePath(ENV.EntryLocation);
            Util.Reg.SetLogPath();
            var srm = Path.Combine(ENV.BaseDirectory, Env.SrmExeName);
            Cmd.RunAsInvoker(srm, "install ShellCommand.exe -codebase");
        }
           
        private void AdminUninstall()
        {
            var srm = Path.Combine(ENV.BaseDirectory, Env.SrmExeName);
            Cmd.RunAsInvoker(srm, "uninstall ShellCommand.exe");
            Util.Reg.DeleteSubKey();
        }

        private void OpenAppFolder(object sender, RoutedEventArgs e)
        {
            Cmd.RunAsInvoker("explorer", ENV.BaseDirectory);
        }

        private void EditGlobalSettingFile(object sender, RoutedEventArgs e)
        {
            var configpath = Path.Combine(ENV.BaseDirectory, Env.GlobalSettingFileName);
            Cmd.RunAsInvoker(configpath, "");
        }

        private void ResetGlobalSettingFile(object sender, RoutedEventArgs e)
        {
            ResetGlobalSettingFile(true);
        }

        private void ResetGlobalSettingFile(bool showMessage)
        {
            var templatefile = Path.Combine(ENV.BaseDirectory, Env.GlobalTemplateSettingFileName);
            var targetfile = Path.Combine(ENV.BaseDirectory, Env.GlobalSettingFileName);
            if (!File.Exists(templatefile))
            {
                if (showMessage)
                {
                    MessageBox.Show("Template global setting file NOT exist!", Env.AppName);
                }
                return;
            }
            if (File.Exists(targetfile))
            {
                if (showMessage)
                {
                    var result = MessageBox.Show("Global setting file will be overwrite, sure?", Env.AppName, MessageBoxButton.OKCancel);
                    if (result == MessageBoxResult.Cancel)
                    {
                        return;
                    }
                }
            }
            File.Copy(templatefile, targetfile, true);
        }

        private void TestGlobalTemplateMenu(object sender, RoutedEventArgs e)
        {
            var position = XJK.SysX.Device.Mouse.GetPosition();
            var globalPath = Path.Combine(ENV.BaseDirectory, Env.GlobalTemplateSettingFileName);
            var menu = MenuCreator.Create(ENV.BaseDirectory, "", globalPath);
            menu.Show(position);
        }

    }
}
