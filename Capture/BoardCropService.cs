
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Linq;

namespace ChessAssistant.Capture;

public static class BoardCropService
{
    // Crop directly from an image already in memory.
    public static BitmapSource CropBoard(
        BitmapSource source,
        string calibrationPath)
    {
        double[][] corners =
            JsonSerializer.Deserialize<double[][]>(
                File.ReadAllText(calibrationPath))
            ?? throw new InvalidOperationException(
                "Could not read board calibration.");

        if (corners.Length != 4 ||
            corners.Any(p => p.Length != 2))
        {
            throw new InvalidOperationException(
                "Calibration must contain four corners.");
        }

        int width = source.PixelWidth;
        int height = source.PixelHeight;

        double left = corners.Min(p => p[0]) * width;
        double top = corners.Min(p => p[1]) * height;
        double right = corners.Max(p => p[0]) * width;
        double bottom = corners.Max(p => p[1]) * height;

        int x = Math.Max(0, (int)Math.Round(left));
        int y = Math.Max(0, (int)Math.Round(top));

        Int32Rect rect = new Int32Rect(
            x,
            y,
            Math.Min(width - x, (int)Math.Round(right - left)),
            Math.Min(height - y, (int)Math.Round(bottom - top)));

        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException(
                "Invalid crop dimensions.");

        CroppedBitmap cropped = new CroppedBitmap(source, rect);
        cropped.Freeze();

        return cropped;
    }

    // Save a cropped image when Python recognition needs a file.
    public static string SaveCroppedBoard(
        BitmapSource cropped,
        string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));

        using FileStream stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write);

        encoder.Save(stream);

        return outputPath;
    }

    // Keep the original file-based method for previews.
    public static string CropBoard(
        string imagePath,
        string calibrationPath,
        string outputPath)
    {
        BitmapImage source = new BitmapImage();

        source.BeginInit();
        source.UriSource = new Uri(imagePath);
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.EndInit();
        source.Freeze();

        BitmapSource cropped = CropBoard(source, calibrationPath);

        return SaveCroppedBoard(cropped, outputPath);
    }
}
