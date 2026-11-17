
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChessAssistant.Engine;

public sealed record StockfishAnalysisResult(
    string BestMove,
    int? EvaluationCentipawns,
    int? MateIn);

public sealed class StockfishService : IDisposable
{
    private readonly string _enginePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stderrLock = new();

    private Process? _process;
    private string _recentStandardError = string.Empty;

    public StockfishService(string enginePath)
    {
        _enginePath = enginePath;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureStartedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetBestMoveAsync(
        string fen,
        int depth = 15,
        CancellationToken cancellationToken = default)
    {
        StockfishAnalysisResult result = await AnalyzeAsync(
            fen,
            depth,
            cancellationToken);
        return result.BestMove;
    }

    public async Task<StockfishAnalysisResult> AnalyzeAsync(
        string fen,
        int depth = 15,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fen) ||
            fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 6)
        {
            throw new ArgumentException("Invalid FEN position.");
        }

        if (depth < 1 || depth > 50)
            throw new ArgumentOutOfRangeException(nameof(depth));

        await _gate.WaitAsync(cancellationToken);

        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await AnalyzeCoreAsync(fen, depth, cancellationToken);
                }
                catch (InvalidOperationException) when
                    (attempt == 0 && _process?.HasExited == true)
                {
                    _process.Dispose();
                    _process = null;
                }
            }

            throw new InvalidOperationException("Stockfish analysis failed after restarting the engine.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StockfishAnalysisResult> AnalyzeCoreAsync(
        string fen,
        int depth,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        await SendCommandAsync("ucinewgame");
        await SendCommandAsync("isready");
        await WaitForResponseAsync("readyok", cancellationToken);

        await SendCommandAsync($"position fen {fen}");
        await SendCommandAsync($"go depth {depth}");

        int? evaluationCentipawns = null;
        int? mateIn = null;

        while (true)
        {
            string line = await ReadLineAsync(
                TimeSpan.FromMinutes(2),
                cancellationToken);

            if (line.StartsWith("bestmove ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                    throw new InvalidOperationException(
                        "Stockfish returned an invalid bestmove response.");

                return new StockfishAnalysisResult(
                    parts[1],
                    evaluationCentipawns,
                    mateIn);
            }

            if (TryParseScore(line, out int? centipawns, out int? mate))
            {
                evaluationCentipawns = centipawns;
                mateIn = mate;
            }
        }
    }

    private static bool TryParseScore(
        string line,
        out int? centipawns,
        out int? mate)
    {
        centipawns = null;
        mate = null;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int scoreIndex = Array.IndexOf(parts, "score");
        if (scoreIndex < 0 || scoreIndex + 2 >= parts.Length)
            return false;

        if (parts[scoreIndex + 1] == "cp" &&
            int.TryParse(parts[scoreIndex + 2], out int cp))
        {
            centipawns = cp;
            return true;
        }

        if (parts[scoreIndex + 1] == "mate" &&
            int.TryParse(parts[scoreIndex + 2], out int mateValue))
        {
            mate = mateValue;
            return true;
        }

        return false;
    }

    private async Task EnsureStartedAsync(
        CancellationToken cancellationToken)
    {
        if (_process != null && !_process.HasExited)
            return;

        if (_process != null)
        {
            _process.Dispose();
            _process = null;
        }

        if (!File.Exists(_enginePath))
        {
            throw new FileNotFoundException(
                "Stockfish executable not found.",
                _enginePath);
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _enginePath,
            WorkingDirectory = Path.GetDirectoryName(_enginePath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        lock (_stderrLock)
            _recentStandardError = string.Empty;

        _process = new Process
        {
            StartInfo = startInfo
        };

        _process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;

            lock (_stderrLock)
            {
                _recentStandardError += args.Data + Environment.NewLine;
                if (_recentStandardError.Length > 4000)
                    _recentStandardError = _recentStandardError[^4000..];
            }
        };

        if (!_process.Start())
            throw new InvalidOperationException(
                "Could not start Stockfish.");

        _process.BeginErrorReadLine();

        await SendCommandAsync("uci");

        await WaitForResponseAsync(
            "uciok",
            cancellationToken);
    }

    private async Task SendCommandAsync(string command)
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException(
                GetStoppedProcessMessage("Stockfish is not running."));

        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    private async Task WaitForResponseAsync(
        string expected,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string line = await ReadLineAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (line.Trim() == expected)
                return;
        }
    }

    private async Task<string> ReadLineAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException(
                GetStoppedProcessMessage("Stockfish unexpectedly stopped."));

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(timeout);

        string? line = await _process.StandardOutput.ReadLineAsync(
            timeoutSource.Token);

        if (line == null)
            throw new InvalidOperationException(
                GetStoppedProcessMessage("Stockfish closed its output stream."));

        return line;
    }

    private string GetStoppedProcessMessage(string message)
    {
        string exitCode = "unknown";
        if (_process != null)
        {
            try
            {
                if (_process.HasExited)
                    exitCode = _process.ExitCode.ToString();
            }
            catch
            {
                // The process may have exited between checking and reading its status.
            }
        }

        string standardError;
        lock (_stderrLock)
            standardError = _recentStandardError.Trim();

        return string.IsNullOrWhiteSpace(standardError)
            ? $"{message} Exit code: {exitCode}."
            : $"{message} Exit code: {exitCode}. Stockfish error output: {standardError}";
    }

    public void Dispose()
    {
        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.WriteLine("quit");
                    _process.StandardInput.Flush();

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

        _gate.Dispose();
    }
}
