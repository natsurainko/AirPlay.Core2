using AirPlay.Core2.Processors.Mirror;

namespace AirPlay.Core2.Models.Messages.Mirror;

/// <summary>
/// Raised synchronously on the mirror worker when the detected codec changes.
/// Assign a matching processor and subscribe to its typed frame event before returning.
/// Leave Processor null when the codec is unsupported. Do not defer selection to a UI callback.
/// </summary>
public sealed class MirrorProcessorRequestedEventArgs(MirrorVideoCodec codec) : EventArgs
{
    public MirrorVideoCodec Codec { get; } = codec;

    public MirrorVideoProcessor? Processor { get; set; }
}
