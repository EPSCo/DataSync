using System.Windows;
using System.Windows.Media;

namespace DataSync.Views
{
    /// <summary>
    /// A message or question in the application's style. The dialog result is true when the OK button is clicked.
    /// </summary>
    public partial class MessageWindow : Window
    {
        /// <param name="cancelText">The second button's text, or null for a message with only the OK button.</param>
        /// <param name="isError">Shows the title in the danger colour.</param>
        public MessageWindow(string title, string message, string okText, string cancelText, bool isError)
        {
            InitializeComponent();

            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            OkButton.Content = okText;

            if (cancelText == null)
            {
                CancelButton.Visibility = Visibility.Collapsed;
                OkButton.IsCancel = true;
            }
            else
            {
                CancelButton.Content = cancelText;
            }

            if (isError)
            {
                TitleText.Foreground = (Brush)FindResource("DangerBrush");
            }

            Loaded += (s, e) => OkButton.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
