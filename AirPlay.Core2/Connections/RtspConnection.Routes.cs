﻿using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages.Audio;
using AirPlay.Core2.Models.Messages.Rtsp;
using AirPlay.Core2.Utils;
using Claunia.PropertyList;
using Microsoft.Extensions.Logging;
using Rebex.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static AirPlay.Core2.Models.Messages.Rtsp.RtspResponseMessage;

namespace AirPlay.Core2.Connections;

public partial class RtspConnection
{
    private async Task OnGetInfoRequested(RtspResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        Dictionary<string, object> infoDictionary = new()
        {
            { "features", 130367356919 },
            { "name", "airserver" },
            {
                "displays",
                new List<Dictionary<string, object>>
                {
                    new()
                    {
                        { "primaryInputDevice", 1 },
                        { "rotation", false },
                        { "width", 1920 },
                        { "height", 1080 },
                        { "widthPhysical", false },
                        { "heightPhysical", false },
                        { "widthPixels", 1920.0 },
                        { "heightPixels", 1080.0 },
                        { "refreshRate", 1 / (float)Constants.MAX_FPS },
                        { "maxFPS", Constants.MAX_FPS },
                        { "features", 14 },
                        { "overscanned", false },
                        { "uuid", "061013ae-7b0f-4305-984b-974f677a150b" },
                    }
                }
            },
            {
                "audioFormats",
                new List<Dictionary<string, object>>
                {
                    {
                        new Dictionary<string, object>
                        {
                            { "type", 100 },
                            { "audioInputFormats", 67108860 },
                            { "audioOutputFormats", 67108860 }
                        }
                    },
                    {
                        new Dictionary<string, object>
                        {
                            { "type", 101 },
                            { "audioInputFormats", 67108860 },
                            { "audioOutputFormats", 67108860 }
                        }
                    }
                }
            },
            { "vv", 2 },
            { "statusFlags", 4 },
            { "keepAliveLowPower", true },
            { "sourceVersion", Constants.AIPLAY_SERVICE_VERSION },
            { "pk", "29fbb183a58b466e05b9ab667b3c429d18a6b785637333d3f0f3a34baa89f45c" },
            { "keepAliveSendStatsAsBody", true },
            { "deviceID", _airTunesConfig.MacAddress },
            { "model", Constants.DEVICE_MODEL },
            {
                "audioLatencies",
                new List<Dictionary<string, object>>
                {
                    {
                        new Dictionary<string, object>
                        {
                            { "outputLatencyMicros", false },
                            { "type", 100 },
                            { "audioType", "default" },
                            { "inputLatencyMicros", false }
                        }
                    },
                    {
                        new Dictionary<string, object>
                        {
                            { "outputLatencyMicros", false },
                            { "type", 101 },
                            { "audioType", "default" },
                            { "inputLatencyMicros", false }
                        }
                    }
                }
            },
            { "macAddress", _airTunesConfig.MacAddress }
        };

        var binaryPlist = NSObject.Wrap(infoDictionary);
        var plistBytes = BinaryPropertyListWriter.WriteToArray(binaryPlist);

        responseMessage.Headers.Add("Content-Type", "application/x-apple-binary-plist");
        await responseMessage.WriteAsync(plistBytes, 0, plistBytes.Length, cancellationToken);
    }

    private async Task OnPostPairSetupRequested(RtspResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        // Return our 32 bytes public key
        responseMessage.Headers.Add("Content-Type", "application/octet-stream");
        await responseMessage.WriteAsync(_publicKey, 0, _publicKey.Length, cancellationToken);
    }

