namespace AirPlay.Core2.Models.Messages.Mirror;

public readonly struct H264Data
{
    public int FrameType { get; init; }

    public byte[] Data { get; init; }

    public int Length { get; init; }

    public long Pts { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}
