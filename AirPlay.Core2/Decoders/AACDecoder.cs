using AirPlay.Core2.Models.Messages.Audio;
using System.Runtime.InteropServices;

namespace AirPlay.Core2.Decoders;

public class AACDecoder : IDecoder, IDisposable
{
    private IntPtr _decoder = IntPtr.Zero;
    private int _channels;
    private int _sampleRate;
    private int _bitDepth;
    private int _frameLength;
    private AudioFormat _type;
    private byte[]? _asc;

    public AudioFormat Type => _type;

    public int Config(int sampleRate, int channels, int bitDepth, int frameLength)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bitDepth = bitDepth;
        _frameLength = frameLength;
        
        if (frameLength == 1024)
        {
            _type = AudioFormat.AAC;
            _asc = BuildAsc(AudioObjectType.AAC_MAIN, sampleRate, channels);
        }
        else if (frameLength == 480)
        {
            _type = AudioFormat.AAC_ELD;
            _asc = BuildAsc(AudioObjectType.AAC_ELD, sampleRate, channels);
        }
        else
        {
            throw new NotSupportedException("Unsupported frameLength, cannot determine AAC type.");
        }

        if (_decoder != IntPtr.Zero)
            Dispose();

        _decoder = FdkAacInterop.aacDecoder_Open(2, 1); // TT_MP4_RAW, 1 layer
        if (_decoder == IntPtr.Zero)
            throw new Exception("Failed to open AAC decoder");

        IntPtr ascPtr = Marshal.AllocHGlobal(_asc.Length);
        Marshal.Copy(_asc, 0, ascPtr, _asc.Length);

        IntPtr[] conf = [ascPtr];
        uint[] lengths = [(uint)_asc.Length];

        int err = FdkAacInterop.aacDecoder_ConfigRaw(_decoder, conf, lengths);
        Marshal.FreeHGlobal(ascPtr);

        if (err != 0)
        {
            Dispose();
            throw new Exception($"aacDecoder_ConfigRaw failed: {err}");
        }