    private async Task OnPostPairVerifyRequested(RtspRequestMessage requestMessage, RtspResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream(requestMessage.Body);
        using var reader = new BinaryReader(memoryStream);

        // Request: 68 bytes (the first 4 bytes are 01 00 00 00)
        // Client request packet remaining 64 bytes of content
        // 01 00 00 00 -> use 01 as flag to check type of verify
        // If flag is 1:
        // 32 bytes ecdh_their 
        // 32 bytes ed_their 
        // If flag is 0:
        // 64 bytes signature

        byte flag = reader.ReadByte();
        reader.ReadBytes(3);

        if (flag == 0)
        {
            byte[] signature = reader.ReadBytes(64);

            using AESCTRBufferedCipher cipher = AESCTRBufferedCipher.CreateDefault(_ecdhShared!);

            byte[] signatureBuffer = new byte[64];
            signatureBuffer = cipher.ProcessBytes(signatureBuffer);
            signatureBuffer = cipher.DoFinal(signature);

            byte[] messageBuffer = new byte[64];
            Array.Copy(_ecdhTheirs!, 0, messageBuffer, 0, 32);
            Array.Copy(_ecdhOurs!, 0, messageBuffer, 32, 32);

            var ed25519 = (Ed25519.Create("ed25519-sha512") as Ed25519)!;
            ed25519.FromPublicKey(_edTheirs!);

            _pairVerified = ed25519.VerifyMessage(messageBuffer, signatureBuffer);

            if (_pairVerified)
                _logger?.PairVerified(_ActiveRemote!);
            else _logger?.PairVerifyFailed(_ActiveRemote!);
        }
        else if (flag == 1)
        {
            _ecdhTheirs = reader.ReadBytes(32);
            _edTheirs = reader.ReadBytes(32);

            _curve25519 = Curve25519.Create("curve25519-sha256");
            _ecdhOurs = _curve25519.GetPublicKey();
            _ecdhShared = _curve25519.GetSharedSecret(_ecdhTheirs);

            byte[] dataToSign = new byte[64];
            Array.Copy(_ecdhOurs, 0, dataToSign, 0, 32);
            Array.Copy(_ecdhTheirs, 0, dataToSign, 32, 32);

            byte[] signature = _ed25519.SignMessage(dataToSign);

            using AESCTRBufferedCipher cipher = AESCTRBufferedCipher.CreateDefault(_ecdhShared);

            byte[] encryptedSignature = cipher.DoFinal(signature);
            byte[] output = [.. _ecdhOurs, .. encryptedSignature];

            responseMessage.Headers.Add("Content-Type", "application/octet-stream");
            await responseMessage.WriteAsync(output, 0, output.Length, cancellationToken);
        }
        else
        {
            // Unknown flag and response nothing
            _logger?.UnknownFlagInPairVerify(flag);
        }
    }

