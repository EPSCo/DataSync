using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using DataSync.Views;

namespace DataSync.Infrastructure
{
    /// <summary>
    /// What view models need from the application window layer: dialogs and restart.
    /// </summary>
    public interface IShell
    {
        void ShowInfo(string message, string title);

        void ShowError(string message, string title);

        /// <summary>A question with two buttons; true for the first (Yes).</summary>
        bool Confirm(string message, string title, string yesText = "Yes", string noText = "No");

        /// <summary>Asks for the clear-data password; true when it was entered correctly.</summary>
        bool ConfirmClearData();

        /// <summary>Shows the Settings dialog; true when settings were saved.</summary>
        bool EditSettings();

        void RestartApplication();

        /// <summary>Opens a folder in Windows Explorer, creating it if needed.</summary>
        void OpenFolder(string path);
    }

    public sealed class Shell : IShell
    {
        public void ShowInfo(string message, string title)
        {
            Show(message, title, false);
        }

        public void ShowError(string message, string title)
        {
            Show(message, title, true);
        }

        public bool Confirm(string message, string title, string yesText = "Yes", string noText = "No")
        {
            return ShowMessage(new MessageWindow(title, message, yesText, noText, false));
        }

        public bool ConfirmClearData()
        {
            var dialog = new ClearDataWindow { Owner = ActiveWindow() };
            return dialog.ShowDialog() == true;
        }

        public bool EditSettings()
        {
            var dialog = new SettingsWindow { Owner = ActiveWindow() };
            return dialog.ShowDialog() == true;
        }

        public void RestartApplication()
        {
            App.Restart();
        }

        public void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        }

        private static void Show(string message, string title, bool isError)
        {
            ShowMessage(new MessageWindow(title, message, "OK", null, isError));
        }

        private static bool ShowMessage(MessageWindow dialog)
        {
            dialog.Owner = ActiveWindow();
            if (dialog.Owner == null)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return dialog.ShowDialog() == true;
        }

        private static Window ActiveWindow()
        {
            var app = Application.Current;
            return app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app?.MainWindow;
        }
    }
}
