using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace Screengrabber;

/// <summary>
/// Full-screen overlay for one monitor: shows the frozen screenshot, dims it, and lets the user drag a box.
/// </summary>
public partial class SnipOverlayWindow : Window
{
    /// <summary>Drags smaller than this (physical px) are treated as a stray click, not a snip.</summary>
    private const int MinSelectionPx = 3;

    private readonly MonitorShot _shot;
    private Point? _dragStart;

    /// <summary>Raised with the selected region in the monitor image's physical pixels.</summary>
    internal event EventHandler<Rectangle>? RegionSelected;
    internal event EventHandler? Cancelled;

    internal SnipOverlayWindow(MonitorShot shot, Hotkey captureHotkey)
    {
        _shot = shot;
        InitializeComponent();
        HintText.Text = $"{captureHotkey.DisplayText} for whole screen";
        Frozen.Source = ScreenCapture.ToBitmapSource(shot.Image);
        Cursor = CrosshairCursor.For(Native.ScaleAt(shot.Bounds.X + shot.Bounds.Width / 2, shot.Bounds.Y + shot.Bounds.Height / 2));

        Root.SizeChanged += (_, _) => UpdateSelection(null);
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Cancelled?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Shows the window covering exactly this monitor's physical bounds.</summary>
    internal void ShowOnMonitor()
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        Place(hwnd);
        Show();
        // Moving onto a monitor with different DPI makes WPF resize the window; snap it back.
        Place(hwnd);
    }

    private void Place(IntPtr hwnd)
    {
        Rectangle b = _shot.Bounds;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
            Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Root);
        CaptureMouse();
        UpdateSelection(new Rect(_dragStart.Value, _dragStart.Value));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is Point start)
            UpdateSelection(new Rect(start, Clamp(e.GetPosition(Root))));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not Point start)
            return;

        ReleaseMouseCapture();
        _dragStart = null;

        Rectangle region = ToPhysical(new Rect(start, Clamp(e.GetPosition(Root))));
        if (region.Width < MinSelectionPx || region.Height < MinSelectionPx)
        {
            UpdateSelection(null);
            return;
        }

        RegionSelected?.Invoke(this, region);
    }

    private Point Clamp(Point p) =>
        new(Math.Clamp(p.X, 0, Root.ActualWidth), Math.Clamp(p.Y, 0, Root.ActualHeight));

    /// <summary>Converts a rect in this window's DIPs to the monitor image's physical pixels.</summary>
    private Rectangle ToPhysical(Rect dip)
    {
        double sx = _shot.Image.Width / Root.ActualWidth;
        double sy = _shot.Image.Height / Root.ActualHeight;
        int left = (int)Math.Round(dip.Left * sx);
        int top = (int)Math.Round(dip.Top * sy);
        int right = Math.Min(_shot.Image.Width, (int)Math.Round(dip.Right * sx));
        int bottom = Math.Min(_shot.Image.Height, (int)Math.Round(dip.Bottom * sy));
        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private void UpdateSelection(Rect? selection)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));

        if (selection is not Rect sel || sel.Width < 1 || sel.Height < 1)
        {
            Dim.Data = full;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeTag.Visibility = Visibility.Collapsed;
            return;
        }

        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(full);
        dim.Children.Add(new RectangleGeometry(sel));
        Dim.Data = dim;

        Canvas.SetLeft(SelectionBorder, sel.Left);
        Canvas.SetTop(SelectionBorder, sel.Top);
        SelectionBorder.Width = sel.Width;
        SelectionBorder.Height = sel.Height;
        SelectionBorder.Visibility = Visibility.Visible;

        Rectangle px = ToPhysical(sel);
        SizeText.Text = $"{px.Width} × {px.Height}";
        SizeTag.Visibility = Visibility.Visible;
        SizeTag.UpdateLayout();
        // Sit just above the selection, or inside it when there's no room at the top of the screen.
        double tagTop = sel.Top - SizeTag.ActualHeight - 4;
        Canvas.SetLeft(SizeTag, sel.Left);
        Canvas.SetTop(SizeTag, tagTop >= 0 ? tagTop : sel.Top + 4);
    }
}
