namespace AirPlay.Core2.Models.Messages.Audio;

public class RaopBuffer
{
    public const int RAOP_BUFFER_LENGTH = 1024; //512;

    public bool IsEmpty { get; set; }
    public ushort FirstSeqNum { get; set; }
    public ushort LastSeqNum { get; set; }

    public RaopBufferEntry[] Entries { get; } = new RaopBufferEntry[RAOP_BUFFER_LENGTH];

    public int BufferSize { get; set; }
    public byte[] Buffer { get; set; } = [];

    public static RaopBuffer Create()
    {
        int audioBufferSize = 480 * 4;
        int bufferSize = audioBufferSize * RAOP_BUFFER_LENGTH;

        RaopBuffer raopBuffer = new()
        {
            BufferSize = bufferSize,
            Buffer = new byte[bufferSize]
        };

        for (int i = 0; i < RAOP_BUFFER_LENGTH; i++)
        {
            raopBuffer.Entries[i].AudioBufferSize = audioBufferSize;
            raopBuffer.Entries[i].AudioBufferLen = 0;
            raopBuffer.Entries[i].AudioBuffer = [.. raopBuffer.Buffer.Skip(i).Take(audioBufferSize)];
        }

        raopBuffer.IsEmpty = true;
        return raopBuffer;
    }
}