    private async Task OnPostFpSetupRequested(RtspRequestMessage requestMessage, RtspResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        // If session is not paired, something gone wrong.
        if (_ecdhShared == null || !_pairVerified)
        {
            responseMessage.Status = StatusCode.UNAUTHORIZED;
            return;
        }

        if (requestMessage.Body.Length < 4 || requestMessage.Body[4] != 0x03)
        {
            // Unsupported fairplay version
            _logger?.UnsupportedFairPlayVersion(requestMessage.Body[4]);
            return;
        }

        if (requestMessage.Body.Length == 16)
        {
            byte[][] replyMessage =
            [
                [0x46,0x50,0x4c,0x59,0x03,0x01,0x02,0x00,0x00,0x00,0x00,0x82,0x02,0x00,0x0f,0x9f,0x3f,0x9e,0x0a,0x25,0x21,0xdb,0xdf,0x31,0x2a,0xb2,0xbf,0xb2,0x9e,0x8d,0x23,0x2b,0x63,0x76,0xa8,0xc8,0x18,0x70,0x1d,0x22,0xae,0x93,0xd8,0x27,0x37,0xfe,0xaf,0x9d,0xb4,0xfd,0xf4,0x1c,0x2d,0xba,0x9d,0x1f,0x49,0xca,0xaa,0xbf,0x65,0x91,0xac,0x1f,0x7b,0xc6,0xf7,0xe0,0x66,0x3d,0x21,0xaf,0xe0,0x15,0x65,0x95,0x3e,0xab,0x81,0xf4,0x18,0xce,0xed,0x09,0x5a,0xdb,0x7c,0x3d,0x0e,0x25,0x49,0x09,0xa7,0x98,0x31,0xd4,0x9c,0x39,0x82,0x97,0x34,0x34,0xfa,0xcb,0x42,0xc6,0x3a,0x1c,0xd9,0x11,0xa6,0xfe,0x94,0x1a,0x8a,0x6d,0x4a,0x74,0x3b,0x46,0xc3,0xa7,0x64,0x9e,0x44,0xc7,0x89,0x55,0xe4,0x9d,0x81,0x55,0x00,0x95,0x49,0xc4,0xe2,0xf7,0xa3,0xf6,0xd5,0xba],
                [0x46,0x50,0x4c,0x59,0x03,0x01,0x02,0x00,0x00,0x00,0x00,0x82,0x02,0x01,0xcf,0x32,0xa2,0x57,0x14,0xb2,0x52,0x4f,0x8a,0xa0,0xad,0x7a,0xf1,0x64,0xe3,0x7b,0xcf,0x44,0x24,0xe2,0x00,0x04,0x7e,0xfc,0x0a,0xd6,0x7a,0xfc,0xd9,0x5d,0xed,0x1c,0x27,0x30,0xbb,0x59,0x1b,0x96,0x2e,0xd6,0x3a,0x9c,0x4d,0xed,0x88,0xba,0x8f,0xc7,0x8d,0xe6,0x4d,0x91,0xcc,0xfd,0x5c,0x7b,0x56,0xda,0x88,0xe3,0x1f,0x5c,0xce,0xaf,0xc7,0x43,0x19,0x95,0xa0,0x16,0x65,0xa5,0x4e,0x19,0x39,0xd2,0x5b,0x94,0xdb,0x64,0xb9,0xe4,0x5d,0x8d,0x06,0x3e,0x1e,0x6a,0xf0,0x7e,0x96,0x56,0x16,0x2b,0x0e,0xfa,0x40,0x42,0x75,0xea,0x5a,0x44,0xd9,0x59,0x1c,0x72,0x56,0xb9,0xfb,0xe6,0x51,0x38,0x98,0xb8,0x02,0x27,0x72,0x19,0x88,0x57,0x16,0x50,0x94,0x2a,0xd9,0x46,0x68,0x8a],
                [0x46,0x50,0x4c,0x59,0x03,0x01,0x02,0x00,0x00,0x00,0x00,0x82,0x02,0x02,0xc1,0x69,0xa3,0x52,0xee,0xed,0x35,0xb1,0x8c,0xdd,0x9c,0x58,0xd6,0x4f,0x16,0xc1,0x51,0x9a,0x89,0xeb,0x53,0x17,0xbd,0x0d,0x43,0x36,0xcd,0x68,0xf6,0x38,0xff,0x9d,0x01,0x6a,0x5b,0x52,0xb7,0xfa,0x92,0x16,0xb2,0xb6,0x54,0x82,0xc7,0x84,0x44,0x11,0x81,0x21,0xa2,0xc7,0xfe,0xd8,0x3d,0xb7,0x11,0x9e,0x91,0x82,0xaa,0xd7,0xd1,0x8c,0x70,0x63,0xe2,0xa4,0x57,0x55,0x59,0x10,0xaf,0x9e,0x0e,0xfc,0x76,0x34,0x7d,0x16,0x40,0x43,0x80,0x7f,0x58,0x1e,0xe4,0xfb,0xe4,0x2c,0xa9,0xde,0xdc,0x1b,0x5e,0xb2,0xa3,0xaa,0x3d,0x2e,0xcd,0x59,0xe7,0xee,0xe7,0x0b,0x36,0x29,0xf2,0x2a,0xfd,0x16,0x1d,0x87,0x73,0x53,0xdd,0xb9,0x9a,0xdc,0x8e,0x07,0x00,0x6e,0x56,0xf8,0x50,0xce],
                [0x46,0x50,0x4c,0x59,0x03,0x01,0x02,0x00,0x00,0x00,0x00,0x82,0x02,0x03,0x90,0x01,0xe1,0x72,0x7e,0x0f,0x57,0xf9,0xf5,0x88,0x0d,0xb1,0x04,0xa6,0x25,0x7a,0x23,0xf5,0xcf,0xff,0x1a,0xbb,0xe1,0xe9,0x30,0x45,0x25,0x1a,0xfb,0x97,0xeb,0x9f,0xc0,0x01,0x1e,0xbe,0x0f,0x3a,0x81,0xdf,0x5b,0x69,0x1d,0x76,0xac,0xb2,0xf7,0xa5,0xc7,0x08,0xe3,0xd3,0x28,0xf5,0x6b,0xb3,0x9d,0xbd,0xe5,0xf2,0x9c,0x8a,0x17,0xf4,0x81,0x48,0x7e,0x3a,0xe8,0x63,0xc6,0x78,0x32,0x54,0x22,0xe6,0xf7,0x8e,0x16,0x6d,0x18,0xaa,0x7f,0xd6,0x36,0x25,0x8b,0xce,0x28,0x72,0x6f,0x66,0x1f,0x73,0x88,0x93,0xce,0x44,0x31,0x1e,0x4b,0xe6,0xc0,0x53,0x51,0x93,0xe5,0xef,0x72,0xe8,0x68,0x62,0x33,0x72,0x9c,0x22,0x7d,0x82,0x0c,0x99,0x94,0x45,0xd8,0x92,0x46,0xc8,0xc3,0x59]
            ];

            // Get mode and send correct reply message
            // byte mode = requestMessage.Body[14];
            byte[] output = replyMessage[requestMessage.Body[14]];

            responseMessage.Headers.Add("Content-Type", "application/octet-stream");
            await responseMessage.WriteAsync(output, 0, output.Length, cancellationToken);
        }

        if (requestMessage.Body.Length == 164)
        {
            byte[] fpHeader = [0x46, 0x50, 0x4c, 0x59, 0x03, 0x01, 0x04, 0x00, 0x00, 0x00, 0x00, 0x14];

            byte[] keyMsg = new byte[164];
            byte[] output = new byte[32];

            Array.Copy(requestMessage.Body, 0, keyMsg, 0, 164);
            _keyMsg = keyMsg;
            _logger?.FairPlaySetUp(_ActiveRemote!);

            byte[] data = [.. requestMessage.Body.Skip(144)];
            Array.Copy(fpHeader, 0, output, 0, 12);
            Array.Copy(data, 0, output, 12, 20);

            responseMessage.Headers.Add("Content-Type", "application/octet-stream");
            await responseMessage.WriteAsync(output, 0, output.Length, cancellationToken);
        }
    }

