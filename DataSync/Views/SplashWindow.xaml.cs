using System.Windows;

namespace DataSync.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();

            ProgramNameText.Text = App.Info.ProgramName;
            DetailsText.Text = App.Info.ProgramDetails;
            VersionText.Text = "Version " + App.Info.Version;
            CopyrightText.Text = App.Info.Copyright;
        }

        /// <summary>
        /// Call on the UI thread.
        /// </summary>
        public void ReportProgress(int percent, string status)
        {
            Progress.Value = percent;
            StatusText.Text = status;
        }
    }
}
