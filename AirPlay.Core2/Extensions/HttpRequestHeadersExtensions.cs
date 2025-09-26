using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace AirPlay.Core2.Extensions;

internal static class HttpRequestHeadersExtensions
{
    extension(HttpRequestHeaders headers)
    {
        public bool TryGetValue(string name, [NotNullWhen(true)] out string[]? value)
        {
            value = null;

            foreach (var keyValuePair in headers)
            {
                if (keyValuePair.Key == name)
                {
                    value = [.. keyValuePair.Value];
                    return true;
                }
            }

            return false;
        }
    }
}
