namespace AirPlay.Core2.Extensions;

internal static class BinaryReaderExtensions
{
    extension(BinaryReader reader)
    {
        public ushort ReadUInt16BE() => BitConverter.ToUInt16(reader.ReadBytesRequired(sizeof(ushort)).Reverse(), 0);

        public short ReadInt16BE() => BitConverter.ToInt16(reader.ReadBytesRequired(sizeof(short)).Reverse(), 0);

        public uint ReadUInt32BE() => BitConverter.ToUInt32(reader.ReadBytesRequired(sizeof(uint)).Reverse(), 0);

        public int ReadInt32BE() => BitConverter.ToInt32(reader.ReadBytesRequired(sizeof(int)).Reverse(), 0);

        public ulong ReadUInt64BE() => BitConverter.ToUInt64(reader.ReadBytesRequired(sizeof(ulong)).Reverse(), 0);

        public long ReadInt64BE() => BitConverter.ToInt64(reader.ReadBytesRequired(sizeof(long)).Reverse(), 0);

        public byte[] ReadBytesRequired(int byteCount)
        {
            byte[] result = reader.ReadBytes(byteCount);

            if (result.Length != byteCount)
                throw new EndOfStreamException(string.Format("{0} bytes required from stream, but only {1} returned.", byteCount, result.Length));

            return result;
        }
    }
}