    private async Task OnSetupRequested(RtspRequestMessage requestMessage, RtspResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        // If session is not ready, something gone wrong.
        if (_keyMsg == null || _ecdhShared == null || !_pairVerified)
        {
            responseMessage.Status = StatusCode.BADREQUEST;
            return;
        }

        NSDictionary nsDict = (PropertyListParser.Parse(requestMessage.Body) as NSDictionary)!;
        Dictionary<string, NSObject> plistDict = nsDict.ToDictionary();

        if (plistDict.TryGetValue("streams", out NSObject? nSObject))
        {
            var stream = (Dictionary<string, object>)((object[])nSObject.ToObject())[0];
            short type = Convert.ToInt16((int)stream["type"]);

            NSDictionary? keyValuePairs = null;

            if (type == 96)
            {
                if (!stream.TryGetValue("audioFormat", out object? audioFormatValue)) return;
                if (!stream.TryGetValue("controlPort", out object? controlPortValue)) return;

                stream.TryGetValue("latencyMin", out object? latencyMinValue);
                stream.TryGetValue("latencyMax", out object? latencyMaxValue);

                _deviceSession?.CreateAudioController
                (
                    Convert.ToUInt16((int)controlPortValue),
                    (AudioFormat)(int)audioFormatValue,
                    latencyMinValue is int latencyMin ? latencyMin : null,
                    latencyMaxValue is int latencyMax ? latencyMax : null
                );

                _deviceSession?.AudioController?.BeginConnectionWorkers();

                keyValuePairs = new()
                {
                    {
                        "streams",
                        new NSArray()
                        {
                            new NSDictionary
                            {
                                { "dataPort", (int)_deviceSession!.AudioController!.DataPort },
                                { "controlPort", (int)_deviceSession!.AudioController!.ControlPort },
                                { "type", 96 },
                            }
                        }
                    },
                    { "timingPort", (int)_deviceSession!.AudioController!.TimingPort },
                };

            }
            else if (type == 110)
            {
                if (!stream.TryGetValue("streamConnectionID", out object? streamConnectionIDValue)) return;

                stream.TryGetValue("latencyMs", out object? latencyMsValue);
                stream.TryGetValue("timestampinfo", out object? timestampinfoValue);

                _deviceSession?.CreateMirrorController(unchecked((ulong)(long)streamConnectionIDValue).ToString());
                _deviceSession?.MirrorController?.BeginConnectionWorkers();

                keyValuePairs = new()
                {
                    {
                        "streams",
                        new NSArray()
                        {
                            new NSDictionary
                            {
                                { "dataPort", (int)_deviceSession!.MirrorController!.DataPort },
                                { "type", 110 },
                            }
                        }
                    },
                    { "timingPort", (int)_deviceSession!.MirrorController!.TimingPort },
                };
            }

            if (keyValuePairs is not null)
            {
                var plistBytes = BinaryPropertyListWriter.WriteToArray(keyValuePairs);

                responseMessage.Headers.Add("Content-Type", "application/x-apple-binary-plist");
                await responseMessage.WriteAsync(plistBytes, 0, plistBytes.Length, cancellationToken);
            }
        }
        else
        {
            if (!plistDict.TryGetValue("eiv", out NSObject? eivValue)) return;
            if (!plistDict.TryGetValue("ekey", out NSObject? ekeyValue)) return;
            if (!plistDict.TryGetValue("timingPort", out NSObject? timingPortValue)) return;
            if (!plistDict.TryGetValue("name", out NSObject? nameValue)) return;
            if (!plistDict.TryGetValue("macAddress", out NSObject? macAddressValue)) return;

            plistDict.TryGetValue("model", out NSObject? modelValue);
            plistDict.TryGetValue("isScreenMirroringSession", out NSObject? isScreenMirroringSessionValue);

            _deviceSession = new((byte[])eivValue.ToObject(), _ecdhShared, Convert.ToUInt16((int)timingPortValue.ToObject()), _loggerFactory?.CreateLogger<DeviceSession>())
            {
                DeviceMacAddress = (string)macAddressValue.ToObject(),
                DeviceDisplayName = (string)nameValue.ToObject(),
                DeviceModel = modelValue?.ToObject() as string,
                DacpId = _DACPID!,
                ActiveRemote = _ActiveRemote!,
                IsMirrorSession = isScreenMirroringSessionValue?.ToObject() is bool isScreenMirroringSession && isScreenMirroringSession
            };

            _deviceSession.DecrypteAesKey(_keyMsg, (byte[])ekeyValue.ToObject());

            SessionPaired?.Invoke(this, _deviceSession);
        }
    }

