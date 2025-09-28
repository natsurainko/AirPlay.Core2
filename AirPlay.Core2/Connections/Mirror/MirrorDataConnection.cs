using AirPlay.Core2.Models.Messages.Mirror;
using AirPlay.Core2.Utils;
using System.Buffers;
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
                    byte[] payloadBuffer = new byte[mirroringHeader.PayloadSize];

                    await networkStream.ReadExactlyAsync(payloadBuffer, cancellationToken);

                    if (mirroringHeader.PayloadType == 0)
                    {
                        DecryptVideoData(payloadBuffer, out byte[] output);

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
                        TryProcessSpsPps(payloadBuffer, out _spsPps);
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

    private void DecryptVideoData(byte[] videoData, out byte[] output)
    {
        if (_nextDecryptCount > 0)
        {
            for (int i = 0; i < _nextDecryptCount; i++)
                videoData[i] = (byte)(videoData[i] ^ _og[16 - _nextDecryptCount + i]);
        }

        int encryptlen = (videoData.Length - _nextDecryptCount) / 16 * 16;

        byte[] decrypted = _cipher.ProcessBytes([.. videoData.Skip(_nextDecryptCount).Take(encryptlen)]);
        Array.Copy(decrypted, 0, videoData, _nextDecryptCount, decrypted.Length);

        Array.Copy(videoData, _nextDecryptCount, videoData, _nextDecryptCount, encryptlen);

        int restlen = (videoData.Length - _nextDecryptCount) % 16;
        int reststart = videoData.Length - restlen;
        _nextDecryptCount = 0;

        if (restlen > 0)
        {
            Array.Fill(_og, (byte)0);
            Array.Copy(videoData, reststart, _og, 0, restlen);

            Array.Copy(_cipher.ProcessBytes([.. _og.Take(16)]), _og, 16);

            Array.Copy(_og, 0, videoData, reststart, restlen);
            _nextDecryptCount = 16 - restlen;
        }

        output = new byte[videoData.Length];
        Array.Copy(videoData, 0, output, 0, videoData.Length);

        // Release video data
        videoData = null!;
    }

    private static bool TryProcessVideo(byte[] payload, byte[] spsPps, long pts, Size frameSize, [NotNullWhen(true)] out H264Data? h264Data)
    {
        h264Data = null;
        int naluSize = 0;

        while (naluSize < payload.Length)
        {
            int nc_len = (payload[naluSize + 3] & 0xFF) | ((payload[naluSize + 2] & 0xFF) << 8) | ((payload[naluSize + 1] & 0xFF) << 16) | ((payload[naluSize] & 0xFF) << 24);

            if (nc_len > 0)
            {
                payload[naluSize] = 0;
                payload[naluSize + 1] = 0;
                payload[naluSize + 2] = 0;
                payload[naluSize + 3] = 1;
                naluSize += nc_len + 4;
            }

            if (payload.Length - nc_len > 4) return false;
        }

        if (spsPps.Length == 0) return false;

        int frameType = payload[4] & 0x1f;

        if (frameType == 5)
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

            // Release payload
            payload = null!;
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

    private static bool TryProcessSpsPps(byte[] payload, [NotNullWhen(true)] out byte[]? spsPps)
    {
        spsPps = null;
        H264Codec h264 = new(payload);

        if (h264.LengthOfSps + h264.LengthOfPps > 102400) 
            return false;

        int spsPpsLen = h264.LengthOfSps + h264.LengthOfPps + 8;
        spsPps = new byte[spsPpsLen];

        spsPps[0] = 0;
        spsPps[1] = 0;
        spsPps[2] = 0;
        spsPps[3] = 1;

        Array.Copy(h264.SequenceParameterSet, 0, spsPps, 4, h264.LengthOfSps);

        spsPps[h264.LengthOfSps + 4] = 0;
        spsPps[h264.LengthOfSps + 5] = 0;
        spsPps[h264.LengthOfSps + 6] = 0;
        spsPps[h264.LengthOfSps + 7] = 1;

        Array.Copy(h264.PictureParameterSet, 0, spsPps, h264.LengthOfSps + 8, h264.LengthOfPps);

        return true;
    }
}
