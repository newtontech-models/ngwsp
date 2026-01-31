using System.Text;
using Spectre.Console;

namespace ngwsp;

public sealed class ConsoleWriter
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly StringBuilder _committedMarkup = new();
    private string _lookaheadMarkup = string.Empty;
    private readonly AutoResetEvent _liveSignal = new(false);
    private readonly CancellationTokenSource _liveCts = new();
    private Task? _liveTask;
    private volatile string _pendingLiveMarkup = string.Empty;
    private bool _liveDisabled;
    private bool _clearedOnce;

    public async Task WriteAsync(
        string committedText,
        string lookaheadText,
        CancellationToken cancellationToken = default)
    {
        committedText ??= string.Empty;
        lookaheadText ??= string.Empty;

        if ((committedText.Length + lookaheadText.Length) == 0)
        {
            return;
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (committedText.Length > 0)
            {
                _committedMarkup.Append(committedText);
            }

            _lookaheadMarkup = BuildLookaheadMarkup(lookaheadText);
            Render();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lookaheadMarkup = string.Empty;
            Render();
        }
        finally
        {
            _sync.Release();
        }

        await StopLiveAsync().ConfigureAwait(false);
    }

    private void Render()
    {
        ClearConsoleOnce();

        var full = _committedMarkup.ToString() + _lookaheadMarkup;
        if (TryRenderLive(full))
        {
            return;
        }

        // Fallback: rewrite in-place if Live cannot start.
        TryHomeAndClearToEnd();
        if (full.Length > 0)
        {
            AnsiConsole.Write(new Markup(full));
        }
    }

    private bool TryRenderLive(string markup)
    {
        if (_liveDisabled)
        {
            return false;
        }

        EnsureLiveStarted();
        if (_liveTask == null)
        {
            return false;
        }

        _pendingLiveMarkup = markup;
        _liveSignal.Set();
        return true;
    }

    private void EnsureLiveStarted()
    {
        if (_liveTask != null || _liveDisabled)
        {
            return;
        }

        ClearConsoleOnce();

        _liveTask = Task.Run(() =>
        {
            try
            {
                AnsiConsole.Live(new Markup(string.Empty)).Start(ctx =>
                {
                    while (!_liveCts.IsCancellationRequested)
                    {
                        _liveSignal.WaitOne(100);
                        if (_liveCts.IsCancellationRequested)
                        {
                            break;
                        }

                        var current = _pendingLiveMarkup;
                        ctx.UpdateTarget(new Markup(current));
                        ctx.Refresh();
                    }

                    var last = _pendingLiveMarkup;
                    if (!string.IsNullOrEmpty(last))
                    {
                        ctx.UpdateTarget(new Markup(last));
                        ctx.Refresh();
                    }
                });
            }
            catch
            {
                _liveDisabled = true;
                _liveTask = null;
            }
        });
    }

    private void ClearConsoleOnce()
    {
        if (_clearedOnce)
        {
            return;
        }

        _clearedOnce = true;

        try
        {
            AnsiConsole.Clear();
            return;
        }
        catch
        {
            // ignored
        }

        try
        {
            Console.Clear();
            return;
        }
        catch
        {
            // ignored
        }

        try
        {
            Console.Write("\x1b[2J\x1b[H");
        }
        catch
        {
            // ignored
        }
    }

    private static void TryHomeAndClearToEnd()
    {
        try
        {
            Console.Write("\x1b[H\x1b[J");
        }
        catch
        {
            // ignored
        }
    }

    private async Task StopLiveAsync()
    {
        if (_liveTask == null)
        {
            return;
        }

        try
        {
            _liveCts.Cancel();
            _liveSignal.Set();
            await _liveTask.ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
        finally
        {
            _liveTask = null;
        }
    }

    private static string BuildLookaheadMarkup(string lookahead)
    {
        if (string.IsNullOrEmpty(lookahead))
        {
            return string.Empty;
        }

        return "[grey50]" + Markup.Escape(lookahead) + "[/]";
    }
}
