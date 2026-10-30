using AirPlay.Core2.Models.Messages.Mirror;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace AirPlay.Core2.Processors.Mirror;

public sealed class H264MirrorVideoProcessor : MirrorVideoProcessor
{
    private byte[]? _spsPps;

    public override MirrorVideoCodec Codec => MirrorVideoCodec.H264;

    public override bool IsSupported => true;

    public event EventHandler<H264Data>? FrameReceived;

    public override bool ProcessConfiguration(ReadOnlySpan<byte> payload)
        => TryProcessSpsPps(payload, out _spsPps);

    public override bool ProcessVideo(byte[] payload, long pts, Size frameSize)
    {
        if (_spsPps == null || !TryProcessVideo(payload, _spsPps, pts, frameSize, out var frame))
            return false;

        FrameReceived?.Invoke(this, frame.Value);
        return true;
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
