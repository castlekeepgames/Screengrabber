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
        ClipboardColumn.ItemsSource = Enum.GetValues<ClipboardContent>();
        Grid.ItemsSource = _rows;
        // Row headers show the number key.
        Grid.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        Grid.SelectionChanged += (_, _) => UpdateButtons();

        AddButton.Click += (_, _) => Add();
        RemoveButton.Click += (_, _) => Remove();
        UpButton.Click += (_, _) => Move(-1);
        DownButton.Click += (_, _) => Move(+1);
        OkButton.Click += (_, _) => Ok();
        UpdateButtons();
    }

    private ShareTarget? Selected => Grid.SelectedItem as ShareTarget;

    private void Add()
    {
        CommitEdits();
        var target = new ShareTarget { Label = "New target", Clipboard = ClipboardContent.Image };
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
        AddButton.IsEnabled = _rows.Count < MaxTargets;
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
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
