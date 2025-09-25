using System.Security.Cryptography;

namespace AirPlay.Core2.Extensions;

internal static class ByteArrayExtensions
{
    public static byte[] CopyOfRange(byte[] src, int start, int end)
    {
        int len = end - start;
        byte[] dest = new byte[len];
        Array.Copy(src, start, dest, 0, len);
        return dest;
    }

    public static byte[] CombineAndHash(byte[] first, byte[] last)
    {
        byte[] combined = [.. first, .. last];
        byte[] hashed = SHA512.HashData(combined);

        return hashed;
    }

    public static void Swap(byte[] arr, int idxA, int idxB)
    {
        using var mem = new MemoryStream(arr);
        using var reader = new BinaryReader(mem);
        using var writer = new BinaryWriter(mem);

        mem.Position = idxA;
        int a = reader.ReadInt32();

        mem.Position = idxB;
        int b = reader.ReadInt32();

        mem.Position = idxB;
        writer.Write(a);

        mem.Position = idxA;
        writer.Write(b);
    }

    public static byte[] Reverse(this byte[] b)
    {
        Array.Reverse(b);
        return b;
    }
}
