using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Screengrabber;

/// <summary>Edits a copy of the share targets; <see cref="Result"/> holds them after OK.</summary>
public partial class ShareTargetsWindow : Window
{
    /// <summary>The picker's number keys are 1–9.</summary>
    private const int MaxTargets = 9;

    private readonly ObservableCollection<ShareTarget> _rows;

    internal List<ShareTarget> Result => _rows.ToList();

    internal ShareTargetsWindow(IEnumerable<ShareTarget> targets)
    {
        InitializeComponent();
        _rows = new ObservableCollection<ShareTarget>(Settings.CloneTargets(targets));

        MethodColumn.ItemsSource = Enum.GetValues<ShareMethod>();
        DeliveryColumn.ItemsSource = new[]
        {
            new KeyValuePair<ShareDelivery, string>(ShareDelivery.DropOnWindow, "Drop on app"),
            new KeyValuePair<ShareDelivery, string>(ShareDelivery.DropAtPointer, "Drop where I point"),
            new KeyValuePair<ShareDelivery, string>(ShareDelivery.TypePath, "Type the path"),
            new KeyValuePair<ShareDelivery, string>(ShareDelivery.OpenOnly, "Just open"),
        };
        Grid.ItemsSource = _rows;
        // Row headers show the number key.
        Grid.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        Grid.SelectionChanged += (_, _) => UpdateButtons();

        AddButton.Click += (_, _) => AddInstalledApp();
        AddCustomButton.Click += (_, _) => Add(new ShareTarget { Label = "New target", Delivery = ShareDelivery.DropAtPointer });
        RemoveButton.Click += (_, _) => Remove();
        UpButton.Click += (_, _) => Move(-1);
        DownButton.Click += (_, _) => Move(+1);
        OkButton.Click += (_, _) => Ok();
        UpdateButtons();
    }

    private ShareTarget? Selected => Grid.SelectedItem as ShareTarget;

    private void AddInstalledApp()
    {
        var picker = new AppPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is InstalledApp app)
            Add(InstalledApps.ToShareTarget(app));
    }

    private void Add(ShareTarget target)
    {
        CommitEdits();
        _rows.Add(target);
        Select(target);
    }

    private void Remove()
    {
        if (Selected is not ShareTarget target)
            return;
        CommitEdits();
        int index = _rows.IndexOf(target);
        _rows.Remove(target);
        Renumber();
        if (_rows.Count > 0)
            Select(_rows[Math.Min(index, _rows.Count - 1)]);
    }

    private void Move(int delta)
    {
        if (Selected is not ShareTarget target)
            return;
        CommitEdits();
        int index = _rows.IndexOf(target);
        int newIndex = index + delta;
        if (newIndex < 0 || newIndex >= _rows.Count)
            return;
        _rows.Move(index, newIndex);
        Renumber();
        Select(target);
    }

    private void Select(ShareTarget target)
    {
        Grid.SelectedItem = target;
        Grid.ScrollIntoView(target);
        UpdateButtons();
    }

    private void Renumber()
    {
        foreach (var item in Grid.Items)
        {
            if (Grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                row.Header = (row.GetIndex() + 1).ToString();
        }
    }

    private void UpdateButtons()
    {
        int index = Selected is ShareTarget t ? _rows.IndexOf(t) : -1;
        AddButton.IsEnabled = AddCustomButton.IsEnabled = _rows.Count < MaxTargets;
        RemoveButton.IsEnabled = index >= 0;
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < _rows.Count - 1;
    }

    private void CommitEdits()
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void Ok()
    {
        CommitEdits();
        for (int i = 0; i < _rows.Count; i++)
        {
            ShareTarget t = _rows[i];
            t.Label = t.Label.Trim();
            t.Launch = string.IsNullOrWhiteSpace(t.Launch) ? null : t.Launch.Trim();
            t.FocusProcess = string.IsNullOrWhiteSpace(t.FocusProcess) ? null : t.FocusProcess.Trim();
            t.KeysAfterOpening = string.IsNullOrWhiteSpace(t.KeysAfterOpening) ? null : t.KeysAfterOpening.Trim();

            if (t.Label.Length == 0)
            {
                ShowError($"Row {i + 1} needs a name.");
                return;
            }
            if (t.Launch == null && t.FocusProcess == null && t.Method == ShareMethod.App)
            {
                ShowError($"{t.Label} needs something to open, or a window to bring to the front.");
                return;
            }
            if (t.Method == ShareMethod.App && t.FocusProcess == null && t.Delivery != ShareDelivery.OpenOnly)
            {
                ShowError($"{t.Label} needs its Window process name, so the file goes into the right app.");
                return;
            }
            if (t.KeysAfterOpening != null)
            {
                try
                {
                    Hotkey.Parse(t.KeysAfterOpening);
                }
                catch (FormatException)
                {
                    ShowError($"{t.Label}: \"{t.KeysAfterOpening}\" isn't a key combination. Use something like Ctrl+K.");
                    return;
                }
            }
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
