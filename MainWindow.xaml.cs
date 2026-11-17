
using System;
using System.Windows;
using ChessAssistant.Windows;
using System.IO;
using System.Text.Json;
using ChessAssistant.Capture;
using ChessAssistant.Recognition;
using ChessAssistant.Chess;
using ChessAssistant.Engine;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace ChessAssistant;

public partial class MainWindow : Window
{
    private OverlayWindow? _overlayWindow;

    private Rect? _selectedBoardBounds;
    private Rect? _selectedBoardPixelBounds;

    private DispatcherTimer? _monitorTimer;
    private bool _isProcessingBoard;
    private string? _previousPosition;
    private char _sideToMove = 'w';
    private string? _lastRecognizedPosition;
    private StockfishService? _stockfish;
    private long _analysisRequestId;
    private ChessImageRecognitionService? _recognitionService;
    private string? _pendingPosition;
    private int _pendingPositionCount;
    // The overlay maps UCI squares onto the captured board using this view.
    // Start in the standard chess orientation to match the initial button label.
    private bool _whiteAtBottom = true;

    public MainWindow()
    {
        InitializeComponent();
        UpdateBoardOrientationButton();
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorTimer?.Stop();
        _recognitionService?.Dispose();
        _stockfish?.Dispose();

        base.OnClosed(e);
    }
    
    private async void SelectBoard_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;

        await System.Threading.Tasks.Task.Delay(300);

        BoardSelectorWindow selector = new BoardSelectorWindow();

        bool? result = selector.ShowDialog();

