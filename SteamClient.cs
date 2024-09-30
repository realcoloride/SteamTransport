using Riptide.Utils;
using Steamworks;
using System;
using System.Threading.Tasks;

namespace Riptide.Transports.Steam
{
    public class SteamClient(SteamServer localServer = null) : SteamPeer, IClient
    {
        public event EventHandler Connected;
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<DisconnectedEventArgs> Disconnected;
        public event EventHandler ConnectionFailed;

        private const string LocalHostName = "localhost";
        private const string LocalHostIP = "127.0.0.1";

        private SteamConnection SteamConnection;
        public SteamServer LocalServer = localServer;
        private Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionStatusChanged;

        private SteamConnection InitializeRelay(Func<SteamConnection> withCallback, bool useDedicated)
        {
            if (useDedicated)
                SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
            else
                SteamNetworkingUtils.InitRelayNetworkAccess();

            return withCallback.Invoke();
        }

        public Connection ConnectToLobby(CSteamID lobbySteamID, bool useDedicated = false) 
            => ConnectToHostID(SteamMatchmaking.GetLobbyOwner(lobbySteamID), useDedicated);
        public Connection ConnectToHostID(CSteamID hostSteamID, bool useDedicated = false)
            => InitializeRelay(() => SteamConnection = TryConnect(hostSteamID), useDedicated);

        public Connection ConnectToLocalServer(bool useDedicated = false)
            => InitializeRelay(() => 
                LocalServer == null 
                    ? throw new($"No locally running server was specified to connect to! Either pass a {nameof(SteamServer)} instance to your {nameof(SteamClient)}'s constructor or use the {nameof(SteamConnection)} property before attempting to connect locally.")
                    : SteamConnection = CreateLocalConnection(), 
               useDedicated);

        private const string DeprecationMessage = "Using Connect is deprecated. Use the other connect functions instead.";

        [Obsolete(DeprecationMessage)]
        public bool Connect(string hostAddress, out Connection connection, out string connectError)
            => throw new NotSupportedException(DeprecationMessage);

        private SteamConnection CreateLocalConnection()
        {
            ConnectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
            CSteamID playerSteamID = SteamUser.GetSteamID();

            SteamNetworkingIdentity clientIdentity = new();
            clientIdentity.SetSteamID(playerSteamID);

            SteamNetworkingIdentity serverIdentity = new();
            serverIdentity.SetSteamID(playerSteamID);

            SteamNetworkingSockets.CreateSocketPair(
                out HSteamNetConnection connectionToClient, 
                out HSteamNetConnection connectionToServer, 
                false, ref clientIdentity, ref serverIdentity
            );

            localServer.Add(new(playerSteamID, connectionToClient, this));
            OnConnected();

            return new(playerSteamID, connectionToServer, this);
        }

        private SteamConnection TryConnect(CSteamID hostID)
        {
            ConnectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            SteamNetworkingIdentity serverIdentity = new();
            serverIdentity.SetSteamID(hostID);

            SteamNetworkingConfigValue_t[] options = [];
            HSteamNetConnection connectionToServer = SteamNetworkingSockets.ConnectP2P(ref serverIdentity, 0, options.Length, options);

            Task.WhenAny(Task.Delay(6000)).Wait();
            
            return SteamConnection.IsConnected ? new(hostID, connectionToServer, this) : throw new("Connection timed out.");
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            if (!callback.m_hConn.Equals(SteamConnection.SteamNetConnection))
            {
                // When connecting via local loopback connection to a locally running SteamServer (aka
                // this player is also the host), other external clients that attempt to connect seem
                // to trigger ConnectionStatusChanged callbacks for the locally connected client. Not
                // 100% sure why this is the case, but returning out of the callback here when the
                // connection doesn't match that between local client & server avoids the problem.
                return;
            }

            switch (callback.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    OnConnected();
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Closed by peer", false);
                    OnDisconnected(DisconnectReason.Disconnected);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Problem detected", false);
                    OnDisconnected(DisconnectReason.TransportError);
                    break;

                default:
                    RiptideLogger.Log(LogType.Info, $"{LogName}: Connection state changed - {callback.m_info.m_eState} | {callback.m_info.m_szEndDebug}");
                    break;
            }
        }

        public void Poll()
        {
            if (SteamConnection == null) return;
            Receive(SteamConnection);
        }

        public void Disconnect()
        {
            ConnectionStatusChanged?.Dispose();
            ConnectionStatusChanged = null;

            SteamNetworkingSockets.CloseConnection(SteamConnection.SteamNetConnection, 0, "Disconnected", false);
            SteamConnection = null;
        }

        #region Event handlers

        protected virtual void OnConnected()
            => Connected?.Invoke(this, EventArgs.Empty);

        protected override void OnDataReceived(byte[] dataBuffer, int amount, SteamConnection fromConnection)
            => DataReceived?.Invoke(this, new(dataBuffer, amount, fromConnection));

        protected virtual void OnDisconnected(DisconnectReason reason) 
            => Disconnected?.Invoke(this, new(SteamConnection, reason));

        #endregion
    }
}
