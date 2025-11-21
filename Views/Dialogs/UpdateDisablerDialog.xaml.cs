using SolusManifestApp.Helpers;
using SolusManifestApp.Views.Dialogs;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SolusManifestApp.Views.Dialogs
{
    public partial class UpdateDisablerDialog : Window
    {
        public List<SelectableApp> Apps { get; set; }
        public List<SelectableApp> SelectedApps { get; private set; }

        public UpdateDisablerDialog(List<SelectableApp> apps)
        {
            InitializeComponent();
            Apps = apps;
            AppListBox.ItemsSource = Apps;
            SelectedApps = new List<SelectableApp>();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var app in Apps)
            {
                app.IsSelected = true;
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var app in Apps)
            {
                app.IsSelected = false;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            SelectedApps = Apps.Where(a => a.IsSelected).ToList();

            if (SelectedApps.Count == 0)
            {
                MessageBoxHelper.Show("Please select at least one app to disable updates for.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
