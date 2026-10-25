using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Screengrabber;

/// <summary>
/// Drops a file onto whatever window is at a screen point by performing a real drag: a small chip appears
/// near the spot, the pointer presses on it, glides to the spot and releases. To the target app this is
/// exactly a user dragging the file in, so anything that accepts dropped files takes it. No clipboard.
/// </summary>
/// <remarks>
/// A drag started purely in code (no button held) depends on focus and timing and proved unreliable;
/// a drag driven by genuine mouse input doesn't.
/// </remarks>
internal static class FileDropper
{
    /// <summary>Chip size in physical pixels.</summary>
    private const int ChipSize = 32;
    /// <summary>How far (physical pixels) from the drop spot the chip appears.</summary>
    private const int ChipOffset = 90;
    /// <summary>A drag that hasn't finished by now is abandoned.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Native.POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    /// <param name="x">Screen X in physical pixels.</param>
    /// <param name="y">Screen Y in physical pixels.</param>
    /// <returns>True if the window there accepted the file.</returns>
    public static async Task<bool> DropAt(string filePath, int x, int y)
    {
        GetCursorPos(out Native.POINT original);

        // Up and to the left of the spot, or down and to the right if that would leave the screen.
        var area = System.Windows.Forms.SystemInformation.VirtualScreen;
        int chipX = x - ChipOffset - ChipSize >= area.Left ? x - ChipOffset - ChipSize : x + ChipOffset;
        int chipY = y - ChipOffset - ChipSize >= area.Top ? y - ChipOffset - ChipSize : y + ChipOffset;
        int startX = chipX + ChipSize / 2, startY = chipY + ChipSize / 2;

        var chip = new Chip(filePath);
        chip.ShowAt(chipX, chipY, ChipSize);
        try
        {
            await Task.Run(() =>
            {
                SyntheticInput.MoveMouse(startX, startY);
                Thread.Sleep(40);
                SyntheticInput.LeftButtonDown();
                Thread.Sleep(40);

                // Glide over in steps (well past the drag threshold), then hover so the target sees DragOver.
                const int Steps = 12;
                for (int i = 1; i <= Steps; i++)
                {
                    SyntheticInput.MoveMouse(startX + (x - startX) * i / Steps, startY + (y - startY) * i / Steps);
                    Thread.Sleep(15);
                }
                for (int i = 0; i < 10; i++)
                {
                    SyntheticInput.MoveMouse(x + (i % 2), y);
                    Thread.Sleep(30);
                }
                SyntheticInput.MoveMouse(x, y);
                Thread.Sleep(30);

                // If the drag never started, releasing here would be a click on the target. Release on the chip instead.
                if (!chip.DragStarted)
                {
                    SyntheticInput.MoveMouse(startX, startY);
                    Thread.Sleep(30);
                }
                SyntheticInput.LeftButtonUp();
            });

            Task finished = await Task.WhenAny(chip.Completed, Task.Delay(Timeout));
            DragDropEffects effect = finished == chip.Completed ? chip.Completed.Result : DragDropEffects.None;
            Log.Info($"DropAt({x},{y}): started={chip.DragStarted}, effect={effect}");
            return effect != DragDropEffects.None;
        }
        finally
        {
            chip.Close();
            SetCursorPos(original.X, original.Y);
        }
    }

    /// <summary>A small, briefly visible stand-in for the file that the synthesized drag starts from.</summary>
    private sealed class Chip : Window
    {
        private readonly TaskCompletionSource<DragDropEffects> _completed = new();
        private readonly string _filePath;
        private int _dragStarted;

        public Task<DragDropEffects> Completed => _completed.Task;
        public bool DragStarted => Volatile.Read(ref _dragStarted) == 1;

        public Chip(string filePath)
        {
            _filePath = filePath;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
            };

            Point? pressedAt = null;
            MouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(this);
            MouseMove += (_, e) =>
            {
                if (pressedAt is not Point start || e.LeftButton != MouseButtonState.Pressed || DragStarted)
                    return;
                Vector moved = e.GetPosition(this) - start;
                if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                Volatile.Write(ref _dragStarted, 1);
                var data = new DataObject(DataFormats.FileDrop, new[] { _filePath });
                var effect = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                _completed.TrySetResult(effect);
            };
            MouseLeftButtonUp += (_, _) =>
            {
                // Released without dragging (the fallback path): report a non-drop.
                if (!DragStarted)
                    _completed.TrySetResult(DragDropEffects.None);
            };
        }

        public void ShowAt(int x, int y, int size)
        {
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, size, size, Native.SWP_NOACTIVATE);
            Show();
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, size, size, Native.SWP_NOACTIVATE);
        }
    }
}
