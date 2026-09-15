using System;
using System.Windows;
using DataSync.Core.Configuration;
using DataSync.Core.Logging;

namespace DataSync.Views
{
    /// <summary>
    /// Edits the App.config settings. The dialog result is true when they were saved.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly EditableSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();

            _settings = EditableSettings.Load();
            DataContext = _settings;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var error = _settings.Validate();
            if (error != null)
            {
                ShowError(error);
                return;
            }

            try
            {
                _settings.Save();
                Log.Information("Settings saved to " + ConfigFile.DefaultPath);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                // Typically no write access to the application folder.
                Log.Error(ex, "Cannot save settings");
                ShowError("Cannot save settings: " + ex.Message);
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
