using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace Screengrabber;

/// <summary>
/// "Share to…" popup: one button per share target, picked by click or number key. Closes on Esc or when it loses focus.
/// </summary>
public partial class SharePickerWindow : Window
{
    private readonly string _imagePath;
    private readonly IReadOnlyList<ShareTarget> _targets;
    private readonly Rectangle _workArea;
    private bool _sharing;
    private bool _closing;

    /// <summary>Raised if a share throws, so the app can log it.</summary>
    internal event EventHandler<Exception>? ShareFailed;

    /// <param name="workArea">The monitor's work area in physical pixels.</param>
    internal SharePickerWindow(string imagePath, IReadOnlyList<ShareTarget> targets, Rectangle workArea)
    {
        _imagePath = imagePath;
        _targets = targets;
        _workArea = workArea;
        InitializeComponent();

        FileName.Text = Path.GetFileName(imagePath);
        for (int i = 0; i < targets.Count; i++)
            Targets.Items.Add(CreateButton(i, targets[i]));

        KeyDown += OnKeyDown;
        // Clicking elsewhere cancels, like a menu. While sharing, focus moving to the target app is expected.
        // Closing itself also deactivates the window, so ignore that one.
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (!_sharing && !_closing)
                Close();
        };
    }

    internal void ShowCentered()
    {
        // Invisible until positioned, so it doesn't flash wherever Windows first puts it.
        Opacity = 0;
        Show();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        // First move onto the right monitor so WPF picks up its DPI, then center using the real size.
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _workArea.X, _workArea.Y, 0, 0, Native.SWP_NOSIZE);
        UpdateLayout();
        double scale = Native.ScaleAt(_workArea.X + _workArea.Width / 2, _workArea.Y + _workArea.Height / 2);
        int width = (int)Math.Round(ActualWidth * scale);
        int x = _workArea.X + (_workArea.Width - width) / 2;
        int y = _workArea.Y + (int)(_workArea.Height * 0.3);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE);
        Opacity = 1;
        Activate();
    }

    private Button CreateButton(int index, ShareTarget target)
    {
        var content = new StackPanel();
        content.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                Text = (index + 1).ToString(),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        content.Children.Add(new TextBlock
        {
            Text = target.Label,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        var button = new Button { Content = content, Style = (Style)Resources["TargetButton"] };
        button.Click += (_, _) => Choose(index);
        return button;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        int number = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => 0,
        };
        if (number >= 1 && number <= _targets.Count)
            Choose(number - 1);
    }

    private async void Choose(int index)
    {
        if (_sharing)
            return;
        _sharing = true;

        ShareTarget target = _targets[index];
        Targets.IsEnabled = false;
        Status.Text = $"Opening {target.Label}…";
        Status.Visibility = Visibility.Visible;

        try
        {
            // The picker stays open (and in the foreground) until the target has focus, so we keep the right to hand it over.
            await ShareService.Share(target, _imagePath);
        }
        catch (Exception ex)
        {
            ShareFailed?.Invoke(this, ex);
        }
        finally
        {
            Close();
        }
    }
}
