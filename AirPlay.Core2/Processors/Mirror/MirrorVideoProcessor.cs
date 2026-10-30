using AirPlay.Core2.Models.Messages.Mirror;
using System.Buffers.Binary;
using System.Drawing;

namespace AirPlay.Core2.Processors.Mirror;

/// <summary>
/// Processes codec configuration and decrypted video payloads.
/// Transport, decryption, timestamps, and display size remain owned by the connection.
/// </summary>
public abstract class MirrorVideoProcessor
{
    public abstract MirrorVideoCodec Codec { get; }

    public abstract bool IsSupported { get; }

    /// <summary>
    /// Identifies the configuration formats used by the current AirPlay mirror path.
    /// This is format identification, not full configuration validation.
    /// </summary>
    public static MirrorVideoCodec DetectCodec(ReadOnlySpan<byte> configuration)
    {
        // AirPlay HEVC configuration uses an hvc1 sample entry (not bare AVC configuration).
        if (configuration.Length >= 8 &&
            (configuration.Slice(4, 4).SequenceEqual("hvc1"u8) ||
             configuration.Slice(4, 4).SequenceEqual("hev1"u8)))
            return MirrorVideoCodec.Hevc;

        // Identify AVC configuration by its version and first SPS, rather than version alone:
        // a raw HEVCDecoderConfigurationRecord can also start with version 1.
        if (configuration.Length >= 9 && configuration[0] == 1 && (configuration[5] & 0x1f) > 0)
        {
            int spsLength = BinaryPrimitives.ReadUInt16BigEndian(configuration.Slice(6, 2));
            if (spsLength > 0 && spsLength <= configuration.Length - 8 &&
                (configuration[8] & 0x1f) == 7)
                return MirrorVideoCodec.H264;
        }

        // Empty or unrecognized configuration is not sufficient evidence of HEVC.
        return MirrorVideoCodec.Unknown;
    }

    public abstract bool ProcessConfiguration(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Processes an owned payload, which may be modified in place.
    /// Returns true when a frame has been produced.
    /// </summary>
    public abstract bool ProcessVideo(byte[] payload, long pts, Size frameSize);
}
