using AirPlay.Core2.Models;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace AirPlay.Core2.Services;

public class SessionManager
{
    private readonly ConcurrentDictionary<IPEndPoint, DeviceSession> _sessions = new();

    public event EventHandler<DeviceSession>? SessionCreated;
    public event EventHandler<DeviceSession>? SessionClosed;

    public bool TryAddSession(IPEndPoint endpoint, DeviceSession session)
    {
        if (_sessions.TryAdd(endpoint, session))
        {
            SessionCreated?.Invoke(this, session);
            return true;
        }

        return false;

    }

    public bool TryRemoveSession(IPEndPoint endpoint, [NotNullWhen(true)] out DeviceSession? session)
    {
        if (_sessions.TryRemove(endpoint, out session))
        {
            SessionClosed?.Invoke(this, session);
            return true;
        }

        return false;
    }

    public bool TryGetSession(IPEndPoint endpoint, [NotNullWhen(true)] out DeviceSession? session) => _sessions.TryGetValue(endpoint, out session);

    internal void SetDacpServiceEndPoint(string dacpId, IPEndPoint? endPoint)
    {
        foreach (var session in _sessions.Values)
        {
            if (string.Equals(session.DacpId, dacpId, StringComparison.OrdinalIgnoreCase))
                session.SetDacpServiceEndPoint(endPoint);
        }
    }

    public bool TryGetSession(string dacpId, [NotNullWhen(true)] out DeviceSession? session)
    {
        foreach (var kvp in _sessions)
        {
            if (string.Equals(kvp.Value.DacpId, dacpId, StringComparison.OrdinalIgnoreCase))
            {
                session = kvp.Value;
                return true;
            }
        }

        session = null;
        return false;
    }
}
