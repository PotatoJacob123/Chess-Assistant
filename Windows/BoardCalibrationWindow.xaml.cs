
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ChessAssistant.Windows;

public partial class BoardCalibrationWindow : Window
{
    private const double GridSize = 800;
    private const int BoardSquares = 8;

    // Order: top-left, top-right, bottom-right, bottom-left.
    private readonly Point[] _corners =
    {
        new Point(20, 20),
        new Point(780, 20),
        new Point(780, 780),
        new Point(20, 780)
    };

    private int _draggingCorner = -1;

    public Point[]? SelectedCorners { get; private set; }

    public BoardCalibrationWindow(string imagePath)
    {
        InitializeComponent();

        LoadBoardImage(imagePath);
        DrawCalibration();
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

    private void Canvas_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        Point mouse = e.GetPosition(CalibrationCanvas);

        double closestDistance = 30;
        int closestCorner = -1;

        for (int i = 0; i < _corners.Length; i++)
        {
            double dx = mouse.X - _corners[i].X;
            double dy = mouse.Y - _corners[i].Y;

            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestCorner = i;
            }
        }

        if (closestCorner == -1)
            return;

        _draggingCorner = closestCorner;

        CalibrationCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_draggingCorner == -1)
            return;

        Point mouse = e.GetPosition(CalibrationCanvas);

        mouse.X = Math.Clamp(mouse.X, 0, GridSize);
        mouse.Y = Math.Clamp(mouse.Y, 0, GridSize);

        _corners[_draggingCorner] = mouse;

        DrawCalibration();
    }

    private void Canvas_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_draggingCorner == -1)
            return;

        _draggingCorner = -1;

        CalibrationCanvas.ReleaseMouseCapture();
    }

    private void DrawCalibration()
    {
        CalibrationCanvas.Children.Clear();

        Point topLeft = _corners[0];
        Point topRight = _corners[1];
        Point bottomRight = _corners[2];
        Point bottomLeft = _corners[3];

        // Draw the 8 horizontal and 8 vertical divisions.
        for (int i = 0; i <= BoardSquares; i++)
        {
            double t = i / (double)BoardSquares;

            // Horizontal grid line.
            Point left = Lerp(topLeft, bottomLeft, t);
            Point right = Lerp(topRight, bottomRight, t);

            AddLine(left, right);

            // Vertical grid line.
            Point top = Lerp(topLeft, topRight, t);
            Point bottom = Lerp(bottomLeft, bottomRight, t);

            AddLine(top, bottom);
        }

        // Draw corner markers.
        string[] labels =
        {
            "TL", "TR", "BR", "BL"
        };

        for (int i = 0; i < _corners.Length; i++)
        {
            AddCornerMarker(_corners[i], labels[i]);
        }
    }

    private Point Lerp(Point a, Point b, double t)
    {
        return new Point(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t);
    }

    private void AddLine(Point start, Point end)
    {
        Line line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = Brushes.LimeGreen,
            StrokeThickness = 2,
            Opacity = 0.9,
            IsHitTestVisible = false
        };

        CalibrationCanvas.Children.Add(line);
    }

    private void AddCornerMarker(Point position, string label)
    {
        const double markerSize = 24;

        Ellipse marker = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = Brushes.LimeGreen,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(marker, position.X - markerSize / 2);
        Canvas.SetTop(marker, position.Y - markerSize / 2);

        CalibrationCanvas.Children.Add(marker);

        TextBlock text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.Yellow,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(text, position.X + 14);
        Canvas.SetTop(text, position.Y - 10);

        CalibrationCanvas.Children.Add(text);
    }

    private void SaveCalibration_Click(
        object sender,
        RoutedEventArgs e)
    {
        // Check that the corners form a valid quadrilateral.
        if (!IsValidQuadrilateral())
        {
            MessageBox.Show(
                "The four corners overlap or form an invalid shape. Please adjust them.",
                "Invalid Calibration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SelectedCorners = (Point[])_corners.Clone();

        DialogResult = true;
        Close();
    }

    private bool IsValidQuadrilateral()
    {
        double area = 0;

        for (int i = 0; i < 4; i++)
        {
            Point current = _corners[i];
            Point next = _corners[(i + 1) % 4];

            area += current.X * next.Y -
                    next.X * current.Y;
        }

        return Math.Abs(area) / 2 > 10000;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectedCorners = null;
        DialogResult = false;
        Close();
    }
}
