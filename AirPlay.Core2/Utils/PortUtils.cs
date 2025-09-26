using System.Net.NetworkInformation;

namespace AirPlay.Core2.Utils;

internal static class PortUtils
{
    public static IEnumerable<ushort> GetAvalivableTcpPorts(ushort startPort, ushort count)
    {
        IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        int[] tcpPortsUsed = [.. ipGlobalProperties.GetActiveTcpListeners().Select(i => i.Port)];

        ushort maxPort = 65535;
        ushort currentPort = startPort;

        while (count > 0 && currentPort <= maxPort)
        {
            if (tcpPortsUsed.Contains((int)currentPort))
            {
                currentPort++;
                continue;
            }

            yield return currentPort;
            currentPort++;
            count--;
        }
    }

    public static IEnumerable<ushort> GetAvalivableUdpPorts(ushort startPort, ushort count)
    {
        IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        int[] udpPortsUsed = [.. ipGlobalProperties.GetActiveUdpListeners().Select(i => i.Port)];

        ushort maxPort = 65535;
        ushort currentPort = startPort;

        while (count > 0 && currentPort <= maxPort)
        {
            if (udpPortsUsed.Contains((int)currentPort))
            {
                currentPort++;
                continue;
            }

            yield return currentPort;
            currentPort++;
            count--;
        }
    }
}
