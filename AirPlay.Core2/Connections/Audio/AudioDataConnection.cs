using AirPlay.Core2.Controllers;
using AirPlay.Core2.Decoders;
using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models.Messages.Audio;
using AirPlay.Core2.Utils;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

using AesSecret = (byte[] DecryptedAesKey, byte[] AesIv, byte[] EcdhShared);
using ResendRequest = (ushort MissingSeqNum, ushort Count, ulong Timestamp);
using SyncData = (ulong SyncTime, ulong SyncTimestamp);

namespace AirPlay.Core2.Connections.Audio;

public class AudioDataConnection : IDisposable
{
    private readonly ICryptoTransform _decryptor;
    private readonly IDecoder _decoder;
    private readonly Socket _udpListener = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    private readonly CancellationTokenSource _tokenSource = new();
    private readonly RaopBuffer _raopBuffer = RaopBuffer.Create();
    private readonly Lock _resentLock = new();

    private bool _resentBeforeDequeue;
    private SyncData _syncData;
    private TaskCompletionSource? _handingResentBuffer;

    public event EventHandler<PcmAudioData>? DataReceived;
    public event EventHandler<ResendRequest>? ResendRequested;

    public AudioDataConnection(ushort receivePort, AudioFormat audioFormat, AesSecret aesSecret)
    {
        _udpListener.Bind(new IPEndPoint(IPAddress.Any, receivePort));

        if (audioFormat == AudioFormat.ALAC)
        {
            // RTP info: 96 AppleLossless, 96 352 0 16 40 10 14 2 255 0 0 44100
            // (ALAC -> PCM)

            _decoder = new ALACDecoder();
            _decoder.Config(sampleRate: 44100, channels: 2, bitDepth: 16, frameLength: 352);
        }
        else if (audioFormat == AudioFormat.AAC)
        {
            // RTP info: 96 mpeg4-generic/44100/2, 96 mode=AAC-main; constantDuration=1024
            // (AAC-MAIN -> PCM)

            _decoder = new AACDecoder();
            _decoder.Config(sampleRate: 44100, channels: 2, bitDepth: 16, frameLength: 1024);
        }
        else if (audioFormat == AudioFormat.AAC_ELD)
        {
            // RTP info: 96 mpeg4-generic/44100/2, 96 mode=AAC-eld; constantDuration=480
            // (AAC-ELD -> PCM)

            _decoder = new AACDecoder();
            _decoder.Config(sampleRate: 44100, channels: 2, bitDepth: 16, frameLength: 480);
        }
        else
        {
            // (PCM -> PCM)
            _decoder = new PCMDecoder();
        }

        using var aes = Aes.Create();


        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;

        aes.Key = AESUtils.HashAndTruncate(aesSecret.DecryptedAesKey, aesSecret.EcdhShared);

        _decryptor = aes.CreateDecryptor();
    }

    public void BeginDataMessageLoopWorker() => Task.Run(async () => await DataMessageLoopWorker(_tokenSource.Token), _tokenSource.Token);

    public void EndDataMessageLoopWorker()
    {
        _udpListener.Close();
        _tokenSource.Cancel();
    }

    public void HandleResendBuffer(byte[] buffer)
    {
        lock (_resentLock)
        {
            _handingResentBuffer = new TaskCompletionSource();

            if (_raopBuffer.Queue(_decryptor, _decoder, buffer, (ushort)buffer.Length) == 1)
                _resentBeforeDequeue = true;

            _handingResentBuffer.SetResult();
        }
    }

    public void HandleSyncData(SyncData syncData) => _syncData = syncData;

    public void Flush(int nextSeq) => _raopBuffer.Flush(nextSeq);

    private async Task DataMessageLoopWorker(CancellationToken cancellationToken)
    {
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(AudioController.RAOP_PACKET_LENGTH);
        Memory<byte> buffer = memoryOwner.Memory;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_handingResentBuffer != null)
                {
                    await _handingResentBuffer.Task;
                    _handingResentBuffer = null;
                }

                int udpReceiveResult = await _udpListener.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                if (udpReceiveResult < 12) continue;

                RaopBufferEntry? audiobuf;
                uint timestamp = 0;

                _ = _raopBuffer.Queue(_decryptor, _decoder, buffer.ToArray(), (ushort)udpReceiveResult);

                while ((audiobuf = _raopBuffer.Dequeue(ref timestamp, _resentBeforeDequeue)) != null)
                {
                    var pcmData = new PcmAudioData
                    {
                        Length = audiobuf.Value.AudioBufferLen,
                        Data = [.. audiobuf.Value.AudioBuffer.Take(audiobuf.Value.AudioBufferLen)],
                        Pts = ((timestamp - _syncData.SyncTimestamp) * 1000000UL / 44100) + _syncData.SyncTime
                    };

                    DataReceived?.Invoke(this, pcmData);

                    _resentBeforeDequeue = false;
                }

                CheckAndRequestResend();

                //await Task.Delay(10, cancellationToken);
            }
            catch (Exception ex)
            {

            }
        }
    }

    public void Dispose()
    {
        _tokenSource.Dispose();
        _udpListener.Dispose();

        _decryptor.Dispose();

        if (_decoder is AACDecoder aacDecoder)
            aacDecoder.Dispose();
    }

    private void CheckAndRequestResend()
    {
        ushort seqnum;

        for (seqnum = _raopBuffer.FirstSeqNum; SeqNumCmp(seqnum, _raopBuffer.LastSeqNum) < 0; seqnum++)
        {
            var entry = _raopBuffer.Entries[seqnum % RaopBuffer.RAOP_BUFFER_LENGTH];
            if (entry.Available)
                break;
        }

        if (SeqNumCmp(seqnum, _raopBuffer.FirstSeqNum) == 0)
            return;

        int count = seqnum - _raopBuffer.FirstSeqNum;
        ulong timestamp = _raopBuffer.Entries[_raopBuffer.FirstSeqNum % RaopBuffer.RAOP_BUFFER_LENGTH].TimeStamp;

        ResendRequested?.Invoke(this, (_raopBuffer.FirstSeqNum, (ushort)count, timestamp));
    }

    private static short SeqNumCmp(ushort s1, ushort s2) => (short)(s1 - s2);
}
