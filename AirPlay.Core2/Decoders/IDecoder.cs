using AirPlay.Core2.Models.Messages.Audio;

namespace AirPlay.Core2.Decoders;

public interface IDecoder
{
    AudioFormat Type { get; }

    int GetOutputStreamLength();

    int Config(int sampleRate, int channels, int bitDepth, int frameLength);

    int DecodeFrame(byte[] input, ref byte[] output);
}
