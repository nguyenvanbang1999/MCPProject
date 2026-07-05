using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharedContracts.ConnectController
{
    internal class ConnectUtil
    {
        /// <summary>
        /// function xác định vai trò Ping Master dựa trên hash của endpoint
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static bool DeterminePingMaster(TcpClient client)
        {
            try
            {
                var local = client?.Client?.LocalEndPoint as IPEndPoint;
                var remote = client?.Client?.RemoteEndPoint as IPEndPoint;
                if (local == null || remote == null)
                {
                    return true; // fallback: mặc định chủ động ping
                }
                uint hLocal = HashEndPoint(local);
                uint hRemote = HashEndPoint(remote);
                return hLocal > hRemote;
            }
            catch
            {
                return true;
            }
        }
        private static uint HashEndPoint(IPEndPoint ep)
        {
            // FNV-1a32-bit trên bytes của IPv6 (MapToIPv6) + port (LE)
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            var addrBytes = ep.Address.MapToIPv6().GetAddressBytes();
            for (int i = 0; i < addrBytes.Length; i++)
            {
                hash ^= addrBytes[i];
                hash *= prime;
            }
            unchecked
            {
                byte p0 = (byte)(ep.Port & 0xFF);
                byte p1 = (byte)((ep.Port >> 8) & 0xFF);
                hash ^= p0; hash *= prime;
                hash ^= p1; hash *= prime;
            }
            return hash;
        }
    }
}
