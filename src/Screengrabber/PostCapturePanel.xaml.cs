using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Drawing.Rectangle;

namespace Screengrabber;

/// <summary>
/// Small always-on-top card in the upper right after a capture: thumbnail, filename, and the next-step hotkeys.
/// Never takes focus, so you keep typing where you were.
/// </summary>
public partial class PostCapturePanel : Window
{
    /// <summary>Distance (DIPs) from the work area's top and right edges to the card.</summary>
    private const double EdgeGap = 16;
    /// <summary>Must match the Grid margin in XAML (room for the shadow).</summary>
    private const double ShadowMargin = 12;

    private readonly DispatcherTimer _closeTimer;
    private readonly Rectangle _workArea;

    public string ImagePath { get; }
    /// <summary>The capture key already opened the folder, so pressing it again starts a new snip.</summary>
    public bool FolderOpened { get; private set; }
    internal Rectangle WorkArea => _workArea;

    /// <summary>The "copy path" row was clicked (same as pressing the copy-path hotkey).</summary>
    public event EventHandler? CopyPathRequested;

    /// <summary>The "open folder" row was clicked (same as pressing the capture hotkey).</summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>The "copy image" row was clicked (same as pressing the copy-image hotkey).</summary>
    public event EventHandler? CopyImageRequested;

    /// <summary>The "share" row was clicked (same as pressing the share hotkey).</summary>
    public event EventHandler? ShareRequested;

    /// <param name="workArea">The monitor's work area in physical pixels.</param>
    internal PostCapturePanel(string imagePath, byte[] png, int imageWidth, Rectangle workArea,
        TimeSpan autoCloseAfter, Hotkey captureHotkey, Hotkey copyPathHotkey, Hotkey copyImageHotkey, Hotkey shareHotkey)
    {
        ImagePath = imagePath;
        _workArea = workArea;
        InitializeComponent();

        CaptureKeyText.Text = captureHotkey.DisplayText;
        CopyPathKeyText.Text = copyPathHotkey.DisplayText;
        CopyImageKeyText.Text = copyImageHotkey.DisplayText;
        ShareKeyText.Text = shareHotkey.DisplayText;

        FileName.Text = Path.GetFileName(imagePath);
        Thumbnail.Source = LoadThumbnail(png, imageWidth);

        _closeTimer = new DispatcherTimer { Interval = autoCloseAfter };
        _closeTimer.Tick += (_, _) => Close();
        Closed += (_, _) => _closeTimer.Stop();

        // Hovering keeps it open; leaving gives a fresh countdown.
        MouseEnter += (_, _) => _closeTimer.Stop();
        MouseLeave += (_, _) => RestartTimer();

        CloseButton.MouseLeftButtonUp += (_, _) => Close();
        FileName.MouseLeftButtonUp += (_, _) => ScreenshotStore.OpenFolder(Path.GetDirectoryName(ImagePath)!, ImagePath);
        HookUpThumbnailDrag();
        OpenFolderRow.MouseLeftButtonUp += (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);
        CopyPathRow.MouseLeftButtonUp += (_, _) => CopyPathRequested?.Invoke(this, EventArgs.Empty);
        CopyImageRow.MouseLeftButtonUp += (_, _) => CopyImageRequested?.Invoke(this, EventArgs.Empty);
        ShareRow.MouseLeftButtonUp += (_, _) => ShareRequested?.Invoke(this, EventArgs.Empty);

        SourceInitialized += (_, _) => MakeNonActivating();
    }

    /// <summary>Click opens the image; dragging carries the file into any app (no clipboard involved).</summary>
    private void HookUpThumbnailDrag()
    {
        Point? pressedAt = null;
        Thumbnail.MouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(Thumbnail);
        Thumbnail.MouseLeftButtonUp += (_, _) =>
        {
            if (pressedAt != null)
                ScreenshotStore.OpenImage(ImagePath);
            pressedAt = null;
        };
        Thumbnail.MouseMove += (_, e) =>
        {
            if (pressedAt is not Point start || e.LeftButton != MouseButtonState.Pressed)
                return;
            Vector moved = e.GetPosition(Thumbnail) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            pressedAt = null; // a drag, not a click
            _closeTimer.Stop();
            DragDrop.DoDragDrop(Thumbnail, new DataObject(DataFormats.FileDrop, new[] { ImagePath }),
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            if (!IsMouseOver)
                RestartTimer();
        };
    }

    internal void ShowInCorner()
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        Place(hwnd);
        Show();
        // Moving onto a monitor with different DPI can make WPF nudge the window; snap it back.
        Place(hwnd);
        RestartTimer();
    }

    internal void MarkFolderOpened()
    {
        FolderOpened = true;
        MarkDone(OpenFolderHint, "✓ Folder opened");
    }

    internal void MarkPathCopied() => MarkDone(CopyPathHint, "✓ Path copied");

    internal void MarkImageCopied() => MarkDone(CopyImageHint, "✓ Image copied");

    private void MarkDone(TextBlock hint, string text)
    {
        hint.Text = text;
        hint.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        // Give a full countdown so there's still time for the next step.
        if (!IsMouseOver)
            RestartTimer();
    }

    private void RestartTimer()
    {
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    /// <summary>Anchors the card's top-right corner EdgeGap DIPs in from the work area's top-right.</summary>
    private void Place(IntPtr hwnd)
    {
        double scale = Native.ScaleAt(_workArea.Right - 1, _workArea.Top);
        int x = _workArea.Right - (int)Math.Round((Width - ShadowMargin + EdgeGap) * scale);
        int y = _workArea.Top + (int)Math.Round((EdgeGap - ShadowMargin) * scale);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    /// <summary>Clicks still work, but the window never steals focus and stays out of Alt+Tab.</summary>
    private void MakeNonActivating()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int style = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    }

    /// <summary>Decodes a downscaled copy so a 4K screenshot doesn't sit in memory at full size.</summary>
    private static BitmapSource LoadThumbnail(byte[] png, int imageWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = Math.Min(imageWidth, 600);
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
