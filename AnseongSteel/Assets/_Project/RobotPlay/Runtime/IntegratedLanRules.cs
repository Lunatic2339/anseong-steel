using System.Net;
using System.Net.Sockets;
namespace AnseongSteel.RobotPlay
{
    public static class IntegratedLanRules
    {
        public static bool IsNewSequence(uint next, uint previous)
            => unchecked((int)(next - previous)) > 0;
        public static bool TryEndpoint(string host, ushort port, out string address, out string error)
        {
            address = host?.Trim(); error = "";
            if (port == 0) { error = "Port must be 1..65535"; return false; }
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            { error = "Enter the host PC's IPv4 address (127.0.0.1 only for same-PC tests)"; return false; }
            byte[] bytes = ip.GetAddressBytes();
            if (bytes[0] == 0 || bytes[0] >= 224 || address == "255.255.255.255")
            { error = "Use a unicast host IPv4 address"; return false; }
            address = ip.ToString(); return true;
        }
    }
}