
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace ChessAssistant.Windows;

public partial class BoardSelectorWindow : Window
{
    private Point _startPoint;
    private bool _isSelecting;

    public Rect? SelectedBoardBounds { get; private set; }
    public Rect? SelectedBoardPixelBounds { get; private set; }

    public BoardSelectorWindow()
    {
        InitializeComponent();

        // Cover the entire virtual desktop, including other monitors.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;

        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void Window_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        _isSelecting = true;

        SelectionRectangle.Visibility = Visibility.Visible;

        UpdateSelection(_startPoint);

        CaptureMouse();
    }

    private void Window_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isSelecting)
            return;

        Point currentPoint = e.GetPosition(SelectionCanvas);

        UpdateSelection(currentPoint);
    }

    private void Window_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isSelecting)
            return;

        Point endPoint = e.GetPosition(SelectionCanvas);

        _isSelecting = false;
        ReleaseMouseCapture();

        double left = Math.Min(_startPoint.X, endPoint.X);
        double top = Math.Min(_startPoint.Y, endPoint.Y);

        double width = Math.Abs(endPoint.X - _startPoint.X);
        double height = Math.Abs(endPoint.Y - _startPoint.Y);

        // Reject selections that are too small.
        if (width < 100 || height < 100)
        {
            InstructionText.Text =
                "Selection too small. Drag over the chessboard again.";

            SelectionRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        // A chessboard should be approximately square.
        double ratio = width / height;

        if (ratio < 0.80 || ratio > 1.20)
        {
            InstructionText.Text =
                "Please select a more square-shaped area. Try again.";

            SelectionRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        // Use a square centered inside the selected rectangle.
        double boardSize = Math.Min(width, height);

        double squareLeft = left + (width - boardSize) / 2;
        double squareTop = top + (height - boardSize) / 2;

        SelectedBoardBounds = new Rect(
            Left + squareLeft,
            Top + squareTop,
            boardSize,
            boardSize);
        // Convert the selection corners to physical screen pixels.
        Point pixelTopLeft = PointToScreen(
            new Point(squareLeft, squareTop));

        Point pixelBottomRight = PointToScreen(
            new Point(squareLeft + boardSize, squareTop + boardSize));

        SelectedBoardPixelBounds = new Rect(
            pixelTopLeft.X,
            pixelTopLeft.Y,
            pixelBottomRight.X - pixelTopLeft.X,
            pixelBottomRight.Y - pixelTopLeft.Y);

        DialogResult = true;
        Close();
    }

    private void UpdateSelection(Point currentPoint)
    {
        double left = Math.Min(_startPoint.X, currentPoint.X);
        double top = Math.Min(_startPoint.Y, currentPoint.Y);

        double width = Math.Abs(currentPoint.X - _startPoint.X);
        double height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);

        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private void Window_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedBoardBounds = null;
            DialogResult = false;
            Close();
        }
    }
}
