using System.Security.Cryptography;
using System.Text;

namespace AirPlay.Core2.Utils;

internal class AESCTRBufferedCipher : IDisposable
{
    private readonly byte[] counter;
    private readonly int blockSize;

    private readonly Aes aes;
    private readonly ICryptoTransform encryptor;

    public AESCTRBufferedCipher(byte[] key, byte[] iv)
    {
        blockSize = 16;
        counter = new byte[blockSize];
        Buffer.BlockCopy(iv, 0, counter, 0, blockSize);

        aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;

        encryptor = aes.CreateEncryptor();
    }

    public byte[] ProcessBytes(byte[] data)
    {
        var encrypted = new byte[data.Length];

        for (int i = 0; i < data.Length; i += blockSize)
        {
            byte[] encryptedCounter = new byte[blockSize];
            encryptor.TransformBlock(counter, 0, blockSize, encryptedCounter, 0);

            int remain = Math.Min(blockSize, data.Length - i);

            for (int j = 0; j < remain; j++)
                encrypted[i + j] = (byte)(data[i + j] ^ encryptedCounter[j]);

            for (int j = blockSize - 1; j >= 0; j--)
            {
                if (++counter[j] != 0)
                    break;
            }
        }

        return encrypted;
    }

    public byte[] DoFinal(byte[] lastBlock) => ProcessBytes(lastBlock);

    public void Dispose()
    {
        aes.Dispose();
        encryptor.Dispose();
    }

    public static AESCTRBufferedCipher CreateDefault(byte[] ecdhShared)
    {
        byte[] aesKey = AESUtils.HashAndTruncate(Encoding.UTF8.GetBytes(AESUtils.PAIR_VERIFY_AES_KEY), ecdhShared);
        byte[] aesIv = AESUtils.HashAndTruncate(Encoding.UTF8.GetBytes(AESUtils.PAIR_VERIFY_AES_IV), ecdhShared);

        return new(aesKey, aesIv);
    }

    public static AESCTRBufferedCipher CreateStream(string streamConnectionId, byte[] decryptedAesKey, byte[] ecdhShared)
    {
        byte[] eaesKey = AESUtils.HashAndTruncate(decryptedAesKey, ecdhShared);

        byte[] aesKey = AESUtils.HashAndTruncate(Encoding.UTF8.GetBytes(AESUtils.AIR_PLAY_STREAM_KEY + streamConnectionId), eaesKey);
        byte[] aesIv = AESUtils.HashAndTruncate(Encoding.UTF8.GetBytes(AESUtils.AIR_PLAY_STREAM_IV + streamConnectionId), eaesKey);

        return new(aesKey, aesIv);
    }
}
