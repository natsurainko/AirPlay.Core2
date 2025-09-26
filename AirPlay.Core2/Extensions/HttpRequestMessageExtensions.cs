using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using static AirPlay.Core2.Models.Messages.Rtsp.RtspRequestMessage;

namespace AirPlay.Core2.Extensions;

internal static partial class HttpRequestMessageExtensions
{
    extension(HttpRequestMessage)
    {
        public static IEnumerable<HttpRequestMessage> ParseRequestsFromHex(string hexRawData)
        {
            var matches = MethodPatternRegex.Matches(hexRawData);

            for (int i = 0; i < matches.Count; i++)
            {
                string hexRequest = i + 1 < matches.Count
                    ? hexRawData.Substring(matches[i].Index, matches[i + 1].Index - matches[i].Index)
                    : hexRawData.Substring(matches[i].Index);

                if (TryParse(hexRequest, out HttpRequestMessage? requestMessage))
                    yield return requestMessage;
            }
        }

        public static bool TryParse(string hexRequest, [NotNullWhen(true)] out HttpRequestMessage? requestMessage)
        {
            requestMessage = null;
            string[] rows = hexRequest.Split("0D0A", StringSplitOptions.None);

            if (rows.Length == 0) return false;
            if (ParseRequestType(rows[0]) is not HttpMethod httpMethod) return false;
            if (ParsePath(rows[0]) is not string path || string.IsNullOrEmpty(path)) return false;

            requestMessage = new HttpRequestMessage(httpMethod, path);
            Dictionary<string, string[]> contentHeaders = [];

            foreach (var headerStr in rows.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(headerStr)) break; // End of headers

                if (TryParseHeader(headerStr, out var header))
                {
                    if (header.Value.key.Contains("Content"))
                    {
                        contentHeaders.Add(header.Value.key, [.. header.Value.value]);
                        continue;
                    }

                    requestMessage.Headers.Add(header.Value.key, header.Value.value);
                }
            }

            if (contentHeaders.TryGetValue("Content-Length", out var lengthHeaderValues))
            {
                int contentLength = int.Parse(lengthHeaderValues[0]);

                if (contentLength > 0)
                {
                    // Some request can have body w/ '\r\n' chars
                    // Use full hex request to extract body based on 'Content-Length'
                    var requestBytes = hexRequest.HexToBytes();
                    var bodyBytes = requestBytes.Skip(requestBytes.Length - contentLength).ToArray();

                    if (contentLength == bodyBytes.Length)
                    {
                        MemoryStream memoryStream = new(bodyBytes);
                        requestMessage.Content = new StreamContent(memoryStream);

                        foreach (var kvp in contentHeaders)
                            requestMessage.Content.Headers.Add(kvp.Key, kvp.Value);

                    }
                    else return false; // wrong body length
                }
            }

            return true;
        }
    }
    
    [GeneratedRegex("20(.*)20")]
    private static partial Regex GenParsePathRegex();
    private static readonly Regex ParsePathRegex = GenParsePathRegex();

    private static bool TryParseHeader(string hexRequest, [NotNullWhen(true)] out (string key, IEnumerable<string> value)? header)
    {
        header = null;
        string[] data = [.. hexRequest.Split("3A", StringSplitOptions.RemoveEmptyEntries).Select(h => h.Trim())];

        if (data.Length < 2) return false;

        try
        {
            header = (ParseName(data[0]), ParseValues(data[1]));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ParseName(string hex) => Encoding.ASCII.GetString(hex.HexToBytes());

    private static IEnumerable<string> ParseValues(string hex)
    {
        // Split hex by ',' (2C)
        foreach (var hexValue in hex.Split("2C", StringSplitOptions.RemoveEmptyEntries))
            yield return Encoding.ASCII.GetString(hexValue.HexToBytes()).Trim();
    }


    private static HttpMethod? ParseRequestType(string hex)
    {
        if (hex.StartsWith(GET, StringComparison.OrdinalIgnoreCase))
            return HttpMethod.Get;
        else if (hex.StartsWith(POST, StringComparison.OrdinalIgnoreCase))
            return HttpMethod.Post;

        return null;
    }

    private static string? ParsePath(string hex)
    {
        var matches = ParsePathRegex.Match(hex);

        if (matches.Success && matches.Groups.Count >= 1)
        {
            var pathHex = matches.Groups[1].Value;
            var pathBytes = pathHex.HexToBytes();
            return Encoding.ASCII.GetString(pathBytes);
        }

        return null;
    }
}
