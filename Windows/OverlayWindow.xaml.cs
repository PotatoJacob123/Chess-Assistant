
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ChessAssistant.Windows;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(
        IntPtr hWnd,
        int nIndex,
        int dwNewLong);

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += OverlayWindow_SourceInitialized;
    }

    // Make the overlay ignore mouse clicks.
    private void OverlayWindow_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        extendedStyle |= WS_EX_TRANSPARENT;
        extendedStyle |= WS_EX_LAYERED;
        extendedStyle |= WS_EX_NOACTIVATE;

        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);
    }

    // Position the overlay over the chessboard.
    public void SetBoardBounds(
        double left,
        double top,
        double size)
    {
        Left = left;
        Top = top;

        Width = size;
        Height = size;

        OverlayCanvas.Width = size;
        OverlayCanvas.Height = size;
    }

    private Point SquareCenter(string square, bool whiteAtBottom = true)
        {
            if (square.Length != 2 ||
                square[0] < 'a' || square[0] > 'h' ||
                square[1] < '1' || square[1] > '8')
            {
                throw new ArgumentException("Invalid chess square.");
            }

            int file = square[0] - 'a';
            int rank = square[1] - '1';

            double squareSize = Width / 8.0;

            double x;
            double y;

            if (whiteAtBottom)
            {
                x = (file + 0.5) * squareSize;
                y = (7 - rank + 0.5) * squareSize;
            }
            else
            {
                x = (7 - file + 0.5) * squareSize;
                y = (rank + 0.5) * squareSize;
            }

            return new Point(x, y);
        }

    public void DrawArrow(
        string from,
        string to,
        bool whiteAtBottom = true)
    {
        OverlayCanvas.Children.Clear();

        Point start = SquareCenter(from, whiteAtBottom);
        Point end = SquareCenter(to, whiteAtBottom);

        Vector direction = end - start;
        direction.Normalize();

        const double arrowLength = 30;
        const double arrowWidth = 16;

        Point arrowBase = end - direction * arrowLength;

        Vector perpendicular = new Vector(
            -direction.Y,
            direction.X);

        Point left = arrowBase + perpendicular * arrowWidth;
        Point right = arrowBase - perpendicular * arrowWidth;

        Line shaft = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = arrowBase.X,
            Y2 = arrowBase.Y,
            Stroke = Brushes.LimeGreen,
            StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.85
        };

        Polygon head = new Polygon
        {
            Points = new PointCollection
            {
                end,
                left,
                right
            },
            Fill = Brushes.LimeGreen,
            Opacity = 0.85
        };

        OverlayCanvas.Children.Add(shaft);
        OverlayCanvas.Children.Add(head);
    }
    public void ClearArrows()
    {
        OverlayCanvas.Children.Clear();
    }
}
