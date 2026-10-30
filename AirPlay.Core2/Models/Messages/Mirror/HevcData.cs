namespace AirPlay.Core2.Models.Messages.Mirror;

/// <summary>A decrypted HEVC access unit in Annex B format.</summary>
public readonly struct HevcData
{
    public int FrameType { get; init; }

    public byte[] Data { get; init; }

    public int Length { get; init; }

    public long Pts { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}
