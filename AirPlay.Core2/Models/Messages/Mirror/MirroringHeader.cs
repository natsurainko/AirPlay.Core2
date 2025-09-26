namespace AirPlay.Core2.Models.Messages.Mirror;

public readonly struct MirroringHeader
{
    public int PayloadSize { get; }

    public ushort PayloadType { get; }

    public ushort PayloadOption { get; }

    public ulong PayloadNtp { get; }

    public ulong PayloadPts { get; }

    public uint WidthSource { get; }

    public uint HeightSource { get; }

    public uint Width { get; }

    public uint Height { get; }

    public MirroringHeader(byte[] header)
    {
        using var memoryStream = new MemoryStream(header);
        using var reader = new BinaryReader(memoryStream);

        PayloadSize = (int)reader.ReadUInt32();
        PayloadType = (ushort)(reader.ReadUInt16() & 0xff);
        PayloadOption = reader.ReadUInt16();

        if (PayloadType == 0)
        {
            PayloadNtp = reader.ReadUInt64();
            PayloadPts = NtpToPts(PayloadNtp);
        }
        else if (PayloadType == 1)
        {
            memoryStream.Position = 40;
            WidthSource = (uint)reader.ReadSingle();
            HeightSource = (uint)reader.ReadSingle();

            memoryStream.Position = 56;
            Width = (uint)reader.ReadSingle();
            Height = (uint)reader.ReadSingle();
        }
    }

    private static ulong NtpToPts(ulong ntp) => (((ntp >> 32) & 0xffffffff) * 1000000) + ((ntp & 0xffffffff) * 1000 * 1000 / int.MaxValue);
}