using AirPlay.Core2.Models.Messages.Mirror;
using AirPlay.Core2.Utils;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

using AesSecret = (byte[] DecryptedAesKey, byte[] AesIv, byte[] EcdhShared);

namespace AirPlay.Core2.Connections.Mirror;

public class MirrorDataConnection(ushort receivePort, string streamConnectionId, AesSecret aesSecret) : IDisposable
{
    private readonly TcpListener _tcpListener = new(IPAddress.Any, receivePort);

    private readonly AESCTRBufferedCipher _cipher = AESCTRBufferedCipher.CreateStream(streamConnectionId, aesSecret.DecryptedAesKey, aesSecret.EcdhShared);
    private readonly CancellationTokenSource _tokenSource = new();

    public void BeginDataMessageLoopWorker()
    {
        Task.Run(async () => await DataMessageLoopWorker(_tokenSource.Token), _tokenSource.Token);
    }

    public void EndDataMessageLoopWorker()
    {
        _tcpListener.Server.Close();
        _tokenSource.Cancel();
    }

    private async Task DataMessageLoopWorker(CancellationToken cancellationToken)
    {
        _tcpListener.Start();

        TcpClient? client = null;
        NetworkStream? networkStream = null;

        using var memoryOwner = MemoryPool<byte>.Shared.Rent(128);

        Memory<byte> headerBuffer = memoryOwner.Memory;

        try
        {
            client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
            networkStream = client.GetStream();

            if (!networkStream.CanRead) throw new InvalidOperationException();

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                while (networkStream.DataAvailable)
                {
                    await networkStream.ReadExactlyAsync(headerBuffer, cancellationToken);
                    byte[] headerBytes = headerBuffer.ToArray();

                    if ((headerBytes[0] == 80 && headerBytes[1] == 79 && headerBytes[2] == 83 && headerBytes[3] == 84) ||
                        (headerBytes[0] == 71 && headerBytes[1] == 69 && headerBytes[2] == 84))
                    {
                        // Request is POST or GET (skip)

                        continue;
                    }

                    MirroringHeader mirroringHeader = new(headerBytes);
                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(mirroringHeader.PayloadSize);

                    await networkStream.ReadExactlyAsync(payloadBuffer, cancellationToken);
                }

                await Task.Delay(10, cancellationToken);
            }
        }
        finally
        {
            client?.Close();
            client?.Dispose();

            _tcpListener.Stop();
        }
    }

    public void Dispose()
    {
        _tokenSource.Dispose();
        _tcpListener.Dispose();
        _cipher.Dispose();
    }
}