        return 0;
    }

    public int DecodeFrame(byte[] input, ref byte[] output)
    {
        if (_decoder == IntPtr.Zero)
            throw new InvalidOperationException("Decoder is not configured.");

        // 1. Fill
        IntPtr inputPtr = Marshal.AllocHGlobal(input.Length);
        Marshal.Copy(input, 0, inputPtr, input.Length);
        IntPtr[] buffer = new IntPtr[] { inputPtr };
        uint[] sizes = new uint[] { (uint)input.Length };
        uint[] valid = new uint[] { (uint)input.Length };

        int fillErr = FdkAacInterop.aacDecoder_Fill(_decoder, buffer, sizes, valid);
        Marshal.FreeHGlobal(inputPtr);

        if (fillErr != 0)
            return -1; // Fill失败

        // 2. PCM输出准备
        int samplesPerFrame = _frameLength; // 1024 (AAC-Main) / 480 (ELD)
        int channels = _channels;
        int pcmSamples = samplesPerFrame * channels;
        short[] pcmBuffer = new short[pcmSamples];

        // 3. Decode
        GCHandle handle = GCHandle.Alloc(pcmBuffer, GCHandleType.Pinned);
        int err;
        try
        {
            IntPtr pcmPtr = handle.AddrOfPinnedObject();
            err = FdkAacInterop.aacDecoder_DecodeFrame(_decoder, pcmPtr, pcmSamples, 0);
        }
        finally
        {
            handle.Free();
        }

        if (err != 0)
        {
            // 解码失败，可能是 AAC_DEC_NOT_ENOUGH_BITS（0x1002），可特殊处理
            return -2;
        }

        // 4. 输出到byte[]
        int pcmBytes = pcmSamples * 2; // 16bit
        if (output == null || output.Length < pcmBytes)
            output = new byte[pcmBytes];
        Buffer.BlockCopy(pcmBuffer, 0, output, 0, pcmBytes);

        return 0;
    }

    //public int DecodeFrame(byte[] input, ref byte[] output)
    //{
    //    int length = input.Length;

    //    if (_decoder == IntPtr.Zero)
    //        throw new InvalidOperationException("Decoder is not configured.");

    //    // Fill
    //    IntPtr inputPtr = Marshal.AllocHGlobal(length);
    //    Marshal.Copy(input, 0, inputPtr, length);
    //    IntPtr[] buffer = [inputPtr];
    //    uint[] sizes = [(uint)length];
    //    uint[] valid = [(uint)length];

    //    int err = FdkAacInterop.aacDecoder_Fill(_decoder, buffer, sizes, valid);
    //    Marshal.FreeHGlobal(inputPtr);

    //    if (err != 0)
    //        return -1;

    //    int pcmSamples = GetOutputStreamLength();
    //    int[] pcmBuffer = new int[pcmSamples];

    //    GCHandle handle = GCHandle.Alloc(pcmBuffer, GCHandleType.Pinned);
    //    try
    //    {
    //        IntPtr pcmPtr = handle.AddrOfPinnedObject();
    //        err = FdkAacInterop.aacDecoder_DecodeFrame(_decoder, pcmPtr, pcmSamples, 0);

    //        if (err != 0)
    //            return -2;

    //        if (output == null || output.Length < pcmSamples * 2)
    //            output = new byte[pcmSamples * 2];
    //        Buffer.BlockCopy(pcmBuffer, 0, output, 0, pcmSamples * 2);
    //        return 0;
    //    }
    //    finally
    //    {
    //        handle.Free();
    //    }
    //}

    //private List<byte> _aacBuffer = new List<byte>();

    //public int DecodeFrame(byte[] input, ref byte[] output)
    //{
    //    _aacBuffer.AddRange(input);

    //    int decodedBytes = 0;
    //    while (true)
    //    {
    //        // 只要buffer里有数据就尝试解码
    //        if (_aacBuffer.Count == 0) break;

    //        int toDecode = _aacBuffer.Count;
    //        IntPtr inputPtr = Marshal.AllocHGlobal(toDecode);
    //        Marshal.Copy(_aacBuffer.ToArray(), 0, inputPtr, toDecode);

    //        IntPtr[] buffer = new IntPtr[] { inputPtr };
    //        uint[] sizes = new uint[] { (uint)toDecode };
    //        uint[] valid = new uint[] { (uint)toDecode };
    //        FdkAacInterop.aacDecoder_Fill(_decoder, buffer, sizes, valid);
    //        Marshal.FreeHGlobal(inputPtr);

    //        int pcmSamples = _frameLength * _channels;
    //        short[] pcmBuffer = new short[pcmSamples];
    //        GCHandle handle = GCHandle.Alloc(pcmBuffer, GCHandleType.Pinned);
    //        int err = FdkAacInterop.aacDecoder_DecodeFrame(_decoder, handle.AddrOfPinnedObject(), pcmSamples, 0);
    //        handle.Free();

    //        if (err == 4098)
    //        {
    //            // 等待更多数据
    //            break;
    //        }
    //        else if (err != 0)
    //        {
    //            // 其它错误
    //            break;
    //        }
    //        else
    //        {
    //            if (output == null || output.Length < pcmSamples * 2)
    //                output = new byte[pcmSamples * 2];
    //            Buffer.BlockCopy(pcmBuffer, 0, output, 0, pcmSamples * 2);

    //            // 移除已消耗字节
    //            int consumed = (int)(toDecode - valid[0]);
    //            _aacBuffer.RemoveRange(0, consumed);
    //            decodedBytes = pcmSamples * 2;
    //            break;
    //        }
    //    }
    //    return decodedBytes;
    //}

    public void Dispose()
    {
        if (_decoder != IntPtr.Zero)
        {
            FdkAacInterop.aacDecoder_Close(_decoder);
            _decoder = IntPtr.Zero;
        }
    }

    public int GetOutputStreamLength() => _frameLength * _channels * _bitDepth / 8;

    private static byte[] BuildAsc(AudioObjectType aot, int sampleRate, int channels)
    {
        int srIndex = GetSampleRateIndex(sampleRate);
        if (aot == AudioObjectType.AAC_MAIN)
        {
            // 2字节 ASC
            return
            [
                (byte)(((int)aot << 3) | (srIndex >> 1)),
                (byte)(((srIndex & 1) << 7) | (channels << 3))
            ];
        }
        else if (aot == AudioObjectType.AAC_ELD)
        {
            // 4字节 ASC for AAC-ELD, 见 fdk-aac ffmpeg实现

            return [0xF8, 0xE8, 0x50, 0x00];

            //return
            //[
            //    0x1C, // 00011100 (AOT=39: AAC-ELD)
            //    (byte)((srIndex << 3) | (channels >> 1)),
            //    (byte)(((channels & 1) << 7) | 0x04), // frameLengthFlag=0, dependsOnCoreCoder=0, extensionFlag=1
            //    0x60  // 01100000 (eld_specific_config)
            //];
        }
        else
        {
            throw new NotSupportedException("Unsupported AudioObjectType");
        }
    }

    private static int GetSampleRateIndex(int sampleRate)
    {
        int[] rates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
        for (int i = 0; i < rates.Length; i++)
            if (rates[i] == sampleRate)
                return i;
        throw new ArgumentException($"Unsupported sample rate: {sampleRate}");
    }

    private enum AudioObjectType
    {
        AAC_MAIN = 1,
        AAC_LC = 2,
        AAC_SSR = 3,
        AAC_LTP = 4,
        SBR = 5,
        AAC_SCALABLE = 6,
        TWINVQ = 7,
        CELP = 8,
        HVXC = 9,
        RESERVED = 10,
        ER_AAC_LC = 17,
        ER_AAC_LTP = 19,
        ER_AAC_SCALABLE = 20,
        ER_TWINVQ = 21,
        ER_BSAC = 22,
        ER_AAC_LD = 23,
        ER_CELP = 24,
        ER_HVXC = 25,
        ER_HILN = 26,
        ER_PARAM_AAC = 27,
        SSC = 28,
        PS = 29,
        MPEG_SURROUND = 30,
        ESCAPE = 31,
        LAYER_1 = 32,
        LAYER_2 = 33,
        LAYER_3 = 34,
        DST = 35,
        ALS = 36,
        SLS = 37,
        SLS_NON_CORE = 38,
        AAC_ELD = 39,
        USAC = 42
    }
}

public static class FdkAacInterop
{
    public const int AAC_DEC_OK = 0;

    [DllImport("libfdk-aac.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr aacDecoder_Open(int transportType, int nrOfLayers);

    [DllImport("libfdk-aac.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int aacDecoder_ConfigRaw(
        IntPtr decoder,
        IntPtr[] conf,
        uint[] length
    );

    [DllImport("libfdk-aac.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int aacDecoder_Fill(IntPtr decoder, IntPtr[] buffer, uint[] bufferSize, uint[] bytesValid);

    [DllImport("libfdk-aac.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int aacDecoder_DecodeFrame(
        IntPtr decoder,
        IntPtr pcmOut,
        int timeDataSize,
        uint flags
    );

    [DllImport("libfdk-aac.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void aacDecoder_Close(IntPtr decoder);
}