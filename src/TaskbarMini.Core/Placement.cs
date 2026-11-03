namespace TaskbarMini.Core;

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

public static class Placement
{
    public static PixelRect? BesideTray(PixelRect taskbar, PixelRect tray, int buttonsRight,
        int width, int height, int dpi)
    {
        if (dpi <= 0 || width <= 0 || height <= 0 || taskbar.Width <= 0 || height > taskbar.Height ||
            tray.Width <= 0 || tray.Left < taskbar.Left || tray.Right > taskbar.Right ||
            tray.Top >= taskbar.Bottom || tray.Bottom <= taskbar.Top || buttonsRight < taskbar.Left)
            return null;
        int gap = Math.Max(1, (int)Math.Round(dpi / 96d));
        int x = tray.Left - width;
        if (x < buttonsRight + gap || x < taskbar.Left) return null;
        return new(x, taskbar.Top + (taskbar.Height - height) / 2, width, height);
    }
}
