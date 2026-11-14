
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChessAssistant.Recognition;

public sealed class ChessImageRecognitionService : IDisposable
{
    private Process? _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _errorOutput = new();

    public async Task<string> RecognizeBoardAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException(
                "Chessboard image not found.", imagePath);

        await _gate.WaitAsync();

        try
        {
            await EnsureStartedAsync();

            if (_process == null)
                throw new InvalidOperationException(
                    "Python recognition process is not running.");

            await _process.StandardInput.WriteLineAsync(imagePath);
            await _process.StandardInput.FlushAsync();

            string? output =
                await _process.StandardOutput.ReadLineAsync();

            if (output == null)
            {
                string error;

                lock (_errorOutput)
                    error = _errorOutput.ToString();

                throw new Exception(
                    "Python recognition process stopped. " + error);
            }

            output = output.Trim();

            if (output.StartsWith("ERROR:", StringComparison.Ordinal))
                throw new Exception("Python recognition failed: " + output);

            return ConvertToFen(output);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync()
    {
        if (_process != null && !_process.HasExited)
            return;

        DisposeProcess();

        string scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "Recognition",
            "Python",
            "recognize_board.py");

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException(
                "Python recognition script not found.", scriptPath);

        ProcessStartInfo startInfo = new()
        {
            FileName = "python",
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(scriptPath);

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        lock (_errorOutput)
            _errorOutput.Clear();

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (_errorOutput)
                    _errorOutput.AppendLine(e.Data);
            }
        };

        if (!_process.Start())
            throw new Exception("Could not start Python recognition.");

        _process.BeginErrorReadLine();

        // The first recognition call waits for Python's model import.
        await Task.Yield();
    }

    private static string ConvertToFen(string output)
    {
        string[] ranks = output.Split('/');

        if (ranks.Length != 8)
            throw new Exception(
                "Recognition returned an invalid board position.");

        StringBuilder fen = new();

        for (int rankIndex = 0; rankIndex < ranks.Length; rankIndex++)
        {
            int emptySquares = 0;

            foreach (char piece in ranks[rankIndex])
            {
                if (piece == '1')
                {
                    emptySquares++;
                }
                else
                {
                    if (emptySquares > 0)
                    {
                        fen.Append(emptySquares);
                        emptySquares = 0;
                    }

                    fen.Append(piece);
                }
            }

            if (emptySquares > 0)
                fen.Append(emptySquares);

            if (rankIndex < 7)
                fen.Append('/');
        }

        return fen.ToString();
    }

    private void DisposeProcess()
    {
        if (_process == null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();

                if (!_process.WaitForExit(2000))
                    _process.Kill(true);
            }
        }
        catch
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(true);
            }
            catch
            {
                // Ignore shutdown errors.
            }
        }

        _process.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        DisposeProcess();
        _gate.Dispose();
    }
}
