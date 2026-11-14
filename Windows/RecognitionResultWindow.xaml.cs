
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChessAssistant.Recognition;
using System.Linq;
using ChessAssistant.Chess;
using System;
using ChessAssistant.Engine;
using System.IO;

namespace ChessAssistant.Windows;

public partial class RecognitionResultWindow : Window
{
    private string _piecePlacement = "";
    public RecognitionResultWindow(
        ChessBoardRecognitionResult result)
    {
        InitializeComponent();

        OrientationText.Text =
            $"Detected orientation: {result.Orientation}";

        ConfidenceText.Text =
            $"Average confidence: {result.AverageConfidence:P1} | " +
            $"Lowest confidence: {result.MinimumConfidence:P1}";

        _piecePlacement = result.PiecePlacement;
        UpdateFen();

        DisplayBoard(result);
    }

    private void DisplayBoard(
        ChessBoardRecognitionResult result)
    {
        BoardGrid.Children.Clear();

        // Display from rank 8 down to rank 1.
        for (int rank = 7; rank >= 0; rank--)
        {
            for (int file = 0; file < 8; file++)
            {
                string square =
                    $"{(char)('a' + file)}{rank + 1}";

                BoardSquarePrediction prediction =
                    result.Squares.First(
                        p => p.Square == square);

                Border cell = new Border
                {
                    Background = (rank + file) % 2 == 0
                        ? new SolidColorBrush(
                            Color.FromRgb(240, 217, 181))
                        : new SolidColorBrush(
                            Color.FromRgb(181, 136, 99))
                };

                StackPanel content = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                TextBlock piece = new TextBlock
                {
                    Text = GetPieceSymbol(prediction.Piece),
                    FontSize = 42,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = prediction.Piece == '.' ||
                                 char.IsUpper(prediction.Piece)
                        ? Brushes.White
                        : Brushes.Black
                };

                TextBlock label = new TextBlock
                {
                    Text = square,
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                TextBlock confidence = new TextBlock
                {
                    Text = $"{prediction.Confidence:P0}",
                    FontSize = 10,
                    Foreground = Brushes.DarkBlue,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                content.Children.Add(piece);
                content.Children.Add(label);
                content.Children.Add(confidence);

                cell.Child = content;

                BoardGrid.Children.Add(cell);
            }
        }
    }

    private static string GetPieceSymbol(char piece)
    {
        return piece switch
        {
            'K' => "♔",
            'Q' => "♕",
            'R' => "♖",
            'B' => "♗",
            'N' => "♘",
            'P' => "♙",

            'k' => "♚",
            'q' => "♛",
            'r' => "♜",
            'b' => "♝",
            'n' => "♞",
            'p' => "♟",

            _ => ""
        };
    }

    private void FenSettings_Changed(
        object sender,
        RoutedEventArgs e)
    {
        UpdateFen();
    }

    private void UpdateFen()
    {
        if (FenText == null)
            return;

        char sideToMove =
            SideToMoveBox.SelectedIndex == 1 ? 'b' : 'w';

        string castling = "";

        if (WhiteKingSide.IsChecked == true)
            castling += "K";

        if (WhiteQueenSide.IsChecked == true)
            castling += "Q";

        if (BlackKingSide.IsChecked == true)
            castling += "k";

        if (BlackQueenSide.IsChecked == true)
            castling += "q";

        if (string.IsNullOrEmpty(castling))
            castling = "-";

        string enPassant = EnPassantText.Text.Trim();

        if (string.IsNullOrEmpty(enPassant))
            enPassant = "-";

        if (!int.TryParse(HalfmoveText.Text, out int halfmove) ||
            !int.TryParse(FullmoveText.Text, out int fullmove) ||
            fullmove < 1)
        {
            FenText.Text = "Invalid move counters.";
            return;
        }

        try
        {
            FenText.Text = FenBuilder.Create(
                _piecePlacement,
                sideToMove,
                castling,
                enPassant,
                halfmove,
                fullmove);
        }
        catch (ArgumentException ex)
        {
            FenText.Text = ex.Message;
        }
    }
    private async void AnalyzeWithStockfish_Click(
        object sender,
        RoutedEventArgs e)
    {
        string fen = FenText.Text.Trim();

        string enginePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Engine",
            "Stockfish",
            "stockfish.exe");

        if (fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 6)
        {
            MessageBox.Show(
                "Please generate a valid FEN before analysing.",
                "Invalid FEN",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        BestMoveText.Text = "Stockfish is analysing...";

        try
        {
            using StockfishService stockfish =
                new StockfishService(enginePath);

            string bestMove = await stockfish.GetBestMoveAsync(
                fen,
                depth: 15);

            BestMoveText.Text =
                $"Best move: {bestMove}";
        }
        catch (Exception ex)
        {
            BestMoveText.Text = "Analysis failed.";

            MessageBox.Show(
                ex.Message,
                "Stockfish Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
