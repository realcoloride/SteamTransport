using Riptide.Utils;
using Steamworks;
using System;
using System.Runtime.InteropServices;

namespace Riptide.Transports.Steam
{
    public abstract class SteamPeer
    {
        public const string LogName = "STEAM";

        protected const int MaxMessages = 256;
        private readonly byte[] ReceiveBuffer = new byte[Message.MaxSize + sizeof(ushort)];

        private readonly IntPtr[] MessagePointers = new IntPtr[MaxMessages];

        protected void Receive(SteamConnection fromConnection)
        {
            int messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(fromConnection.SteamNetConnection, MessagePointers, MaxMessages);
            if (messageCount == 0) return;

            for (int i = 0; i < messageCount; i++)
            {
                SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(MessagePointers[i]);

                if (data.m_cbSize > 0)
                {
                    int byteCount = data.m_cbSize;
                    if (data.m_cbSize > ReceiveBuffer.Length)
                    {
                        RiptideLogger.Log(LogType.Warning, $"{LogName}: Can't fully handle {data.m_cbSize} bytes because it exceeds the maximum of {ReceiveBuffer.Length}. Data will be incomplete!");
                        byteCount = ReceiveBuffer.Length;
                    }

                    Marshal.Copy(data.m_pData, ReceiveBuffer, 0, data.m_cbSize);
                    OnDataReceived(ReceiveBuffer, byteCount, fromConnection);
                }

                SteamNetworkingMessage_t.Release(MessagePointers[i]);
            }
        }

        internal void Send(byte[] dataBuffer, int numBytes, HSteamNetConnection toConnection)
        {
            GCHandle handle = GCHandle.Alloc(dataBuffer, GCHandleType.Pinned);
            IntPtr pDataBuffer = handle.AddrOfPinnedObject();

            SteamNetworkingSockets.SendMessageToConnection(toConnection, pDataBuffer, (uint)numBytes, Constants.k_nSteamNetworkingSend_Unreliable, out long _);
            handle.Free();
        }
        
        protected abstract void OnDataReceived(byte[] dataBuffer, int amount, SteamConnection fromConnection);
    }
}
