using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Trnscrbr.Services;

public sealed class SingleInstanceService : IDisposable
{
    public const string MutexName = "Local\\Trnscrbr.App";
    private const string PipeName = "Trnscrbr.App.Pipe";
    private const string ShowCommand = "show";

    private readonly Action _onShowRequested;
    private CancellationTokenSource? _cancellation;
    private Task? _serverTask;

    public SingleInstanceService(Action onShowRequested)
    {
        _onShowRequested = onShowRequested;
    }

    public void Start()
    {
        _cancellation = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerAsync(_cancellation.Token));
    }

    public static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(250);
            var bytes = Encoding.UTF8.GetBytes(ShowCommand);
            client.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            // Best effort only. If the running instance is busy, the second instance can exit silently.
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var command = await reader.ReadToEndAsync(cancellationToken);
                if (string.Equals(command, ShowCommand, StringComparison.OrdinalIgnoreCase))
                {
                    _onShowRequested();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }
}
