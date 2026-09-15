using System.ComponentModel;
using System.Windows;
using DataSync.Infrastructure;
using DataSync.ViewModels;

namespace DataSync.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(App.Info, App.CurrentUser.UserName, new Shell(), App.IsDesignMode);
            DataContext = _viewModel;
            Loaded += (s, e) => _viewModel.Start();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _viewModel.Shutdown();
            base.OnClosing(e);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
