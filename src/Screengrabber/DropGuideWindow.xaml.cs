using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Rectangle = System.Drawing.Rectangle;

namespace Screengrabber;

/// <summary>
/// Instructions shown at the top of the screen while the user picks where a screenshot goes inside an app
/// (e.g. a Slack channel or thread). Never takes focus, so the user can keep working in that app.
/// </summary>
public partial class DropGuideWindow : Window
{
    /// <summary>Give up waiting after this long.</summary>
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(2);

    /// <summary>Distance (DIPs) from the work area's top edge to the card.</summary>
    private const double EdgeGap = 16;
    /// <summary>Must match the Grid margin in XAML (room for the shadow).</summary>
    private const double ShadowMargin = 12;

    private readonly Rectangle _workArea;

    /// <param name="workArea">Work area (physical pixels) of the monitor the app is on.</param>
    internal DropGuideWindow(string label, Hotkey dropHotkey, Rectangle workArea)
    {
        _workArea = workArea;
        InitializeComponent();

        Heading.Text = $"Share to {label}";
        StepOne.Text = $"In {label}, open the chat, channel or thread it should go to.";
        KeyText.Text = dropHotkey.DisplayText;

        CloseButton.MouseLeftButtonUp += (_, _) => Close();
        var expiry = new DispatcherTimer { Interval = Expiry };
        expiry.Tick += (_, _) => Close();
        expiry.Start();
        Closed += (_, _) => expiry.Stop();

        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int style = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Shows the card centred at the top of the monitor.</summary>
    internal void ShowAtTop()
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        Place(hwnd);
        Show();
        // Moving onto a monitor with different DPI can make WPF nudge the window; snap it back.
        Place(hwnd);
    }

    internal void ShowProblem(string text)
    {
        Problem.Text = text;
        Problem.Visibility = Visibility.Visible;
    }

    private void Place(IntPtr hwnd)
    {
        double scale = Native.ScaleAt(_workArea.X + _workArea.Width / 2, _workArea.Top);
        int width = (int)Math.Round(Width * scale);
        int x = _workArea.X + (_workArea.Width - width) / 2;
        int y = _workArea.Top + (int)Math.Round((EdgeGap - ShadowMargin) * scale);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }
}
