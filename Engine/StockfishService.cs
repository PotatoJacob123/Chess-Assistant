
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChessAssistant.Engine;

public sealed class StockfishService : IDisposable
{
    private readonly string _enginePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;

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
            await EnsureStartedAsync(cancellationToken);

            await SendCommandAsync("ucinewgame");
            await SendCommandAsync("isready");
            await WaitForResponseAsync("readyok", cancellationToken);

            await SendCommandAsync($"position fen {fen}");
            await SendCommandAsync($"go depth {depth}");

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

                    return parts[1];
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(
        CancellationToken cancellationToken)
    {
        if (_process != null && !_process.HasExited)
            return;

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
            CreateNoWindow = true
        };

        _process = new Process
        {
            StartInfo = startInfo
        };

        if (!_process.Start())
            throw new InvalidOperationException(
                "Could not start Stockfish.");

        await SendCommandAsync("uci");

        await WaitForResponseAsync(
            "uciok",
            cancellationToken);
    }

    private async Task SendCommandAsync(string command)
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException(
                "Stockfish is not running.");

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
                "Stockfish unexpectedly stopped.");

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(timeout);

        string? line = await _process.StandardOutput.ReadLineAsync(
            timeoutSource.Token);

        if (line == null)
            throw new InvalidOperationException(
                "Stockfish closed its output stream.");

        return line;
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
