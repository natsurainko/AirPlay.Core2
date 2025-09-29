using AirPlay.Core2.Controllers;
using AirPlay.Core2.Decoders;
using AirPlay.Core2.Models.Messages.Audio;
using System.Security.Cryptography;

namespace AirPlay.Core2.Extensions;

internal static class RaopBufferExtensions
{
    extension(RaopBuffer raopBuffer)
    {
        public void Flush(int nextSeq)
        {
            lock (raopBuffer)
            {
                for (int i = 0; i < RaopBuffer.RAOP_BUFFER_LENGTH; i++)
                {
                    raopBuffer.Entries[i].Available = false;
                    raopBuffer.Entries[i].AudioBufferLen = 0;
                }

                raopBuffer.IsEmpty = true;

                if (nextSeq > 0 && nextSeq < 0xffff)
                {
                    raopBuffer.FirstSeqNum = (ushort)nextSeq;
                    raopBuffer.LastSeqNum = (ushort)(nextSeq - 1);
                }
            }
        }

        public int Queue(ICryptoTransform decryptor, IDecoder decoder, byte[] data, ushort dataLength)
        {
            lock (raopBuffer)
            {
                RaopBufferEntry entry;

                /* Check packet data length is valid */
                if (dataLength < 12 || dataLength > AudioController.RAOP_PACKET_LENGTH) return -1;

                var seqnum = (ushort)((data[2] << 8) | data[3]);
                if (dataLength == 16 && data[12] == 0x0 && data[13] == 0x68 && data[14] == 0x34 && data[15] == 0x0) return 0;
                if (!raopBuffer.IsEmpty && seqnum < raopBuffer.FirstSeqNum && seqnum != 0) return 0; // Ignore, old

                /* Check that there is always space in the buffer, otherwise flush */
                if (raopBuffer.FirstSeqNum + RaopBuffer.RAOP_BUFFER_LENGTH < seqnum || seqnum == 0)
                    raopBuffer.Flush(seqnum);

                entry = raopBuffer.Entries[seqnum % RaopBuffer.RAOP_BUFFER_LENGTH];
                if (entry.Available && entry.SeqNum == seqnum) return 0; // Packet resent, we can safely ignore

                entry.Flags = data[0];
                entry.Type = data[1];
                entry.SeqNum = seqnum;

                entry.TimeStamp = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
                entry.SSrc = (uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]);
                entry.Available = true;

                int payloadsize = dataLength - 12;
                int encryptedlen = payloadsize / 16 * 16;

                byte[] raw = new byte[payloadsize];

                if (encryptedlen > 0)
                {
                    byte[] decryptedData = decryptor.TransformFinalBlock(data, 12, encryptedlen);
                    Array.Copy(decryptedData, 0, data, 12, decryptedData.Length);

                    Array.Copy(data, 12, raw, 0, encryptedlen);
                }

                Array.Copy(data, 12 + encryptedlen, raw, encryptedlen, payloadsize - encryptedlen);

                /* RAW -> PCM */
                int length = decoder.GetOutputStreamLength();
                byte[] output = new byte[length];

                if (decoder.DecodeFrame(raw, ref output, length) != 0)
                    output = new byte[length];

                //Array.Copy(output, 0, entry.AudioBuffer, 0, output.Length);
                entry.AudioBuffer = output;
                entry.AudioBufferLen = output.Length;

                /* Update the raop_buffer seqnums */
                if (raopBuffer.IsEmpty)
                {
                    raopBuffer.FirstSeqNum = seqnum;
                    raopBuffer.LastSeqNum = seqnum;
                    raopBuffer.IsEmpty = false;
                }

                if (raopBuffer.LastSeqNum < seqnum)
                    raopBuffer.LastSeqNum = seqnum;

                // Update entries
                raopBuffer.Entries[seqnum % RaopBuffer.RAOP_BUFFER_LENGTH] = entry;

                return 1;
            }
        }

        public RaopBufferEntry? Dequeue(ref uint pts, bool noResend)
        {
            lock (raopBuffer)
            {
                short buflen;
                RaopBufferEntry entry;

                /* Calculate number of entries in the current buffer */
                buflen = (short)(raopBuffer.LastSeqNum - raopBuffer.FirstSeqNum + 1);

                /* Cannot dequeue from empty buffer */
                if (raopBuffer.IsEmpty || buflen <= 0) return null;

                /* Get the first buffer entry for inspection */
                entry = raopBuffer.Entries[raopBuffer.FirstSeqNum % RaopBuffer.RAOP_BUFFER_LENGTH];

                try
                {
                    if (noResend)
                    {
                        return entry;
                    }
                    else if (!entry.Available)
                    {
                        /* Check how much we have space left in the buffer */
                        //if (buflen < RaopBuffer.RAOP_BUFFER_LENGTH)
                        //{
                        //    /* Return nothing and hope resend gets on time */
                        //    return null;
                        //}
                        return null;
                    }

                    return entry;
                }
                finally
                {
                    /* If we do no resends, always return the first entry */
                    entry.Available = false;

                    /* Return entry audio buffer */
                    pts = entry.TimeStamp;
                    entry.AudioBufferLen = 0;

                    raopBuffer.Entries[raopBuffer.FirstSeqNum % RaopBuffer.RAOP_BUFFER_LENGTH] = entry;
                    raopBuffer.FirstSeqNum += 1;
                }
            }
        }
    }
}