    private async Task OnGetParameterRequested(RtspRequestMessage requestMessage, RtspResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        string parameter = Encoding.ASCII.GetString(requestMessage.Body).Trim();

        if (parameter == "volume")
        {
            // The volume is a float value representing the audio attenuation in dB.
            // Then it goes from –30 to 0.
            // A value of –144 means the audio is muted.
            double volume = ((_deviceSession?.Volume ?? 100) / 100 * 30) - 30;
            byte[] output = Encoding.ASCII.GetBytes($"volume: {volume:0.000000}\r\n"); // if missing the "\r\n", the device will disconnect

            responseMessage.Headers.Add("Content-Type", "text/parameters");
            await responseMessage.WriteAsync(output, 0, output.Length, cancellationToken);
        }
    }

    private static Task OnRecordRequested()
    {
        // return nothing
        return Task.CompletedTask;
    }

    private static Task OnPostFeedbackRequested()
    {
        // return nothing
        return Task.CompletedTask;
    }

    private Task OnFlushRequested(RtspRequestMessage requestMessage)
    {
        int nextSeq = -1;

        if (requestMessage.Headers.TryGetValue("RTP-Info", out var value))
        {
            string rtpInfo = ((string[])value)[0].Trim();

            if (!string.IsNullOrEmpty(rtpInfo))
            {
                var match = FindSeqRegex.Match(rtpInfo);

                if (match.Success)
                    nextSeq = int.Parse(match.Groups[1].Value);
            }
        }

        _deviceSession?.AudioController?.Flush(nextSeq);
        return Task.CompletedTask;
    }

