using System.Runtime.InteropServices;

namespace AirPlay.Core2.Models.Messages.Mirror;

public readonly struct MirroringHeader
{
    public int PayloadSize { get; }
    public short PayloadType { get; }
    public short PayloadOption { get; }
    public long PayloadNtp { get; }
    public long PayloadPts { get; }
    public int WidthSource { get; }
    public int HeightSource { get; }
    public int Width { get; }
    public int Height { get; }

    public MirroringHeader(ReadOnlySpan<byte> header)
    {
        var reader = new SpanReader(header);
        PayloadSize = (int)reader.ReadNext<uint>(sizeof(uint));
        PayloadType = (short)(reader.ReadNext<ushort>(sizeof(ushort)) & 0xff);
        PayloadOption = (short)reader.ReadNext<ushort>(sizeof(ushort));

        if (PayloadType == 0)
        {
            PayloadNtp = (long)reader.ReadNext<ulong>(sizeof(ulong));
            PayloadPts = NtpToPts(PayloadNtp);
        }
        else if (PayloadType == 1)
        {
            reader.Position = 40;
            WidthSource = (int)reader.ReadNext<float>(sizeof(float));
            HeightSource = (int)reader.ReadNext<float>(sizeof(float));

            reader.Position = 56;
            Width = (int)reader.ReadNext<float>(sizeof(float));
            Height = (int)reader.ReadNext<float>(sizeof(float));
        }
    }

    private static long NtpToPts(long ntp) => (((ntp >> 32) & 0xffffffff) * 1000000) + ((ntp & 0xffffffff) * 1000 * 1000 / int.MaxValue);

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> data;

        public int Position { get; set; }

        public SpanReader(ReadOnlySpan<byte> data) { this.data = data; }

        public T ReadNext<T>(int size) where T : unmanaged
        {
            var result = MemoryMarshal.Read<T>(data.Slice(Position, size));
            Position += size;
            return result;
        }
    }
}