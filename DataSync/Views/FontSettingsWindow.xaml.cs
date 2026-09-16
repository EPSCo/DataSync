using System;
using System.ComponentModel;
using System.Windows;
using DataSync.Core.Configuration;
using DataSync.Core.Logging;
using DataSync.Infrastructure;

namespace DataSync.Views
{
    /// <summary>
    /// Edits the font sizes of the text styles. Changes are previewed while typing, saved to App.config on Save and
    /// undone on Cancel.
    /// </summary>
    public partial class FontSettingsWindow : Window
    {
        private readonly FontSettings _original = FontSettings.Load();
        private FontSettings _settings;
        private bool _saved;

        public FontSettingsWindow()
        {
            InitializeComponent();

            HintText.Text = "Sizes from " + FontSettings.MinimumSize + " to " + FontSettings.MaximumSize +
                            ". Previewed while you type; saved changes apply without a restart.";
            Edit(FontSettings.Load());
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!_saved)
            {
                FontSizes.Apply(_original);
            }
            base.OnClosed(e);
        }

        private void Edit(FontSettings settings)
        {
            _settings = settings;
            foreach (var item in settings.Items)
            {
                item.PropertyChanged += Size_PropertyChanged;
            }
            DataContext = settings;
            FontSizes.Apply(settings);
        }

        private void Size_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            FontSizes.Apply(_settings);
        }

        private void DefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            Edit(FontSettings.Defaults());
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
                _saved = true;
                Log.Information("Font settings saved to " + ConfigFile.DefaultPath);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                // Typically no write access to the application folder.
                Log.Error(ex, "Cannot save font settings");
                ShowError("Cannot save font settings: " + ex.Message);
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
