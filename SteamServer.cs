using Riptide.Utils;
using Steamworks;
using System;
using System.Collections.Generic;

namespace Riptide.Transports.Steam
{
    public class SteamServer : SteamPeer, IServer
    {
        public event EventHandler<ConnectedEventArgs> Connected;
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<DisconnectedEventArgs> Disconnected;

        public ushort Port { get; private set; }

        private Dictionary<CSteamID, SteamConnection> Connections;
        private HSteamListenSocket ListenSocket;
        private Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionStatusChanged;

        public void Start(ushort port) => Start(port, false);
        public void Start(ushort port, bool useDedicated = false)
        {
            Port = port; Connections = [];
            ConnectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            if (useDedicated) SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
            else SteamNetworkingUtils.InitRelayNetworkAccess();

            SteamNetworkingConfigValue_t[] options = [];
            ListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(port, options.Length, options);
        }

        internal void Add(SteamConnection connection)
        {
            if (!Connections.ContainsKey(connection.SteamID))
            {
                Connections.Add(connection.SteamID, connection);
                OnConnected(connection);
            }
            else
                RiptideLogger.Log(LogType.Info, $"{LogName}: Connection from {connection.SteamID} could not be accepted: Already connected");
        }

        private void Accept(HSteamNetConnection connection)
        {
            EResult result = SteamNetworkingSockets.AcceptConnection(connection);
            if (result != EResult.k_EResultOK)
                RiptideLogger.Log(LogType.Warning, $"{LogName}: Connection could not be accepted: {result}");
        }

        public void Close(Connection connection)
        {
            if (connection is not SteamConnection steamConnection) return;

            SteamNetworkingSockets.CloseConnection(steamConnection.SteamNetConnection, 0, "Disconnected by server", false);
            Connections.Remove(steamConnection.SteamID);
        }

        public void Poll()
        {
            foreach (SteamConnection connection in Connections.Values)
                Receive(connection);
        }

        public void Shutdown()
        {
            if (ConnectionStatusChanged != null)
            {
                ConnectionStatusChanged.Dispose();
                ConnectionStatusChanged = null;
            }

            foreach (SteamConnection connection in Connections.Values)
                SteamNetworkingSockets.CloseConnection(connection.SteamNetConnection, 0, "Server stopped", false);

            Connections.Clear();
            SteamNetworkingSockets.CloseListenSocket(ListenSocket);
        }


        #region Event handlers

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            CSteamID clientSteamId = callback.m_info.m_identityRemote.GetSteamID();
            switch (callback.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    Accept(callback.m_hConn);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    Add(new SteamConnection(clientSteamId, callback.m_hConn, this));
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Closed by peer", false);
                    OnDisconnected(clientSteamId, DisconnectReason.Disconnected);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Problem detected", false);
                    OnDisconnected(clientSteamId, DisconnectReason.TransportError);
                    break;

                default:
                    RiptideLogger.Log(LogType.Info, $"{LogName}: {clientSteamId}'s connection state changed - {callback.m_info.m_eState}");
                    break;
            }
        }

        protected internal virtual void OnConnected(Connection connection)
            => Connected?.Invoke(this, new(connection));

        protected override void OnDataReceived(byte[] dataBuffer, int amount, SteamConnection fromConnection)
        {
            if ((MessageHeader)dataBuffer[0] == MessageHeader.Connect)
            {
                if (fromConnection.DidReceiveConnect)
                    return;

                fromConnection.DidReceiveConnect = true;
            }

            DataReceived?.Invoke(this, new(dataBuffer, amount, fromConnection));
        }

        protected virtual void OnDisconnected(CSteamID steamId, DisconnectReason reason)
        {
            if (!Connections.TryGetValue(steamId, out SteamConnection connection)) return;

            Disconnected?.Invoke(this, new DisconnectedEventArgs(connection, reason));
            Connections.Remove(steamId);
        }

        #endregion
    }
}
