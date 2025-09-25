using AirPlay.Core2.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace AirPlay.Core2.Models.Messages.Rtsp;

public partial class RtspHeader(string name, params IEnumerable<string> values)
{
    public string Name { get; set; } = name;

    public string[] Values { get; set; } = [.. values];
}

public partial class RtspHeader 
{
    public static bool TryParse(string hexRequest, [NotNullWhen(true)] out RtspHeader? header)
    {
        header = null;
        string[] data = [.. hexRequest.Split("3A", StringSplitOptions.RemoveEmptyEntries).Select(h => h.Trim())];

        if (data.Length < 2) return false;

        try
        {
            header = new RtspHeader
            (
                ParseName(data[0]), 
                ParseValues(data[1])
            );
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

    public static implicit operator string[](RtspHeader rtspHeader) => rtspHeader.Values;
}