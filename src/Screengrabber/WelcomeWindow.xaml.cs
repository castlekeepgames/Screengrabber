using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Screengrabber;

/// <summary>
/// First-run setup: explains the keys, offers the apps found on this PC as share targets, and asks about startup.
/// Closing the window any way keeps the current choices.
/// </summary>
public partial class WelcomeWindow : Window
{
    /// <summary>The picker's number keys are 1–9.</summary>
    private const int MaxTargets = 9;

    private readonly List<(ShareTarget Target, CheckBox Check)> _choices = new();
    private bool _finished;

    /// <summary>Raised once, when the window closes, with the chosen targets (in order) and the startup choice.</summary>
    internal event EventHandler<(List<ShareTarget> Targets, bool StartWithWindows)>? Finished;

    internal WelcomeWindow(Settings settings)
    {
        InitializeComponent();

        AddKeyRow(settings.CaptureHotkey, "Take a screenshot: drag a box. Press again for the whole screen.");
        AddKeyRow(settings.CaptureHotkey, "After a screenshot: copy its file path");
        AddKeyRow(settings.CopyImageHotkey, "After a screenshot: copy the image");
        AddKeyRow(settings.ShareHotkey, "After a screenshot: share it to one of the apps below");
        ShareNote.Text = $"These appear when you press {settings.ShareHotkey.DisplayText}. Untick any you don't want; you can change this later in Settings.";

        AddChoice(Settings.EmailTarget(), "Email (your default mail app)");

        AddAppButton.Click += (_, _) => AddFromPicker();
        DoneButton.Click += (_, _) => Close();
        Closed += (_, _) => Finish();
        Loaded += async (_, _) =>
        {
            List<InstalledApp> apps = await InstalledApps.ListAsync();
            foreach (InstalledApp app in InstalledApps.Suggested(apps))
                AddChoice(InstalledApps.ToShareTarget(app));
            LoadingText.Visibility = Visibility.Collapsed;
        };
    }

    private void AddKeyRow(Hotkey hotkey, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var chip = new Border { Style = (Style)Resources["KeyChip"] };
        chip.Child = new TextBlock { Text = hotkey.DisplayText, Style = (Style)Resources["KeyText"] };
        row.Children.Add(chip);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        KeyRows.Children.Add(row);
    }

    private void AddChoice(ShareTarget target, string? label = null)
    {
        var check = new CheckBox
        {
            Content = label ?? target.Label,
            IsChecked = _choices.Count(c => c.Check.IsChecked == true) < MaxTargets,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _choices.Add((target, check));
        TargetList.Children.Add(check);
    }

    private void AddFromPicker()
    {
        var picker = new AppPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is InstalledApp app)
            AddChoice(InstalledApps.ToShareTarget(app));
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        var targets = _choices.Where(c => c.Check.IsChecked == true).Select(c => c.Target).Take(MaxTargets).ToList();
        Finished?.Invoke(this, (targets, StartWithWindowsCheck.IsChecked == true));
    }
}
