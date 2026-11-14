
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;
using System.Windows.Controls;

namespace ChessAssistant.Windows;

public partial class BoardGridWindow : Window
{
    private const double GridSize = 800;
    private const int BoardSquares = 8;

    public BoardGridWindow(string imagePath)
    {
        InitializeComponent();

        LoadBoardImage(imagePath);
        DrawGrid();
    }

    private void LoadBoardImage(string imagePath)
    {
        BitmapImage bitmap = new BitmapImage();

        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        BoardImage.Source = bitmap;
    }

    private void DrawGrid()
    {
        GridCanvas.Children.Clear();

        double squareSize = GridSize / BoardSquares;

        // Draw vertical and horizontal grid lines.
        for (int i = 0; i <= BoardSquares; i++)
        {
            double position = i * squareSize;

            Line verticalLine = new Line
            {
                X1 = position,
                Y1 = 0,
                X2 = position,
                Y2 = GridSize,
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 2,
                Opacity = 0.9
            };

            Line horizontalLine = new Line
            {
                X1 = 0,
                Y1 = position,
                X2 = GridSize,
                Y2 = position,
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 2,
                Opacity = 0.9
            };

            GridCanvas.Children.Add(verticalLine);
            GridCanvas.Children.Add(horizontalLine);
        }

        // Label each column.
        for (int i = 0; i < BoardSquares; i++)
        {
            string file = ((char)('a' + i)).ToString();

            AddLabel(
                file,
                i * squareSize + squareSize / 2 - 5,
                5);
        }

        // Label each row.
        for (int i = 0; i < BoardSquares; i++)
        {
            string rank = (8 - i).ToString();

            AddLabel(
                rank,
                5,
                i * squareSize + 5);
        }
    }

    private void AddLabel(string text, double left, double top)
    {
        System.Windows.Controls.TextBlock label =
            new System.Windows.Controls.TextBlock
            {
                Text = text,
                Foreground = Brushes.Yellow,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(
                    Color.FromArgb(150, 0, 0, 0))
            };

        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);

        GridCanvas.Children.Add(label);
    }
}
