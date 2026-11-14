
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChessAssistant.Recognition;

public sealed class BoardSquarePrediction
{
    public string Square { get; init; } = "";
    public char Piece { get; init; }
    public float Confidence { get; init; }
}

public sealed class ChessBoardRecognitionResult
{
    public string PiecePlacement { get; init; } = "";
    public string Orientation { get; init; } = "";
    public float AverageConfidence { get; init; }
    public float MinimumConfidence { get; init; }
    public IReadOnlyList<BoardSquarePrediction> Squares { get; init; }
        = Array.Empty<BoardSquarePrediction>();
}

public sealed class ChessPieceRecognitionService : IDisposable
{
    private const int BoardSize = 256;
    private const int SquareSize = 32;
    private const int SquareCount = 64;
    private const int InputSize = 1024;
    private const int ClassCount = 13;

    // Model class order: empty, white pieces, black pieces.
    private const string ClassLabels = "1KQRBNPkqrbnp";

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    public ChessPieceRecognitionService(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                "Chess recognition model not found.",
                modelPath);

        SessionOptions options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = 2
        };

        _session = new InferenceSession(modelPath, options);

        if (_session.InputMetadata.Count != 1 ||
            _session.OutputMetadata.Count != 1)
        {
            throw new InvalidOperationException(
                "Unexpected ONNX model input/output configuration.");
        }

        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
    }

    public ChessBoardRecognitionResult Recognize(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException(
                "Chessboard image not found.",
                imagePath);

        byte[] grayscalePixels = LoadGrayscaleImage(
            imagePath,
            out int width,
            out int height);

        if (Math.Abs(width - height) > Math.Max(width, height) * 0.01)
        {
            throw new InvalidOperationException(
                "The captured chessboard must be square. " +
                "Please use a corrected board image.");
        }

        float[] inputData = PrepareModelInput(
            grayscalePixels,
            width,
            height);

        DenseTensor<float> inputTensor = new DenseTensor<float>(
            inputData,
            new[] { SquareCount, InputSize });

        var input = NamedOnnxValue.CreateFromTensor(
            _inputName,
            inputTensor);

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue>
            results = _session.Run(new[] { input });

        Tensor<float> outputTensor =
            results.First().AsTensor<float>();

        if (outputTensor.Dimensions.Length != 2 ||
            outputTensor.Dimensions[0] != SquareCount ||
            outputTensor.Dimensions[1] != ClassCount)
        {
            throw new InvalidOperationException(
                "Unexpected model output. Expected [64, 13].");
        }

        float[] scores = new float[SquareCount * ClassCount];

        int scoreIndex = 0;

        foreach (float score in outputTensor)
            scores[scoreIndex++] = score;

        return BuildResult(scores);
    }

    private static byte[] LoadGrayscaleImage(
        string imagePath,
        out int width,
        out int height)
    {
        BitmapDecoder decoder = BitmapDecoder.Create(
            new Uri(Path.GetFullPath(imagePath)),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        BitmapSource source = decoder.Frames[0];

        FormatConvertedBitmap grayscale = new FormatConvertedBitmap(
            source,
            PixelFormats.Gray8,
            null,
            0);

        grayscale.Freeze();

        width = grayscale.PixelWidth;
        height = grayscale.PixelHeight;

        byte[] pixels = new byte[width * height];

        grayscale.CopyPixels(
            pixels,
            width,
            0);

        return pixels;
    }

    private static float[] PrepareModelInput(
        byte[] sourcePixels,
        int sourceWidth,
        int sourceHeight)
    {
        // Resize the corrected board to 256 x 256.
        float[] resized = new float[BoardSize * BoardSize];

        for (int y = 0; y < BoardSize; y++)
        {
            double sourceY =
                ((y + 0.5) * sourceHeight / BoardSize) - 0.5;

            int y0 = (int)Math.Floor(sourceY);
            double weightY = sourceY - y0;

            for (int x = 0; x < BoardSize; x++)
            {
                double sourceX =
                    ((x + 0.5) * sourceWidth / BoardSize) - 0.5;

                int x0 = (int)Math.Floor(sourceX);
                double weightX = sourceX - x0;

                double top =
                    Sample(sourcePixels, sourceWidth, sourceHeight, x0, y0)
                    * (1.0 - weightX) +
                    Sample(sourcePixels, sourceWidth, sourceHeight, x0 + 1, y0)
                    * weightX;

                double bottom =
                    Sample(sourcePixels, sourceWidth, sourceHeight, x0, y0 + 1)
                    * (1.0 - weightX) +
                    Sample(sourcePixels, sourceWidth, sourceHeight, x0 + 1, y0 + 1)
                    * weightX;

                resized[y * BoardSize + x] =
                    (float)((top * (1.0 - weightY) +
                             bottom * weightY) / 255.0);
            }
        }

        float[] input = new float[SquareCount * InputSize];

        // Model expects A1 through H8 order.
        for (int rank = 0; rank < 8; rank++)
        {
            for (int file = 0; file < 8; file++)
            {
                int squareIndex = rank * 8 + file;

                // Rank 1 is at the bottom of the screenshot.
                int sourceY = (7 - rank) * SquareSize;
                int sourceX = file * SquareSize;

                for (int y = 0; y < SquareSize; y++)
                {
                    for (int x = 0; x < SquareSize; x++)
                    {
                        int inputIndex =
                            squareIndex * InputSize +
                            y * SquareSize +
                            x;

                        int imageIndex =
                            (sourceY + y) * BoardSize +
                            sourceX + x;

                        input[inputIndex] = resized[imageIndex];
                    }
                }
            }
        }

        return input;
    }

    private static float Sample(
        byte[] pixels,
        int width,
        int height,
        int x,
        int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);

        return pixels[y * width + x];
    }

    private static ChessBoardRecognitionResult BuildResult(float[] scores)
    {
        char[] board = new char[SquareCount];

        List<BoardSquarePrediction> predictions = new();

        float confidenceSum = 0;
        float minimumConfidence = 1;

        for (int square = 0; square < SquareCount; square++)
        {
            int bestClass = 0;
            float bestScore = float.NegativeInfinity;

            for (int c = 0; c < ClassCount; c++)
            {
                float score = scores[square * ClassCount + c];

                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            char label = ClassLabels[bestClass];

            char piece = label == '1' ? '.' : label;

            board[square] = piece;

            int rank = square / 8;
            int file = square % 8;

            predictions.Add(new BoardSquarePrediction
            {
                Square = $"{(char)('a' + file)}{rank + 1}",
                Piece = piece,
                Confidence = bestScore
            });

            confidenceSum += bestScore;
            minimumConfidence = Math.Min(
                minimumConfidence,
                bestScore);
        }

        string placement = CreateFenPlacement(board);

        string orientation = DetectOrientation(
            ref placement);

        return new ChessBoardRecognitionResult
        {
            PiecePlacement = placement,
            Orientation = orientation,
            AverageConfidence = confidenceSum / SquareCount,
            MinimumConfidence = minimumConfidence,
            Squares = predictions
        };
    }

    private static string CreateFenPlacement(char[] board)
    {
        List<string> ranks = new();

        // FEN is written from rank 8 down to rank 1.
        for (int rank = 7; rank >= 0; rank--)
        {
            string row = "";

            int emptyCount = 0;

            for (int file = 0; file < 8; file++)
            {
                char piece = board[rank * 8 + file];

                if (piece == '.')
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        row += emptyCount;
                        emptyCount = 0;
                    }

                    row += piece;
                }
            }

            if (emptyCount > 0)
                row += emptyCount;

            ranks.Add(row);
        }

        return string.Join("/", ranks);
    }

    private static string DetectOrientation(ref string placement)
    {
        string[] rows = placement.Split('/');

        double whitePawnRankSum = 0;
        double blackPawnRankSum = 0;

        int whitePawnCount = 0;
        int blackPawnCount = 0;

        for (int row = 0; row < 8; row++)
        {
            int rank = 8 - row;

            foreach (char piece in rows[row])
            {
                if (piece == 'P')
                {
                    whitePawnRankSum += rank;
                    whitePawnCount++;
                }
                else if (piece == 'p')
                {
                    blackPawnRankSum += rank;
                    blackPawnCount++;
                }
            }
        }

        if (whitePawnCount == 0 || blackPawnCount == 0)
            return "Unknown";

        double whiteAverage =
            whitePawnRankSum / whitePawnCount;

        double blackAverage =
            blackPawnRankSum / blackPawnCount;

        // If black pawns are closer to rank 1, rotate the board.
        if (blackAverage < whiteAverage)
        {
            placement = RotatePlacement(placement);
            return "Black at bottom";
        }

        return "White at bottom";
    }

    private static string RotatePlacement(string placement)
    {
        string[] rows = placement.Split('/');

        char[,] board = new char[8, 8];

        for (int row = 0; row < 8; row++)
        {
            int file = 0;

            foreach (char value in rows[row])
            {
                if (char.IsDigit(value))
                {
                    int count = value - '0';

                    for (int i = 0; i < count; i++)
                        board[row, file++] = '.';
                }
                else
                {
                    board[row, file++] = value;
                }
            }
        }

        List<string> rotatedRows = new();

        for (int row = 7; row >= 0; row--)
        {
            string expandedRow = "";

            for (int file = 7; file >= 0; file--)
                expandedRow += board[row, file];

            string compressed = "";
            int emptyCount = 0;

            foreach (char piece in expandedRow)
            {
                if (piece == '.')
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        compressed += emptyCount;
                        emptyCount = 0;
                    }

                    compressed += piece;
                }
            }

            if (emptyCount > 0)
                compressed += emptyCount;

            rotatedRows.Add(compressed);
        }

        return string.Join("/", rotatedRows);
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
