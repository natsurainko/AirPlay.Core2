using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Configs;
using AirPlay.Core2.Models.Messages.Rtsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebex.Security.Cryptography;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using static AirPlay.Core2.Models.Messages.Rtsp.RtspRequestMessage;

namespace AirPlay.Core2.Connections;

public partial class RtspConnection : IDisposable
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<RtspConnection>? _logger;
    private readonly AirTunesConfig _airTunesConfig;

    private readonly TcpClient _client;
    private readonly IPEndPoint _endPoint;

    private readonly Ed25519 _ed25519;
    private readonly byte[] _publicKey;

    private Curve25519? _curve25519;
    private byte[]? _ecdhOurs;
    private byte[]? _ecdhTheirs;
    private byte[]? _edTheirs;
    private byte[]? _ecdhShared;

    private bool _pairVerified;
    private byte[]? _keyMsg;

    private string? _ActiveRemote;
    private string? _DACPID;

    private DeviceSession? _deviceSession;
    private bool _disconnectRequested = false;

    public RtspConnection(TcpClient client, IOptions<AirTunesConfig> options, ILoggerFactory? loggerFactory = null)
    {
        _client = client;
        _endPoint = client.Client.RemoteEndPoint as IPEndPoint
            ?? throw new ArgumentException("TcpClient must be connected to a remote endpoint");

        _airTunesConfig = options.Value;

        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<RtspConnection>();

        _ed25519 = (Ed25519.Create("ed25519-sha512") as Ed25519)!;
        _ed25519.FromSeed([.. Enumerable.Range(0, 32).Select(r => (byte)r)]);

        _publicKey = _ed25519.GetPublicKey();
    }

    public event EventHandler? ConnectionClosed;
    public event EventHandler<DeviceSession>? SessionPaired;

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
        ConnectionClosed += (_,_) => _logger?.EndMessageLoopWorker(_endPoint);

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
                // Why AirTunes client dont't close the connection after handshake failed?
                if (lastRequestTime != null && DateTime.Now.Subtract(lastRequestTime.Value).TotalSeconds > 10)
                {
                    _logger?.ConnectionIdle(_endPoint);
                    break;
                }

                await Task.Delay(10, cancellationToken);
                continue;
            }

            foreach (var requestMessage in RtspRequestMessage.ParseRequestsFromHex(rawHexData))
                await HandleRequestMessageAsync(requestMessage, cancellationToken)
                    .ContinueWith(async t => await HandleResponseMessageAsync(requestMessage, t.Result, networkStream, cancellationToken), TaskContinuationOptions.OnlyOnRanToCompletion);

            lastRequestTime = DateTime.Now;

            if (_disconnectRequested || (_deviceSession?.RequestedDisconnecet ?? false)) 
                await _client.Client.DisconnectAsync(false, cancellationToken);
        }
    }

    private async Task<RtspResponseMessage> HandleRequestMessageAsync(RtspRequestMessage requestMessage, CancellationToken cancellationToken)
    {
        var responseMessage = requestMessage.CreateResponse();

        _ActiveRemote = requestMessage.Headers["Active-Remote"][0];
        _DACPID = requestMessage.Headers["DACP-ID"][0];

        _logger?.RtspRequestMessageReceived(_ActiveRemote, requestMessage.Type, requestMessage.Path);

        if (requestMessage.Type == RequestType.GET && "/info".Equals(requestMessage.Path, StringComparison.OrdinalIgnoreCase))
            await OnGetInfoRequested(responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.POST && "/pair-setup".Equals(requestMessage.Path, StringComparison.OrdinalIgnoreCase))
            await OnPostPairSetupRequested(responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.POST && "/pair-verify".Equals(requestMessage.Path, StringComparison.OrdinalIgnoreCase))
            await OnPostPairVerifyRequested(requestMessage, responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.POST && "/fp-setup".Equals(requestMessage.Path, StringComparison.OrdinalIgnoreCase))
            await OnPostFpSetupRequested(requestMessage, responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.SETUP)
            await OnSetupRequested(requestMessage, responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.SETUP)
            await OnSetupRequested(requestMessage, responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.GET_PARAMETER)
            await OnGetParameterRequested(requestMessage, responseMessage, cancellationToken);
        else if (requestMessage.Type == RequestType.RECORD)
            await OnRecordRequested(); // The sender wants to start streaming.
        else if (requestMessage.Type == RequestType.POST && "/feedback".Equals(requestMessage.Path, StringComparison.OrdinalIgnoreCase))
            await OnPostFeedbackRequested(); // Probably an heartbeat to ensure the ensure the receiver is alive. Sent until the receiver is disconnected.
        else if (requestMessage.Type == RequestType.FLUSH)
            await OnFlushRequested(requestMessage);
        else if (requestMessage.Type == RequestType.TEARDOWN)
            await OnTeardownRequested(requestMessage);
        else if (requestMessage.Type == RequestType.SET_PARAMETER)
            await OnSetParameterRequested(requestMessage);

        return responseMessage;
    }

    private async Task HandleResponseMessageAsync(RtspRequestMessage requestMessage, RtspResponseMessage responseMessage, NetworkStream networkStream, CancellationToken cancellationToken)
    {
        byte[] bodyBuffer = await responseMessage.ReadToEndAsync();
        responseMessage.Headers["Content-Length"] = [bodyBuffer.Length.ToString()];

        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine($"RTSP/1.0 {(int)responseMessage.Status} {responseMessage.Status}");

        foreach (var header in responseMessage.Headers)
            stringBuilder.AppendLine($"{header.Name}: {string.Join(",", header)}");

        stringBuilder.AppendLine();

        byte[] headerBuffer = Encoding.ASCII.GetBytes(stringBuilder.ToString());
        byte[] payloadBuffer = [.. headerBuffer, .. bodyBuffer];

        try
        {
            await networkStream.WriteAsync(payloadBuffer, cancellationToken);
            await networkStream.FlushAsync(cancellationToken);
        }
        catch (IOException) 
        {
            _logger?.SendResponseMessageError(_ActiveRemote!, requestMessage.Type, requestMessage.Path);
        }
        finally
        {
            responseMessage.Dispose();
        }
    }

    public void Dispose() => _client.Dispose();
}

internal static partial class RtspConnectionLoggers
{
    [LoggerMessage(LogLevel.Information, "Running message loop worker for client [{endPoint}]")]
    public static partial void RunningMessageLoopWorker(this ILogger logger, EndPoint? endPoint);

    [LoggerMessage(LogLevel.Information, "End message loop worker and close client [{endPoint}]")]
    public static partial void EndMessageLoopWorker(this ILogger logger, EndPoint? endPoint);

    [LoggerMessage(LogLevel.Warning, "The connection [{endPoint}] is idle ")]
    public static partial void ConnectionIdle(this ILogger logger, EndPoint? endPoint);

    [LoggerMessage(LogLevel.Information, "RtspRequestMessage from [{activeRemote}] Received: [{requestType}] \"{requestPath}\"")]
    public static partial void RtspRequestMessageReceived(this ILogger logger, string activeRemote, RtspRequestMessage.RequestType requestType, string requestPath);

    [LoggerMessage(LogLevel.Warning, "Failed to send responseMessage to RtspRequestMessage from [{activeRemote}] Received: [{requestType}] \"{requestPath}\"")]
    public static partial void SendResponseMessageError(this ILogger logger, string activeRemote, RtspRequestMessage.RequestType requestType, string requestPath);
}