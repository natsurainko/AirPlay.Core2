using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace AirPlay.Core2.Connections;

public partial class ModifiedHttpConnection : IDisposable
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<ModifiedHttpConnection>? _logger;
    private readonly AirPlayConfig _airPlayConfig;

    private readonly TcpClient _client;
    private readonly IPEndPoint _endPoint;

    public ModifiedHttpConnection(TcpClient client, IOptions<AirPlayConfig> options, ILoggerFactory? loggerFactory = null)
    {
        _client = client;
        _endPoint = client.Client.RemoteEndPoint as IPEndPoint
            ?? throw new ArgumentException("TcpClient must be connected to a remote endpoint");

        _airPlayConfig = options.Value;

        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<ModifiedHttpConnection>();

        //_ed25519 = (Ed25519.Create("ed25519-sha512") as Ed25519)!;
        //_ed25519.FromSeed([.. Enumerable.Range(0, 32).Select(r => (byte)r)]);

        //_publicKey = _ed25519.GetPublicKey();
        //_privateKey = _ed25519.GetPrivateKey();
    }

    public event EventHandler? ConnectionClosed;

    public void BeginMessageLoopWorker(CancellationToken cancellationToken)
    {
        Task.Run(async () => await MessageLoopWorker(cancellationToken), cancellationToken).ContinueWith(t =>
        {
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }, cancellationToken);
    }

    private async Task MessageLoopWorker(CancellationToken cancellationToken)
    {
        _logger?.RunningMessageLoopWorker(_endPoint);
        ConnectionClosed += (_, _) => _logger?.EndMessageLoopWorker(_endPoint);

        DateTime? lastRequestTime = null;

        using var memoryOwner = MemoryPool<byte>.Shared.Rent(1024);
        using var networkStream = _client.GetStream();

        if (!_client.Connected) throw new InvalidOperationException("TcpClient is not connected");
        if (!networkStream.CanRead) throw new InvalidOperationException("Can't read the NetworkStream");

        while (_client.Connected)
        {
            long readTotleBytesLength = 0;
            string rawHexData = string.Empty;

            while (networkStream.DataAvailable)
            {
                try
                {
                    Memory<byte> buffer = memoryOwner.Memory;
                    int readCount = await networkStream.ReadAsync(buffer, cancellationToken);

                    readTotleBytesLength += readCount;
                    rawHexData += string.Join(string.Empty, buffer[..readCount].ToArray().Select(b => b.ToString("X2")));

                    // Wait for other possible data
                    await Task.Delay(10, cancellationToken);
                }
                catch (IOException) { }
            }

            if (string.IsNullOrEmpty(rawHexData))
            {
                // Same as AirTunes
                if (lastRequestTime != null && DateTime.Now.Subtract(lastRequestTime.Value).TotalSeconds > 10)
                {
                    _logger?.ConnectionIdle(_endPoint);
                    break;
                }

                await Task.Delay(10, cancellationToken);
                continue;
            }

            foreach (var requestMessage in HttpRequestMessage.ParseRequestsFromHex(rawHexData))
                await HandleRequestMessageAsync(requestMessage, cancellationToken)
                    .ContinueWith(async t => await HandleResponseMessageAsync(requestMessage, t.Result, networkStream, cancellationToken), TaskContinuationOptions.OnlyOnRanToCompletion);

            //if (_disconnectRequested) await _client.Client.DisconnectAsync(false, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> HandleRequestMessageAsync(HttpRequestMessage requestMessage, CancellationToken cancellationToken)
    {
        var responseMessage = new HttpResponseMessage();

        _logger?.HttpRequestMessageReceived(_endPoint, requestMessage.Method, requestMessage.RequestUri);

        return responseMessage;
    }

    private async Task HandleResponseMessageAsync(HttpRequestMessage requestMessage, HttpResponseMessage responseMessage, NetworkStream networkStream, CancellationToken cancellationToken)
    {
        byte[] bodyBuffer = await responseMessage.Content.ReadAsByteArrayAsync(cancellationToken);
        //responseMessage.Headers["Content-Length"] = [bodyBuffer.Length.ToString()];

        try
        {
            await networkStream.WriteAsync(bodyBuffer, cancellationToken);
            await networkStream.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            _logger?.SendResponseMessageError(_endPoint, requestMessage.Method, requestMessage.RequestUri);
        }
        finally
        {
            requestMessage.Dispose();
            responseMessage.Dispose();
        }
    }

    public void Dispose() => _client?.Dispose();
}

internal static partial class ModifiedHttpConnectionLoggers
{
    [LoggerMessage(LogLevel.Information, "HttpRequestMessage from [{iPEndPoint}] Received: [{method}] \"{requestPath}\"")]
    public static partial void HttpRequestMessageReceived(this ILogger logger, IPEndPoint iPEndPoint, HttpMethod method, Uri? requestPath);

    [LoggerMessage(LogLevel.Warning, "Failed to send responseMessage to HttpRequestMessage from [{iPEndPoint}] Received: [{requestType}] \"{requestPath}\"")]
    public static partial void SendResponseMessageError(this ILogger logger, IPEndPoint iPEndPoint, HttpMethod requestType, Uri? requestPath);
}