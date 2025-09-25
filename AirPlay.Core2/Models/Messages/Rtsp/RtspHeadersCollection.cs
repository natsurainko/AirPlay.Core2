using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace AirPlay.Core2.Models.Messages.Rtsp;

public class RtspHeadersCollection : IEnumerable<RtspHeader>
{
    private readonly Dictionary<string, RtspHeader> _headers;

    public RtspHeadersCollection()
    {
        _headers = new Dictionary<string, RtspHeader>(StringComparer.OrdinalIgnoreCase);
    }

    public string[] this[string key]
    {
        get => _headers[key].Values;
        set
        {
            if (_headers.ContainsKey(key))
            {
                _headers[key] = new(key, value);
                return;
            }

            _headers.Add(key, new(key, value));
        }
    }

    public ICollection<string> Keys => _headers.Keys;

    public ICollection<string[]> Values => [.. _headers.Values ];

    public int Count => _headers.Count;

    public bool IsReadOnly => false;

    public IEnumerable<T> GetValues<T>(string key)
    {
        var typeConverter = TypeDescriptor.GetConverter(typeof(T));
        var values = _headers[key].Values;

        foreach (var value in values)
            if (typeConverter.ConvertFromString(value) is T tValue)
                yield return tValue;
    }

    public void Add(string key, string value) => _headers.Add(key, new RtspHeader(key, value));

    public void Add(RtspHeader value) => _headers.Add(value.Name, value);

    public bool ContainsKey(string key) => _headers.ContainsKey(key);

    public void Clear() => _headers.Clear();

    public IEnumerator<RtspHeader> GetEnumerator() => _headers.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public bool TryGetValue(string key, [NotNullWhen(true)] out RtspHeader? value) => _headers.TryGetValue(key, out value);
}
