using System;
using System.Threading;
using System.Windows;

namespace DataSync.Views
{
    public partial class SplashWindow : Window
    {
        private readonly CancellationTokenSource _exit = new CancellationTokenSource();

        public SplashWindow()
        {
            InitializeComponent();

            ProgramNameText.Text = App.Info.ProgramName;
            DetailsText.Text = App.Info.ProgramDetails;
            VersionText.Text = "Version " + App.Info.Version;
            CopyrightText.Text = App.Info.Copyright;
        }

        /// <summary>
        /// Cancelled when the user clicks Exit.
        /// </summary>
        public CancellationToken ExitToken => _exit.Token;

        /// <summary>
        /// Call on the UI thread.
        /// </summary>
        public void ReportProgress(int percent, string status)
        {
            Progress.Value = percent;
            StatusText.Text = status;
        }

        /// <summary>
        /// Shows the Exit button while startup waits for something that may take long. Call on the UI thread.
        /// </summary>
        public void ShowExitButton(bool visible)
        {
            ExitButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            _exit.Dispose();
            base.OnClosed(e);
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            ExitButton.IsEnabled = false;
            StatusText.Text = "Exiting...";
            _exit.Cancel();
        }
    }
}
