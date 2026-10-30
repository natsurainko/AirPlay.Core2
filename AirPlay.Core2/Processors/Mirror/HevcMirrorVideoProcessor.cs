using AirPlay.Core2.Models.Messages.Mirror;
using System.Buffers.Binary;
using System.Drawing;

namespace AirPlay.Core2.Processors.Mirror;

/// <summary>
/// Experimental AirPlay HEVC processing: hvc1/hev1 configuration to VPS/SPS/PPS,
/// followed by length-prefixed access units to Annex B.
/// </summary>
public sealed class HevcMirrorVideoProcessor : MirrorVideoProcessor
{
    private const int MaxParameterSetBytes = 102400;
    private byte[]? _parameterSets;
    private int _nalLengthSize;
    private bool _needsParameterSets;

    public override MirrorVideoCodec Codec => MirrorVideoCodec.Hevc;

    public override bool IsSupported => true;

    public event EventHandler<HevcData>? FrameReceived;

    public override bool ProcessConfiguration(ReadOnlySpan<byte> payload)
    {
        // Never keep old parameter sets after an invalid/replaced configuration.
        _parameterSets = null;
        _nalLengthSize = 0;
        _needsParameterSets = false;

        if (!TryGetConfigurationRecord(payload, out var configuration) ||
            !TryReadParameterSets(configuration, out var parameterSets, out int nalLengthSize))
            return false;

        _parameterSets = parameterSets;
        _nalLengthSize = nalLengthSize;
        _needsParameterSets = true;
        return true;
    }

    public override bool ProcessVideo(byte[] payload, long pts, Size frameSize)
    {
        if (_parameterSets == null || payload.Length == 0) return false;

        int offset = 0;
        int frameType = -1;
        bool hasVcl = false;
        bool hasIrap = false;
        long annexBLength = 0;

        // Validate the entire access unit before modifying any length prefixes.
        while (offset < payload.Length)
        {
            if (payload.Length - offset < _nalLengthSize) return false;
            uint length = ReadNalLength(payload.AsSpan(offset, _nalLengthSize));
            offset += _nalLengthSize;
            if (length < 2 || length > (uint)(payload.Length - offset)) return false;

            var nal = payload.AsSpan(offset, (int)length);
            if (!IsValidNalHeader(nal)) return false;
            int nalType = (nal[0] >> 1) & 0x3f;
            if (frameType == -1 || (!hasVcl && nalType <= 31)) frameType = nalType;
            hasVcl |= nalType <= 31;
            // BLA, IDR and CRA pictures all need the current VPS/SPS/PPS.
            hasIrap |= nalType is >= 16 and <= 23;
            annexBLength += 4L + length;
            offset += (int)length;
        }

        // Deliver new configuration even if the first access unit contains only SEI.
        bool prependParameterSets = _needsParameterSets || hasIrap;
        int prefixLength = prependParameterSets ? _parameterSets.Length : 0;
        if (annexBLength + prefixLength > int.MaxValue) return false;

        byte[] output = _nalLengthSize == 4 && !prependParameterSets
            ? payload
            : new byte[(int)annexBLength + prefixLength];
        if (prependParameterSets) _parameterSets.CopyTo(output, 0);

        offset = 0;
        int outputOffset = prefixLength;
        while (offset < payload.Length)
        {
            int length = (int)ReadNalLength(payload.AsSpan(offset, _nalLengthSize));
            offset += _nalLengthSize;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(outputOffset, 4), 1);
            payload.AsSpan(offset, length).CopyTo(output.AsSpan(outputOffset + 4, length));
            offset += length;
            outputOffset += 4 + length;
        }

        _needsParameterSets = false;
        FrameReceived?.Invoke(this, new HevcData
        {
            FrameType = frameType,
            Data = output,
            Length = output.Length,
            Pts = pts,
            Width = frameSize.Width,
            Height = frameSize.Height
        });
        return true;
    }

    private static uint ReadNalLength(ReadOnlySpan<byte> bytes)
    {
        uint length = 0;
        foreach (byte value in bytes) length = (length << 8) | value;
        return length;
    }

    private static bool IsValidNalHeader(ReadOnlySpan<byte> nal)
        => nal.Length >= 2 && (nal[0] & 0x80) == 0 && (nal[1] & 0x07) != 0;

    private static bool TryGetConfigurationRecord(ReadOnlySpan<byte> payload, out ReadOnlySpan<byte> configuration)
    {
        configuration = default;
        // A normal VisualSampleEntry has an 8-byte box header and 78 fixed bytes.
        // Its hvcC child contains a 23-byte HEVCDecoderConfigurationRecord header,
        // so the common AirPlay parameter arrays start at 86 + 8 + 23 = 0x75.
        if (payload.Length < 86 || DetectCodec(payload) != MirrorVideoCodec.Hevc) return false;
        uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(payload[..4]);
        if (declaredSize != 0 && (declaredSize < 86 || declaredSize > (uint)payload.Length)) return false;
        int end = declaredSize == 0 ? payload.Length : (int)declaredSize;

        int offset = 86;
        while (offset < end)
        {
            if (end - offset < 8) return false;
            uint size = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, 4));
            if (size == 0) size = (uint)(end - offset);
            // Extended-size boxes are not used by this AirPlay sample-entry layout.
            if (size < 8 || size > (uint)(end - offset)) return false;

            if (payload.Slice(offset + 4, 4).SequenceEqual("hvcC"u8))
            {
                configuration = payload.Slice(offset + 8, (int)size - 8);
                return true;
            }
            offset += (int)size;
        }
        return false;
    }

    private static bool TryReadParameterSets(ReadOnlySpan<byte> configuration, out byte[]? parameterSets, out int nalLengthSize)
    {
        parameterSets = null;
        nalLengthSize = 0;
        if (configuration.Length < 23 || configuration[0] != 1) return false;

        using var vps = new MemoryStream();
        using var sps = new MemoryStream();
        using var pps = new MemoryStream();
        int totalLength = 0;
        int offset = 23;
        int arrayCount = configuration[22];
        for (int i = 0; i < arrayCount; i++)
        {
            if (configuration.Length - offset < 3) return false;
            int nalType = configuration[offset++] & 0x3f;
            int count = BinaryPrimitives.ReadUInt16BigEndian(configuration.Slice(offset, 2));
            offset += 2;
            for (int j = 0; j < count; j++)
            {
                if (configuration.Length - offset < 2) return false;
                int length = BinaryPrimitives.ReadUInt16BigEndian(configuration.Slice(offset, 2));
                offset += 2;
                if (length < 2 || length > configuration.Length - offset) return false;
                var nal = configuration.Slice(offset, length);
                if (!IsValidNalHeader(nal) || ((nal[0] >> 1) & 0x3f) != nalType) return false;

                MemoryStream? destination = nalType switch
                {
                    32 => vps,
                    33 => sps,
                    34 => pps,
                    _ => null
                };
                if (destination != null)
                {
                    totalLength += 4 + length;
                    if (totalLength > MaxParameterSetBytes) return false;
                    destination.Write([0, 0, 0, 1]);
                    destination.Write(nal);
                }
                offset += length;
            }
        }

        if (offset != configuration.Length || vps.Length == 0 || sps.Length == 0 || pps.Length == 0)
            return false;

        using var output = new MemoryStream(totalLength);
        vps.WriteTo(output);
        sps.WriteTo(output);
        pps.WriteTo(output);
        parameterSets = output.ToArray();
        nalLengthSize = (configuration[21] & 0x03) + 1;
        return true;
    }
}
