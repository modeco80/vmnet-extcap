using System;

namespace ExtcapNet.PacketPublish
{
    public interface IPacketsPublisher : IDisposable
    {
        /// <summary>
        /// Sends with the wrapper's 'default link layer' and no timestamp
        /// </summary>
        /// <param name="data">Packet bytes</param>
        void Send(ReadOnlySpan<byte> data);
        void Send(ReadOnlySpan<byte> data, LinkLayerType linkLayer);
        void Send(PacketToSend pkt);
    }
}