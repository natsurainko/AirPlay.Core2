using AirPlay.Core2.Models.Messages.Mirror;
using AirPlay.Core2.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net;
using System.Net.Sockets;

using AesSecret = (byte[] DecryptedAesKey, byte[] AesIv, byte[] EcdhShared);

namespace AirPlay.Core2.Connections.Mirror;

public class MirrorDataConnection(ushort receivePort, string streamConnectionId, AesSecret aesSecret) : IDisposable
{
    private readonly TcpListener _tcpListener = new(IPAddress.Any, receivePort);

    private readonly AESCTRBufferedCipher _cipher = AESCTRBufferedCipher.CreateStream(streamConnectionId, aesSecret.DecryptedAesKey, aesSecret.EcdhShared);
    private readonly CancellationTokenSource _tokenSource = new();

    private readonly byte[] _og = new byte[16];

    private byte[]? _spsPps;
    private int _nextDecryptCount;
    private long _payloadPts;

    public event EventHandler<Size>? FrameSizeChanged;
    public event EventHandler<H264Data>? DataReceived;

    public Size? FrameSize { get; private set; }

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

                            if (FrameSize != null && _spsPps != null)
                                if (TryProcessVideo(output, _spsPps, _payloadPts, FrameSize.Value, out H264Data? h264Data))
                                    DataReceived?.Invoke(this, h264Data.Value);
                        }
                        else if (mirroringHeader.PayloadType == 1)
                        {
                            if (FrameSize?.Height != mirroringHeader.HeightSource || FrameSize?.Width != mirroringHeader.WidthSource)
                            {
                                FrameSize = new Size(mirroringHeader.WidthSource, mirroringHeader.HeightSource);
                                FrameSizeChanged?.Invoke(this, FrameSize.Value);
                            }

                            _payloadPts = mirroringHeader.PayloadPts;
                            TryProcessSpsPps(payloadBuffer.AsSpan(0, mirroringHeader.PayloadSize), out _spsPps);
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

    private static bool TryProcessVideo(byte[] payload, byte[] spsPps, long pts, Size frameSize, [NotNullWhen(true)] out H264Data? h264Data)
    {
        h264Data = null;
        if (payload.Length == 0 || spsPps.Length == 0) return false;

        int offset = 0;
        int frameType = 0;
        bool hasIdr = false;

        while (offset < payload.Length)
        {
            if (payload.Length - offset < 4) return false;

            uint nalLength = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4));
            if (nalLength == 0 || nalLength > (uint)(payload.Length - offset - 4))
                return false;

            int nalType = payload[offset + 4] & 0x1f;
            if (offset == 0 || nalType is >= 1 and <= 5)
                frameType = nalType;
            hasIdr |= nalType == 5;

            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset, 4), 1);
            offset += 4 + (int)nalLength;
        }

        if (hasIdr)
        {
            byte[] payloadOut = new byte[payload.Length + spsPps.Length];

            Array.Copy(spsPps, 0, payloadOut, 0, spsPps.Length);
            Array.Copy(payload, 0, payloadOut, spsPps.Length, payload.Length);

            h264Data = new()
            {
                FrameType = 5,
                Pts = pts,
                Width = frameSize.Width,
                Height = frameSize.Height,
                Data = payloadOut,
                Length = payload.Length + spsPps.Length
            };

            return true;
        }

        h264Data = new()
        {
            FrameType = frameType,
            Pts = pts,
            Width = frameSize.Width,
            Height = frameSize.Height,
            Data = payload,
            Length = payload.Length
        };
        return true;
    }

    private static bool TryProcessSpsPps(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out byte[]? spsPps)
    {
        spsPps = null;

        // AVCDecoderConfigurationRecord; video payloads below use 4-byte NAL lengths.
        if (payload.Length < 7 || payload[0] != 1 || (payload[4] & 0x03) != 3)
            return false;

        int spsCount = payload[5] & 0x1f;
        if (spsCount == 0) return false;

        using var output = new MemoryStream();
        int offset = 6;

        for (int i = 0; i < spsCount; i++)
            if (!TryAppendParameterSet(payload, ref offset, output)) return false;

        if (offset >= payload.Length) return false;
        int ppsCount = payload[offset++];
        if (ppsCount == 0) return false;

        for (int i = 0; i < ppsCount; i++)
            if (!TryAppendParameterSet(payload, ref offset, output)) return false;

        spsPps = output.ToArray();
        return true;
    }

    private static bool TryAppendParameterSet(ReadOnlySpan<byte> payload, ref int offset, MemoryStream output)
    {
        if (payload.Length - offset < 2) return false;

        int length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2));
        offset += 2;

        if (length == 0 || length > payload.Length - offset || output.Length + length + 4 > 102400)
            return false;

        output.Write([0, 0, 0, 1]);
        output.Write(payload.Slice(offset, length));
        offset += length;
        return true;
    }
}