    private Task OnTeardownRequested(RtspRequestMessage requestMessage)
    {
        NSDictionary nsDict = (PropertyListParser.Parse(requestMessage.Body) as NSDictionary)!;
        Dictionary<string, NSObject> plistDict = nsDict.ToDictionary();

        if (plistDict.TryGetValue("streams", out NSObject? nSObject))
        {
            foreach (var obj in (object[])nSObject.ToObject())
            {
                Dictionary<string, object> stream = (Dictionary<string, object>)obj;
                short type = Convert.ToInt16((int)stream["type"]);

                if (type == 96) _deviceSession?.CloseAudioController();
                if (type == 110) _deviceSession?.CloseMirrorController();
            }
        }
        else
        {
            _deviceSession?.CloseAudioController();
            _deviceSession?.CloseMirrorController();

            _disconnectRequested = true;
        }

        return Task.CompletedTask;
    }

    private Task OnSetParameterRequested(RtspRequestMessage requestMessage)
    {
        if (!requestMessage.Headers.TryGetValue("Content-Type", out var contentTypeValue)) return Task.CompletedTask;

        string contentType = contentTypeValue.Values[0];

        if (contentType.Equals("text/parameters", StringComparison.OrdinalIgnoreCase))
        {
            string[] keyPair = [.. Encoding.ASCII.GetString(requestMessage.Body)
            .Split(":", StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim())];

            (string key, string value) = (keyPair[0], keyPair[1]);

            if (key.Equals("volume", StringComparison.OrdinalIgnoreCase))
                _deviceSession?.RemoteSetVolume(double.Parse(value));
            else if (key.Equals("progress", StringComparison.OrdinalIgnoreCase))
            {
                string[] progressValues = value.Split("/", StringSplitOptions.RemoveEmptyEntries);

                var start = long.Parse(progressValues[0]);
                var current = long.Parse(progressValues[1]);
                var end = long.Parse(progressValues[2]);

                _deviceSession?.RemoteSetProgress(new
                (
                    TimeSpan.FromSeconds((end - start) / 44100),
                    TimeSpan.FromSeconds((current - start) / 44100)
                ));
            }
        }
        else if (contentType.Equals("application/x-dmap-tagged", StringComparison.OrdinalIgnoreCase))
        {
            DMapTagged dmap = new();
            Dictionary<string, object> output = dmap.Decode(requestMessage.Body);

            if (!output.TryGetValue("minm", out var minm)) return Task.CompletedTask;
            output.TryGetValue("asar", out var asar);
            output.TryGetValue("asal", out var asal);

            _deviceSession?.RemoteSetWorkInfo(new((string)minm, (string?)asar, (string?)asal));
        }
        else if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            _deviceSession?.RemoteSetCover(requestMessage.Body);

        return Task.CompletedTask;
    }
}

public partial class RtspConnection
{
    [GeneratedRegex(@"seq\=([^;]*)")]
    private static partial Regex GenFindSeqRegex();

    private readonly static Regex FindSeqRegex = GenFindSeqRegex();
}

internal static partial class RtspConnectionLoggers
{
    [LoggerMessage(LogLevel.Error, "Unknown flag in PairVerify process: [\"flag\": (byte){flag}]")]
    public static partial void UnknownFlagInPairVerify(this ILogger logger, byte flag);

    [LoggerMessage(LogLevel.Error, "Pair Verify Failed for [{activeRemote}]")]
    public static partial void PairVerifyFailed(this ILogger logger, string activeRemote);

    [LoggerMessage(LogLevel.Information, "Pair Verified for [{activeRemote}]")]
    public static partial void PairVerified(this ILogger logger, string activeRemote);

    [LoggerMessage(LogLevel.Error, "Unsupported fairplay version: [\"body[4]\": (byte){value}]")]
    public static partial void UnsupportedFairPlayVersion(this ILogger logger, byte value);

    [LoggerMessage(LogLevel.Information, "FairPlay is setup for [{activeRemote}]")]
    public static partial void FairPlaySetUp(this ILogger logger, string activeRemote);
}