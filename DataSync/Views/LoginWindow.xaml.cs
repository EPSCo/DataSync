using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using DataSync.Core.Data;
using DataSync.Core.Logging;
using DataSync.Core.Models;
using DataSync.Core.Security;

namespace DataSync.Views
{
    public partial class LoginWindow : Window
    {
        private List<User> _users;

        public LoginWindow()
        {
            InitializeComponent();

            HeaderText.Text = App.Info.ProgramName + " Login";
            CopyrightText.Text = App.Info.Copyright;

            var saved = CredentialStore.Load();
            UserNameBox.Text = saved.UserName ?? "";
            PasswordBox.Password = saved.Password ?? "";
            RememberCheckBox.IsChecked = saved.Remember;

            Loaded += OnLoaded;
        }

        /// <summary>The user who logged in, when the dialog result is true.</summary>
        public User AuthenticatedUser { get; private set; }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UserNameBox.Focus();
            LoginButton.IsEnabled = false;
            try
            {
                _users = await Task.Run(() => new UserRepository(ConnectionKind.Local).GetAll());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot read users");
                ShowError("Cannot read users from the local database: " + ex.Message);
            }
            finally
            {
                LoginButton.IsEnabled = _users != null;
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_users == null || _users.Count == 0)
            {
                ShowError("No users found. Please contact the database administrator.");
                return;
            }

            var userName = UserNameBox.Text.Trim();
            var password = PasswordBox.Password.Trim();
            var user = UserRepository.FindByCredentials(_users, userName, password);
            if (user == null)
            {
                ShowError("Incorrect username or password. Please try again.");
                PasswordBox.Focus();
                PasswordBox.SelectAll();
                return;
            }

            try
            {
                if (RememberCheckBox.IsChecked == true)
                {
                    CredentialStore.Save(userName, password);
                }
                else
                {
                    CredentialStore.Forget();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot update " + CredentialStore.FileName);
            }

            Log.Information("User logged in: " + user.UserName);
            AuthenticatedUser = user;
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
