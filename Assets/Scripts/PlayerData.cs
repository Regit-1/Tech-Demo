using Steamworks;
using System;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Used for a list in order to store and easily retrieve player data locally, instead of contacting steam
/// </summary>
public struct PlayerData : IEquatable<PlayerData>
{
    [Header("Returns the steamID for the player's steam account")]
    public ulong SteamID;
    public ulong ClientID;

    public FixedString32Bytes PlayerName;

    /// <summary>
    /// Used for a list in order to store and easily retrieve player data locally, instead of contacting steam
    /// </summary>
    /// <param name="steamID">Steam ID of the stored user</param>
    /// <param name="clientID">Client ID of the stored user</param>
    /// <param name="playerName">The Steam player name of the stored user</param>
    public PlayerData(ulong steamID, ulong clientID, string playerName)
    {
        this.SteamID = steamID;
        this.ClientID = clientID;
        this.PlayerName = playerName;
    }

    // Script to check if the given playerdata is equal to the data of this player
    public bool Equals(PlayerData otherPlayer) => SteamID== otherPlayer.SteamID && ClientID == otherPlayer.ClientID;
}
