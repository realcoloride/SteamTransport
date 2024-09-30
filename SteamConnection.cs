using Steamworks;
using System;
using System.Collections.Generic;

namespace Riptide.Transports.Steam
{
    public class SteamConnection : Connection, IEquatable<SteamConnection>
    {
        public readonly CSteamID SteamID;
        public readonly HSteamNetConnection SteamNetConnection;

        internal bool DidReceiveConnect;

        private readonly SteamPeer Peer;

        internal SteamConnection(CSteamID steamID, HSteamNetConnection steamNetConnection, SteamPeer peer)
        {
            SteamID = steamID;
            SteamNetConnection = steamNetConnection;
            Peer = peer;
        }

        protected override void Send(byte[] dataBuffer, int amount) => Peer.Send(dataBuffer, amount, SteamNetConnection);

        /// <inheritdoc/>
        public override string ToString() => SteamNetConnection.ToString();

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SteamConnection);
        /// <inheritdoc/>
        public bool Equals(SteamConnection other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return SteamNetConnection.Equals(other.SteamNetConnection);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => -721414014 + EqualityComparer<HSteamNetConnection>.Default.GetHashCode(SteamNetConnection);

        public static bool operator ==(SteamConnection left, SteamConnection right)
        {
            if (left is null)
            {
                if (right is null)
                    return true;

                return false; // Only the left side is null
            }

            // Equals handles case of null on right side
            return left.Equals(right);
        }

        public static bool operator !=(SteamConnection left, SteamConnection right) => !(left == right);
    }
}
