using System;
using System.Windows;
using DataSync.Core.Security;

namespace DataSync.Views
{
    /// <summary>
    /// Asks for the date-based clear-data password. The dialog result is true when it is correct.
    /// </summary>
    public partial class ClearDataWindow : Window
    {
        public ClearDataWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => PasswordBox.Focus();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ClearDataPassword.IsValid(PasswordBox.Password, DateTime.Now))
            {
                ErrorText.Text = "The password you entered is incorrect. Please try again.";
                ErrorText.Visibility = Visibility.Visible;
                PasswordBox.Focus();
                PasswordBox.SelectAll();
                return;
            }

            DialogResult = true;
        }
    }
}
