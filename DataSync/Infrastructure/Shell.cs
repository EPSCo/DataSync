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

        /// <summary>Yes/No question; true for Yes.</summary>
        bool Confirm(string message, string title);

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
            Show(message, title, MessageBoxImage.Information);
        }

        public void ShowError(string message, string title)
        {
            Show(message, title, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title)
        {
            var owner = ActiveWindow();
            var result = owner != null
                ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
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

        private static void Show(string message, string title, MessageBoxImage image)
        {
            var owner = ActiveWindow();
            if (owner != null)
            {
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
            }
            else
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, image);
            }
        }

        private static Window ActiveWindow()
        {
            var app = Application.Current;
            return app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app?.MainWindow;
        }
    }
}
