using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Screengrabber;

/// <summary>Searchable list of installed apps; <see cref="Selected"/> holds the choice after OK.</summary>
public partial class AppPickerWindow : Window
{
    internal InstalledApp? Selected { get; private set; }

    internal AppPickerWindow()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => CollectionViewSource.GetDefaultView(AppList.ItemsSource)?.Refresh();
        AppList.SelectionChanged += (_, _) => AddButton.IsEnabled = AppList.SelectedItem != null;
        AppList.MouseDoubleClick += (_, _) => Choose();
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            // Down arrow moves from the search box into the list.
            if (e.Key == Key.Down && AppList.Items.Count > 0)
            {
                AppList.SelectedIndex = Math.Max(AppList.SelectedIndex, 0);
                AppList.Focus();
                e.Handled = true;
            }
        };
        AddButton.Click += (_, _) => Choose();
        CancelButton.Click += (_, _) => DialogResult = false;
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            List<InstalledApp> apps = await InstalledApps.ListAsync();
            AppList.ItemsSource = apps;
            ICollectionView view = CollectionViewSource.GetDefaultView(apps);
            view.Filter = item => SearchBox.Text.Length == 0 ||
                ((InstalledApp)item).Name.Contains(SearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
            LoadingText.Visibility = Visibility.Collapsed;
        };
    }

    private void Choose()
    {
        if (AppList.SelectedItem is not InstalledApp app)
            return;
        Selected = app;
        DialogResult = true;
    }
}
