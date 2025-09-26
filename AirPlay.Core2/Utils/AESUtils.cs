using AirPlay.Core2.Extensions;

namespace AirPlay.Core2.Utils;

internal static class AESUtils
{
    public const string PAIR_VERIFY_AES_KEY = "Pair-Verify-AES-Key";
    public const string PAIR_VERIFY_AES_IV = "Pair-Verify-AES-IV";

    public const string AIR_PLAY_STREAM_KEY = "AirPlayStreamKey";
    public const string AIR_PLAY_STREAM_IV = "AirPlayStreamIV";

    public static byte[] HashAndTruncate(byte[] prefix, byte[] shared, int length = 16)
    {
        byte[] hash = ByteArrayExtensions.CombineAndHash(prefix, shared);
        byte[] result = new byte[length];

        Buffer.BlockCopy(hash, 0, result, 0, length);
        return result;
    }
}
