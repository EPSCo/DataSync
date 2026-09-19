using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

            _viewModel = new MainViewModel(App.Info, new Shell(), App.IsDesignMode);
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

        /// <summary>
        /// Shows or hides one Sync table column from the grid's right-click menu.
        /// Each menu item's Tag holds the matching column's x:Name; at least one column stays visible.
        /// </summary>
        private void SyncColumnMenuItem_Toggled(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var column = FindSyncColumn(menuItem?.Tag as string);
            if (menuItem == null || column == null)
            {
                return;
            }

            if (!menuItem.IsChecked && SyncGrid.Columns.Count(c => c.Visibility == Visibility.Visible) <= 1)
            {
                // Keep at least one column visible so the grid never goes blank.
                menuItem.IsChecked = true;
                return;
            }

            column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private DataGridColumn FindSyncColumn(string columnName)
        {
            switch (columnName)
            {
                case "BeginColumn": return BeginColumn;
                case "EndColumn": return EndColumn;
                case "CurrentColumn": return CurrentColumn;
                case "CountColumn": return CountColumn;
                case "TotalColumn": return TotalColumn;
                case "ProgressColumn": return ProgressColumn;
                case "StatusColumn": return StatusColumn;
                case "SyncColumn": return SyncColumn;
                default: return null;
            }
        }
    }
}
