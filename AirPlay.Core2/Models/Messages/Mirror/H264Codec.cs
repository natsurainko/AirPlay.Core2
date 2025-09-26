namespace AirPlay.Core2.Models.Messages.Mirror;

public readonly struct H264Codec
{
    public byte Compatibility { get; }

    public short LengthOfPps { get; }

    public short LengthOfSps { get; }

    public byte Level { get; }

    public short NumberOfPps { get; }

    public byte[] PictureParameterSet { get; }

    public byte ProfileHigh { get; }

    public byte Reserved3AndSps { get; }

    public byte Reserved6AndNal { get; }

    public byte[] SequenceParameterSet { get; }

    public byte Version { get; }

    public H264Codec(byte[] payload)
    {
        Version = payload[0];
        ProfileHigh = payload[1];
        Compatibility = payload[2];
        Level = payload[3];
        Reserved6AndNal = payload[4];
        Reserved3AndSps = payload[5];
        LengthOfSps = (short)(((payload[6] & 255) << 8) + (payload[7] & 255));

        var sequence = new byte[LengthOfSps];
        Array.Copy(payload, 8, sequence, 0, LengthOfSps);
        SequenceParameterSet = sequence;
        NumberOfPps = payload[LengthOfSps + 8];
        LengthOfPps = (short)(((payload[LengthOfSps + 9] & 2040) + payload[LengthOfSps + 10]) & 255);

        var picture = new byte[LengthOfPps];
        Array.Copy(payload, LengthOfSps + 11, picture, 0, LengthOfPps);
        PictureParameterSet = picture;
    }
}
