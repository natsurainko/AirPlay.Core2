using AirPlay.Core2.Models.Messages.Mirror;
using AirPlay.Core2.Processors.Mirror;
using AirPlay.Core2.Utils;
using System.Buffers;
using System.Drawing;
using System.Net;
using System.Net.Sockets;

using AesSecret = (byte[] DecryptedAesKey, byte[] AesIv, byte[] EcdhShared);

namespace AirPlay.Core2.Connections.Mirror;

public class MirrorDataConnection : IDisposable
{
    private readonly TcpListener _tcpListener;
    private readonly AESCTRBufferedCipher _cipher;
    private bool _processorSelectionRequested;
    private readonly CancellationTokenSource _tokenSource = new();

    private readonly byte[] _og = new byte[16];

    private int _nextDecryptCount;
    private long _payloadPts;

    public MirrorDataConnection(ushort receivePort, string streamConnectionId, AesSecret aesSecret)
    {
        _tcpListener = new TcpListener(IPAddress.Any, receivePort);
        _cipher = AESCTRBufferedCipher.CreateStream(streamConnectionId, aesSecret.DecryptedAesKey, aesSecret.EcdhShared);
    }

    public event EventHandler<Size>? FrameSizeChanged;

    /// <summary>
    /// Subscribe before starting the worker. Select a processor synchronously when the codec changes.
    /// </summary>
    public event EventHandler<MirrorProcessorRequestedEventArgs>? ProcessorRequested;

    public Size? FrameSize { get; private set; }

    public MirrorVideoCodec VideoCodec { get; private set; } = MirrorVideoCodec.Unknown;

    public MirrorVideoProcessor? VideoProcessor { get; private set; }

    public bool IsVideoConfigured { get; private set; }

    public void BeginDataMessageLoopWorker()
    {
        Task.Run(async () => await DataMessageLoopWorker(_tokenSource.Token), _tokenSource.Token);
    }

    public void EndDataMessageLoopWorker()
    {
        _tcpListener.Server.Close();
        _tokenSource.Cancel();
    }

    private async Task DataMessageLoopWorker(CancellationToken cancellationToken)
    {
        _tcpListener.Start();
        using var headerBuffer = MemoryPool<byte>.Shared.Rent(128);

        try
        {
            using var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
            using var networkStream = client.GetStream();

            if (!networkStream.CanRead) throw new InvalidOperationException();

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                while (networkStream.DataAvailable)
                {
                    await networkStream.ReadExactlyAsync(headerBuffer.Memory[..128], cancellationToken);

                    if (headerBuffer.Memory.Span[..4] == "POST"u8 || headerBuffer.Memory.Span[..3] == "GET"u8)
                    {
                        // Request is POST or GET (skip)

                        continue;
                    }

                    MirroringHeader mirroringHeader = new(headerBuffer.Memory.Span[..128]);
                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(mirroringHeader.PayloadSize);

                    try
                    {
                        await networkStream.ReadExactlyAsync(payloadBuffer, 0, mirroringHeader.PayloadSize, cancellationToken);

                        if (mirroringHeader.PayloadType == 0)
                        {
                            DecryptVideoData(payloadBuffer, mirroringHeader.PayloadSize, out byte[] output);

                            if (FrameSize != null && IsVideoConfigured)
                                VideoProcessor?.ProcessVideo(output, _payloadPts, FrameSize.Value);
                        }
                        else if (mirroringHeader.PayloadType == 1)
                        {
                            if (FrameSize?.Height != mirroringHeader.HeightSource || FrameSize?.Width != mirroringHeader.WidthSource)
                            {
                                FrameSize = new Size(mirroringHeader.WidthSource, mirroringHeader.HeightSource);
                                FrameSizeChanged?.Invoke(this, FrameSize.Value);
                            }

                            _payloadPts = mirroringHeader.PayloadPts;
                            ProcessVideoConfiguration(payloadBuffer.AsSpan(0, mirroringHeader.PayloadSize));
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payloadBuffer);
                    }
                }
            }
        }
        finally
        {
            _tcpListener.Stop();
        }
    }

    private void ProcessVideoConfiguration(ReadOnlySpan<byte> payload)
    {
        var codec = MirrorVideoProcessor.DetectCodec(payload);
        IsVideoConfigured = false;

        if (!_processorSelectionRequested || codec != VideoCodec)
        {
            VideoCodec = codec;
            VideoProcessor = null;
            _processorSelectionRequested = true;

            var request = new MirrorProcessorRequestedEventArgs(codec);
            ProcessorRequested?.Invoke(this, request);

            if (request.Processor is { } processor)
            {
                if (processor.Codec != codec)
                    throw new InvalidOperationException($"Processor codec {processor.Codec} does not match detected codec {codec}.");
                if (!processor.IsSupported)
                    throw new NotSupportedException($"The selected {codec} mirror processor is not implemented.");

                VideoProcessor = processor;
            }
        }

        // No selected processor means the caller explicitly does not support this codec.
        // Repeated configuration for the same codec reuses the selected processor/decoder.
        if (VideoProcessor != null)
            IsVideoConfigured = VideoProcessor.ProcessConfiguration(payload);
    }

    public void Dispose()
    {
        _tokenSource.Dispose();
        _tcpListener.Dispose();
        _cipher.Dispose();
    }

    private void DecryptVideoData(byte[] input, int length, out byte[] output)
    {
        int consumed = Math.Min(length, _nextDecryptCount);
        int keyStreamOffset = 16 - _nextDecryptCount;

        for (int i = 0; i < consumed; i++)
            input[i] ^= _og[keyStreamOffset + i];

        _nextDecryptCount -= consumed;

        if (consumed == length)
        {
            output = input.AsSpan(0, length).ToArray();
            return;
        }

        int encryptlen = (length - consumed) / 16 * 16;

        if (encryptlen > 0)
        {
            byte[] decrypted = _cipher.ProcessBytes(input.AsSpan(consumed, encryptlen).ToArray());
            Array.Copy(decrypted, 0, input, consumed, decrypted.Length);
        }

        int restlen = (length - consumed) % 16;
        int reststart = length - restlen;

        if (restlen > 0)
        {
            Array.Fill(_og, (byte)0);
            Array.Copy(input, reststart, _og, 0, restlen);

            Array.Copy(_cipher.ProcessBytes([.. _og.Take(16)]), _og, 16);

            Array.Copy(_og, 0, input, reststart, restlen);
            _nextDecryptCount = 16 - restlen;
        }

        output = new byte[length];
        Array.Copy(input, 0, output, 0, length);
    }
}
