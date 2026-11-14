
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ChessAssistant.Capture;

public static class ScreenCaptureService
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(
        IntPtr hDC, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(
        IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest,
        int xDest,
        int yDest,
        int width,
        int height,
        IntPtr hdcSource,
        int xSource,
        int ySource,
        uint rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    // Capture the screen region and return the image in memory.
    public static BitmapSource Capture(Rect bounds)
    {
        int x = (int)Math.Round(bounds.Left);
        int y = (int)Math.Round(bounds.Top);
        int width = (int)Math.Round(bounds.Width);
        int height = (int)Math.Round(bounds.Height);

        if (width <= 0 || height <= 0)
            throw new ArgumentException("Invalid capture dimensions.");

        IntPtr screenDC = IntPtr.Zero;
        IntPtr memoryDC = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            screenDC = GetDC(IntPtr.Zero);

            if (screenDC == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Could not access the screen.");

            memoryDC = CreateCompatibleDC(screenDC);

            if (memoryDC == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Could not create capture context.");

            bitmap = CreateCompatibleBitmap(screenDC, width, height);

            if (bitmap == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Could not create capture bitmap.");

            oldBitmap = SelectObject(memoryDC, bitmap);

            bool copied = BitBlt(
                memoryDC,
                0,
                0,
                width,
                height,
                screenDC,
                x,
                y,
                SRCCOPY | CAPTUREBLT);

            if (!copied)
                throw new InvalidOperationException(
                    "Screen capture failed.");

            BitmapSource image =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

            image.Freeze();

            return image;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memoryDC != IntPtr.Zero)
                SelectObject(memoryDC, oldBitmap);

            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);

            if (memoryDC != IntPtr.Zero)
                DeleteDC(memoryDC);

            if (screenDC != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    // Keep this method for manual screenshots and previews.
    public static void CaptureAndSave(Rect bounds, string filePath)
    {
        BitmapSource image = Capture(bounds);

        string? directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using FileStream stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write);

        encoder.Save(stream);
    }
}