        if (result == true &&
            selector.SelectedBoardBounds.HasValue &&
            selector.SelectedBoardPixelBounds.HasValue)
        {
            _selectedBoardBounds = selector.SelectedBoardBounds.Value;
            _selectedBoardPixelBounds = selector.SelectedBoardPixelBounds.Value;

            Rect bounds = _selectedBoardBounds.Value;

            // Position the overlay over the selected board.
            _overlayWindow ??= new OverlayWindow();

            _overlayWindow.SetBoardBounds(
                bounds.Left,
                bounds.Top,
                bounds.Width);

            bool overlayWasVisible = _overlayWindow.IsVisible;
            if (overlayWasVisible)
                _overlayWindow.Hide();

            try
            {
                // Save the screenshot in the user's Pictures folder.
                string picturesFolder =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyPictures);

                string folder = Path.Combine(
                    picturesFolder,
                    "ChessAssistant");

                string filePath = Path.Combine(
                    folder,
                    "board-capture.png");

                ScreenCaptureService.CaptureAndSave(
                    selector.SelectedBoardPixelBounds.Value,
                    filePath);

                string calibrationPath = Path.Combine(
                    folder,
                    "board-calibration.json");
                double[][] fullBoardCalibration =
                [
                    [0.0, 0.0],
                    [1.0, 0.0],
                    [1.0, 1.0],
                    [0.0, 1.0]
                ];
                File.WriteAllText(
                    calibrationPath,
                    JsonSerializer.Serialize(fullBoardCalibration));

                BoardStatus.Text =
                    $"Board captured and calibrated from the selected square. {filePath}";
            }
            catch (Exception ex)
            {
                BoardStatus.Text = "Capture failed: " + ex.Message;
            }
            finally
            {
                if (overlayWasVisible)
                    _overlayWindow.Show();
            }
        }
        else
        {
            BoardStatus.Text = "Board selection cancelled.";
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    private void StartAnalysis_Click(
        object sender,
        RoutedEventArgs e)
    {
        // Clicking again stops monitoring.
        if (_monitorTimer != null && _monitorTimer.IsEnabled)
        {
            _monitorTimer.Stop();
            EngineStatus.Text = "Live monitoring stopped.";
            return;
        }

        if (!_selectedBoardPixelBounds.HasValue)
        {
            MessageBox.Show("Please select a chessboard first.");
            return;
        }

        string picturesFolder =
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures);

        string folder = Path.Combine(
            picturesFolder,
            "ChessAssistant");

        string calibrationPath = Path.Combine(
            folder,
            "board-calibration.json");

        if (!File.Exists(calibrationPath))
        {
            MessageBox.Show("Please calibrate the board first.");
            return;
        }

        _previousPosition = null;
        _pendingPosition = null;
        _pendingPositionCount = 0;

        _monitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _monitorTimer.Tick += MonitorBoard_Tick;
        _monitorTimer.Start();

        EngineStatus.Text = "Live monitoring started (1-second interval).";
        MonitorBoard_Tick(this, EventArgs.Empty);
    }

    private async void MonitorBoard_Tick(
        object? sender,
        EventArgs e)
    {
        // Prevent overlapping recognition operations.
        if (_isProcessingBoard)
            return;

        _isProcessingBoard = true;

        try
        {
            string picturesFolder =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures);

            string folder = Path.Combine(
                picturesFolder,
                "ChessAssistant");

            string imagePath = Path.Combine(
                folder,
                "board-capture.png");

            string calibrationPath = Path.Combine(
                folder,
                "board-calibration.json");

            string croppedImagePath = Path.Combine(
                folder,
                "board-cropped.png");

            // ScreenCaptureService omits CAPTUREBLT, so the layered arrow
            // overlay is excluded without hiding it and flickering on screen.
            var capturedImage = ScreenCaptureService.Capture(
                _selectedBoardPixelBounds!.Value);

            // Crop the chessboard directly in memory.
            var croppedImage = BoardCropService.CropBoard(
                capturedImage,
                calibrationPath);

            // Save only the cropped image for Python recognition.
            BoardCropService.SaveCroppedBoard(
                croppedImage,
                croppedImagePath);

            // Recognise the current position.
            _recognitionService ??= new ChessImageRecognitionService();

            string currentPosition =
                await _recognitionService.RecognizeBoardAsync(croppedImagePath);

            // Compare with the previous position.
            _lastRecognizedPosition = currentPosition;

            if (_previousPosition == null)
            {
                _previousPosition = currentPosition;

                EngineStatus.Text = "Initial position detected.";

                await AnalyzePositionAsync(currentPosition);
            }
            else if (_previousPosition != currentPosition)
            {
                if (currentPosition == _pendingPosition)
                {
                    _pendingPositionCount++;
                }
                else
                {
                    _pendingPosition = currentPosition;
                    _pendingPositionCount = 1;
                }

                // Require the same changed position to be detected twice.
                if (_pendingPositionCount >= 2)
                {
                    _previousPosition = currentPosition;

                    _pendingPosition = null;
                    _pendingPositionCount = 0;

                    _sideToMove = _sideToMove == 'w' ? 'b' : 'w';

                    EngineStatus.Text =
                        $"{(_sideToMove == 'w' ? "White" : "Black")} to move. Analysing...";

                    await AnalyzePositionAsync(currentPosition);
                }
            }
            else
            {
                _pendingPosition = null;
                _pendingPositionCount = 0;

                EngineStatus.Text = "Monitoring board... No change detected.";
            }
        }
        catch (Exception ex)
        {
            EngineStatus.Text =
                "Monitoring error: " + ex.Message;
        }
        finally
        {
            _isProcessingBoard = false;
        }
    }


    private async void RecognizePieces_Click(
        object sender,
        RoutedEventArgs e)
    {
        long requestId = ++_analysisRequestId;

        string picturesFolder =
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures);

        string folder = Path.Combine(
            picturesFolder,
            "ChessAssistant");

        string imagePath = Path.Combine(
            folder,
            "board-capture.png");

        string calibrationPath = Path.Combine(
            folder,
            "board-calibration.json");

        string croppedImagePath = Path.Combine(
            folder,
            "board-cropped.png");

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(
                "No board capture found. Select a chessboard first.");
            return;
        }

        if (!File.Exists(calibrationPath))
        {
            MessageBox.Show(
                "Board calibration not found. Calibrate the board first.");
            return;
        }

        try
        {
            // Crop the chessboard automatically.
            BoardCropService.CropBoard(
                imagePath,
                calibrationPath,
                croppedImagePath);

            // Recognize the chess pieces using Python.
            ChessImageRecognitionService recognizer =
                new ChessImageRecognitionService();

            string piecePlacement =
                await recognizer.RecognizeBoardAsync(
                    croppedImagePath);

            if (requestId != _analysisRequestId)
                return;

            // Display the recognized position for testing.
            char sideToMove = _sideToMove;
            string fen = FenBuilder.Create(
                ToStandardChessOrientation(piecePlacement),
                sideToMove,
                "KQkq",
                "-",
                0,
                1);
            PositionEvaluation.Text = "Evaluating current position...";
            StockfishAnalysisResult analysis =
                await GetStockfishService().AnalyzeAsync(fen, 8);

            if (requestId != _analysisRequestId)
                return;

            string bestMove = analysis.BestMove;
            PositionEvaluation.Text = FormatPositionEvaluation(analysis, sideToMove);

            // Check that Stockfish returned a valid UCI move.
            if (bestMove.Length < 4 ||
                bestMove == "0000")
            {
                MessageBox.Show("Stockfish could not find a legal move.");
                return;
            }

            // Make sure a board has been selected.
            if (!_selectedBoardBounds.HasValue)
            {
                MessageBox.Show("Please select the chessboard first.");
                return;
            }

            // Position the overlay over the selected chessboard.
            Rect bounds = _selectedBoardBounds.Value;

            _overlayWindow ??= new OverlayWindow();

            _overlayWindow.SetBoardBounds(
                bounds.Left,
                bounds.Top,
                bounds.Width);

            // Show the overlay.
            if (!_overlayWindow.IsVisible)
            {
                _overlayWindow.Show();
            }

            // Extract the starting and ending squares.
            string from = bestMove.Substring(0, 2);
            string to = bestMove.Substring(2, 2);

            // Draw Stockfish's recommended move.
            _overlayWindow.DrawArrow(from, to, _whiteAtBottom);

            EngineStatus.Text = $"Best move: {bestMove}";

            // MessageBox.Show(
            //     $"Recognized Position:\n{fen}\n\nBest Move: {bestMove}",
            //     "Chess Analysis",
            //     MessageBoxButton.OK,
            //     MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (requestId == _analysisRequestId)
            {
                PositionEvaluation.Text = "Position evaluation: unavailable";
                MessageBox.Show(
                    ex.Message,
                    "Recognition Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void PreviewBoardCrop_Click(object sender, RoutedEventArgs e)
        {
            string picturesFolder =
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            string folder = Path.Combine(picturesFolder, "ChessAssistant");

            string imagePath = Path.Combine(folder, "board-capture.png");
            string calibrationPath = Path.Combine(folder, "board-calibration.json");
            string outputPath = Path.Combine(folder, "board-cropped.png");

            try
            {
                string croppedPath = BoardCropService.CropBoard(
                    imagePath,
                    calibrationPath,
                    outputPath);

                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = croppedPath,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Board Crop Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    private void ClearArrow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _overlayWindow?.ClearArrows();

        EngineStatus.Text = "Arrow cleared";
    }
    private async void WhiteToMove_Click(
        object sender,
        RoutedEventArgs e)
    {
        _sideToMove = 'w';
        EngineStatus.Text = _lastRecognizedPosition == null
            ? "White to move selected. Waiting for the first board scan..."
            : "White to move selected.";

        if (_lastRecognizedPosition != null)
            await AnalyzePositionAsync(_lastRecognizedPosition, 4);
    }

    private async void BlackToMove_Click(
        object sender,
        RoutedEventArgs e)
    {
        _sideToMove = 'b';
        EngineStatus.Text = _lastRecognizedPosition == null
            ? "Black to move selected. Waiting for the first board scan..."
            : "Black to move selected.";

        if (_lastRecognizedPosition != null)
            await AnalyzePositionAsync(_lastRecognizedPosition, 4);
    }
    private async Task AnalyzePositionAsync(string piecePlacement, int depth = 8)
    {
        long requestId = ++_analysisRequestId;
        char sideToMove = _sideToMove;

        try
        {
            PositionEvaluation.Text = "Evaluating current position...";

            string fen = FenBuilder.Create(
                ToStandardChessOrientation(piecePlacement),
                sideToMove,
                "KQkq",
                "-",
                0,
                1);

            StockfishAnalysisResult analysis =
                await GetStockfishService().AnalyzeAsync(fen, depth);

            if (requestId != _analysisRequestId)
                return;

            string bestMove = analysis.BestMove;
            PositionEvaluation.Text = FormatPositionEvaluation(analysis, sideToMove);

            if (bestMove.Length < 4 || bestMove == "0000")
            {
                EngineStatus.Text = "No legal move found.";
                return;
            }

            if (!_selectedBoardBounds.HasValue)
                return;

            Rect bounds = _selectedBoardBounds.Value;

            _overlayWindow ??= new OverlayWindow();

            _overlayWindow.SetBoardBounds(
                bounds.Left,
                bounds.Top,
                bounds.Width);

            if (!_overlayWindow.IsVisible)
                _overlayWindow.Show();

            _overlayWindow.DrawArrow(
                bestMove.Substring(0, 2),
                bestMove.Substring(2, 2),
                _whiteAtBottom);

            EngineStatus.Text =
                $"{(sideToMove == 'w' ? "White" : "Black")} best move: {bestMove}";
        }
        catch (Exception ex)
        {
            if (requestId == _analysisRequestId)
            {
                PositionEvaluation.Text = "Position evaluation: unavailable";
                EngineStatus.Text = "Analysis error: " + ex.Message;
            }
        }
    }

    private StockfishService GetStockfishService()
    {
        if (_stockfish == null)
        {
            string enginePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Engine",
                "Stockfish",
                "stockfish.exe");

            _stockfish = new StockfishService(enginePath);
        }

        return _stockfish;
    }

    private static string FormatPositionEvaluation(
        StockfishAnalysisResult analysis,
        char sideToMove)
    {
        int whitePerspective = sideToMove == 'w' ? 1 : -1;

        if (analysis.MateIn.HasValue)
        {
            int mateIn = analysis.MateIn.Value * whitePerspective;
            string winner = mateIn > 0 ? "White" : "Black";
            return mateIn == 0
                ? "Position evaluation: Checkmate"
                : $"Position evaluation: {winner} is winning (mate in {Math.Abs(mateIn)})";
        }

        if (!analysis.EvaluationCentipawns.HasValue)
            return "Position evaluation: unavailable";

        double pawns = analysis.EvaluationCentipawns.Value * whitePerspective / 100.0;
        string assessment = Math.Abs(pawns) < 0.4
            ? "Position is roughly equal"
            : pawns > 2.0
                ? "White is winning"
                : pawns > 0
                    ? "White is ahead"
                    : pawns < -2.0
                        ? "Black is winning"
                        : "Black is ahead";

        return $"{assessment} ({pawns:+0.00;-0.00;0.00})";
    }

    private async void FlipBoardOrientation_Click(
        object sender,
        RoutedEventArgs e)
    {
        _whiteAtBottom = !_whiteAtBottom;
        UpdateBoardOrientationButton();

        // Recalculate the arrow for the current position.
        if (_lastRecognizedPosition != null)
        {
            await AnalyzePositionAsync(_lastRecognizedPosition, 8);
        }
    }

    private void UpdateBoardOrientationButton()
    {
        BoardOrientationButton.Content = _whiteAtBottom
            ? "Board View: White at Bottom"
            : "Board View: Black at Bottom";
    }

    // Recognition reads the captured image as if White were at the bottom.
    // Convert a black-bottom image's placement to standard FEN coordinates
    // before sending it to Stockfish. The overlay then applies the inverse
    // screen mapping when it draws Stockfish's standard UCI squares.
    private string ToStandardChessOrientation(string piecePlacement)
    {
        if (_whiteAtBottom)
            return piecePlacement;

        string[] fenRanks = piecePlacement.Split('/');
        if (fenRanks.Length != 8)
            throw new ArgumentException("Invalid chess piece placement.", nameof(piecePlacement));

        char[,] board = new char[8, 8];

        for (int fenRank = 0; fenRank < 8; fenRank++)
        {
            int file = 0;
            foreach (char symbol in fenRanks[fenRank])
            {
                if (symbol >= '1' && symbol <= '8')
                {
                    int emptyCount = symbol - '0';
                    for (int i = 0; i < emptyCount; i++)
                        board[fenRank, file++] = '.';
                }
                else
                {
                    if (file >= 8)
                        throw new ArgumentException("Invalid chess piece placement.", nameof(piecePlacement));
                    board[fenRank, file++] = symbol;
                }
            }

            if (file != 8)
                throw new ArgumentException("Invalid chess piece placement.", nameof(piecePlacement));
        }

        // A 180-degree board rotation reverses both rank order and file order.
        var standardRanks = new string[8];
        for (int targetRank = 0; targetRank < 8; targetRank++)
        {
            var row = new System.Text.StringBuilder();
            int emptyCount = 0;

            for (int targetFile = 0; targetFile < 8; targetFile++)
            {
                char piece = board[7 - targetRank, 7 - targetFile];
                if (piece == '.')
                {
                    emptyCount++;
                    continue;
                }

                if (emptyCount > 0)
                {
                    row.Append(emptyCount);
                    emptyCount = 0;
                }
                row.Append(piece);
            }

            if (emptyCount > 0)
                row.Append(emptyCount);
            standardRanks[targetRank] = row.ToString();
        }

        return string.Join("/", standardRanks);
    }
}
