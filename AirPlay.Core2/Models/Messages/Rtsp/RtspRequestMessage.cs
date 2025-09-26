using AirPlay.Core2.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace AirPlay.Core2.Models.Messages.Rtsp;

public partial class RtspRequestMessage
{
    public required RequestType Type { get; set; }

    public required string Path { get; set; }

    public required byte[] Body { get; set; }

    public required RtspHeadersCollection Headers { get; set; }
}

public partial class RtspRequestMessage
{
    public static IEnumerable<RtspRequestMessage> ParseRequestsFromHex(string hexRawData)
    {
        var matches = MethodPatternRegex.Matches(hexRawData);

        for (int i = 0; i < matches.Count; i++)
        {
            string hexRequest = i + 1 < matches.Count
                ? hexRawData.Substring(matches[i].Index, matches[i + 1].Index - matches[i].Index)
                : hexRawData.Substring(matches[i].Index);

            if (TryParse(hexRequest, out RtspRequestMessage? requestMessage))
                yield return requestMessage;
        }
    }

    public static bool TryParse(string hexRequest, [NotNullWhen(true)] out RtspRequestMessage? requestMessage)
    {
        requestMessage = null;
        string[] rows = hexRequest.Split("0D0A", StringSplitOptions.None);

        if (rows.Length == 0) return false;
        if (ParseRequestType(rows[0]) is not RequestType requestType) return false;
        if (ParsePath(rows[0]) is not string path || string.IsNullOrEmpty(path)) return false;

        RtspHeadersCollection rtspHeaders = [];
        byte[] body = [];

        foreach (var headerStr in rows.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(headerStr)) break; // End of headers

            if (RtspHeader.TryParse(headerStr, out var header))
                rtspHeaders.Add(header);
        }

        if (rtspHeaders.TryGetValue("Content-Length", out var lengthHeader))
        {
            int contentLength = int.Parse(lengthHeader.Values[0]);

            if (contentLength > 0)
            {
                // Some request can have body w/ '\r\n' chars
                // Use full hex request to extract body based on 'Content-Length'
                var requestBytes = hexRequest.HexToBytes();
                var bodyBytes = requestBytes.Skip(requestBytes.Length - contentLength).ToArray();

                if (contentLength == bodyBytes.Length)
                    body = bodyBytes;
                else return false; // wrong body length
            }
        }

        requestMessage = new RtspRequestMessage
        {
            Type = requestType,
            Path = path,
            Headers = rtspHeaders,
            Body = body
        };

        return true;
    }
}

partial class RtspRequestMessage
{
    public const string GET = "47455420";
    public const string POST = "504F535420";
    public const string SETUP = "534554555020";
    public const string ANNOUNCE = "414E4E4F554E4345";
    public const string RECORD = "5245434F524420";
    public const string GET_PARAMETER = "4745545F504152414D4554455220";
    public const string SET_PARAMETER = "5345545F504152414D4554455220";
    public const string FLUSH = "464C555348";
    public const string TEARDOWN = "54454152444F574E";
    public const string OPTIONS = "4F5054494F4E53";
    public const string PAUSE = "5041555345";

    public readonly static Regex MethodPatternRegex = GetMethodPatternRegex();

    [GeneratedRegex("^47455420[.]*|^504F535420[.]*|^534554555020[.]*|^4745545F504152414D4554455220[.]*|^5245434F524420[.]*|^5345545F504152414D4554455220[.]*|^414E4E4F554E4345[.]*|^464C555348[.]*|^4F5054494F4E53[.]*|^5041555345[.]*|^54454152444F574E[.]*", RegexOptions.Multiline)]
    private static partial Regex GetMethodPatternRegex();
}

partial class RtspRequestMessage
{
    public enum RequestType : ushort
    {
        GET = 0,
        POST = 1,
        SETUP = 2,
        GET_PARAMETER = 3,
        RECORD = 4,
        SET_PARAMETER = 5,
        ANNOUNCE = 6,
        FLUSH = 7,
        TEARDOWN = 8,
        OPTIONS = 9,
        PAUSE = 10
    }

    private static RequestType? ParseRequestType(string hex)
    {
        if (hex.StartsWith(GET, StringComparison.OrdinalIgnoreCase))
            return RequestType.GET;
        else if (hex.StartsWith(POST, StringComparison.OrdinalIgnoreCase))
            return RequestType.POST;
        else if (hex.StartsWith(SETUP, StringComparison.OrdinalIgnoreCase))
            return RequestType.SETUP;
        else if (hex.StartsWith(RECORD, StringComparison.OrdinalIgnoreCase))
            return RequestType.RECORD;
        else if (hex.StartsWith(GET_PARAMETER, StringComparison.OrdinalIgnoreCase))
            return RequestType.GET_PARAMETER;
        else if (hex.StartsWith(SET_PARAMETER, StringComparison.OrdinalIgnoreCase))
            return RequestType.SET_PARAMETER;
        else if (hex.StartsWith(OPTIONS, StringComparison.OrdinalIgnoreCase))
            return RequestType.OPTIONS;
        else if (hex.StartsWith(ANNOUNCE, StringComparison.OrdinalIgnoreCase))
            return RequestType.ANNOUNCE;
        else if (hex.StartsWith(FLUSH, StringComparison.OrdinalIgnoreCase))
            return RequestType.FLUSH;
        else if (hex.StartsWith(TEARDOWN, StringComparison.OrdinalIgnoreCase))
            return RequestType.TEARDOWN;
        else if (hex.StartsWith(PAUSE, StringComparison.OrdinalIgnoreCase))
            return RequestType.PAUSE;

        return null;
    }

    private static string? ParsePath(string hex)
    {
        var matches = ParsePathRegex().Match(hex);

        if (matches.Success && matches.Groups.Count >= 1)
        {
            var pathHex = matches.Groups[1].Value;
            var pathBytes = pathHex.HexToBytes();
            return Encoding.ASCII.GetString(pathBytes);
        }

        return null;
    }

    [GeneratedRegex("20(.*)20")]
    private static partial Regex ParsePathRegex();
}