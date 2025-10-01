using AirPlay.Core2.Controllers;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

using SyncData = (ulong SyncTime, ulong SyncTimestamp);
using ResendRequest = (ushort MissingSeqNum, ushort Count, ulong Timestamp);

namespace AirPlay.Core2.Connections.Audio;

public class AudioControlConnection : IDisposable
{
    private const ulong OFFSET_1900_TO_1970 = 2208988800UL;

    private readonly Socket _udpListener = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    //private readonly ushort _sendPort;
    private ushort _controlSeqNum = 0;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _tokenSource = new();

    public event EventHandler<SyncData>? SyncDataReceived;
    public event EventHandler<byte[]>? ResentDataReceived;
    public AudioControlConnection(ushort receivePort) => _udpListener.Bind(new IPEndPoint(IPAddress.Any, receivePort));

    public void BeginControlMessageLoopWorker()
    {
        Task.Run(async () => await ControlMessageLoopWorker(_tokenSource.Token), _tokenSource.Token);
    }

    public void EndControlMessageLoopWorker()
    {
        _tokenSource.Cancel();
        _udpListener.Close();
    }

    public void HandleResendPacket(ResendRequest resendRequest)
    {
        if (!_udpListener.Connected) return;

        lock (_lock)
        {
            _controlSeqNum++;

            byte[] packet =
            [
                0x80,                          // RTP Version + Marker (Marker=1)
                0x55 | 0x80,                   // Payload type 85 + Marker bit
                (byte)(_controlSeqNum >> 8),
                (byte)_controlSeqNum,
                (byte)(resendRequest.Timestamp >> 24),
                (byte)(resendRequest.Timestamp >> 16),
                (byte)(resendRequest.Timestamp >> 8),
                (byte)(resendRequest.Timestamp),
                (byte)(resendRequest.MissingSeqNum >> 8),
                (byte)resendRequest.MissingSeqNum,
                (byte)(resendRequest.Count >> 8),
                (byte)resendRequest.Count,
            ];

            _udpListener.Send(packet, 0, packet.Length, SocketFlags.None);
        }
    }

    private async Task ControlMessageLoopWorker(CancellationToken cancellationToken)
    {
        byte[] packet = ArrayPool<byte>.Shared.Rent(AudioController.RAOP_PACKET_LENGTH);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int udpReceiveResult = await _udpListener.ReceiveAsync(packet, SocketFlags.None, cancellationToken);

                using var memoryStream = new MemoryStream(packet);
                using var reader = new BinaryReader(memoryStream);

                memoryStream.Position = 1;
                int type = reader.ReadByte() & ~0x80;

                if (type == 0x56)
                {
                    memoryStream.Position = 4;
                    byte[] data = reader.ReadBytes(udpReceiveResult - 4);

                    ResentDataReceived?.Invoke(this, packet);
                }
                else if (type == 0x54)
                {
                    /* packetlen = 20
                     * bytes	description
                        8	RTP header without SSRC
                        8	current NTP time
                        4	RTP timestamp for the next audio packet
                     */

                    memoryStream.Position = 8;
                    ulong ntp_time = (((ulong)reader.ReadInt32()) * 1000000UL) + ((((ulong)reader.ReadInt32()) * 1000000UL) / int.MaxValue);
                    uint rtp_timestamp = (uint)((packet[4] << 24) | (packet[5] << 16) | (packet[6] << 8) | packet[7]);
                    uint next_timestamp = (uint)((packet[16] << 24) | (packet[17] << 16) | (packet[18] << 8) | packet[19]);

                    SyncDataReceived?.Invoke(this, (ntp_time - OFFSET_1900_TO_1970 * 1000000UL, rtp_timestamp));
                }
                else
                {
                    //Console.WriteLine("Unknown packet");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packet);
        }
    }

    public void Dispose()
    {
        _udpListener.Dispose();
        _tokenSource.Dispose();
    }
}
