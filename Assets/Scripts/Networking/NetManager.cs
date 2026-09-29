using Steamworks;
using System;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using Steamworks.Data;
using Unity.VisualScripting;
using NUnit.Framework.Constraints;

// Network manager to handle the overall network
public class NetManager : NetworkManager
{
    /// <summary>Temporary data storage for users currently joining the host</summary>
    private Dictionary<ulong, PlayerData> pendingSteamData = new();
    private int spawnIndex = 1;

    /// <summary>Stores all the players connected to the host, only the server should write to this</summary>
    public NetworkList<PlayerData> ConnectedPlayers = new();

    /// <summary>Emitted when a lobby is successfully created by a host</summary>
    public event Action LobbyCreated;

    /// <summary>Emitted when a lobby is successfully deleted by a host</summary>
    public event Action LobbyDeleted;

    /// <summary>Emitted when a lobby is successfully modified by a host</summary>
    public event Action LobbyModified;

    /// <summary>Emitted when a lobby is successfully joined by a player</summary>
    public event Action LobbyJoined;

    /// <summary>Emitted when a lobby is left by the player</summary>
    public event Action LobbyLeft;

    private void Awake()
    {
        if (this == null) { Debug.Log("Error encountered loading singleton"); return; }

        this.ConnectionApprovalCallback = OnApproval;
        this.OnConnectionEvent += HandleConnection;

        DontDestroyOnLoad(this);
    }
    /// <summary>
    /// Host a Steam lobby using your Steam ID as a reference, running this will make the user the host of the server
    /// </summary>
    /// <param name="maxPlayers">The max players allowed to join the lobby created by steam, this parameter does not affect the server-client connectivity</param>
    /// <returns>True if the player creates the steam lobby and created the server, and false if either could not be completed</returns>
    public async Task<bool> CreateLobby(int MaxPlayers) 
    {
        try {
            // Utilizes steam matchmaking to create the lobby
            var createLobbyOutput = await SteamMatchmaking.CreateLobbyAsync(MaxPlayers);
            if (!createLobbyOutput.HasValue) return false;

            // By default, the lobby is public and joinable
            var lobby = createLobbyOutput.Value;
            lobby.SetPublic();
            lobby.SetJoinable(true);

            lobby.SetData("HostSteamID", SteamClient.SteamId.ToString());
            // You can set more custom data here, like lobby.SetData("MapName", "Arastos");
        }
        catch (Exception e) 
        {
            Debug.Log("Failed to create lobby: " + e.Message);
            return false;
        }

        SendPayload();
        
        // Singleton is the singleton instance for the network manager
        bool success = Singleton.StartHost();

        if (success)
        {
            pendingSteamData[ServerClientId] = new PlayerData(SteamClient.SteamId, LocalClientId, SteamClient.Name);
        }

        // Invoke some actions
        // LobbyCreated.Invoke();
        // LobbyJoined.Invoke();

        return success;
    }

    /// <summary>
    /// Connects the player to a lobby on steam and connects the client to the server
    /// </summary>
    /// <param name="lobby">Facepunch (Steamworks) requires a lobby reference to know which lobby to join</param>
    /// <returns>True if the player joined the steam lobby and connected to the server, and false if either could not be completed</returns>
    public async Task<bool> JoinLobby(Lobby lobby) 
    {
        bool joinedSteam = true;

        try {
            if (IsServer) {
                joinedSteam = false;
                throw new Exception("This function was called by a server, cannot join");
            }

            // Join the Steam Lobby before connecting to the actual game
            var result = await lobby.Join();

            if (result != RoomEnter.Success) {
                joinedSteam = false;
                throw new Exception("Failed to join Steam Lobby, returning to menu");
            }

            Debug.Log($"Lobby found and joined, owned by: {lobby.Owner}");
            string hostIDString = lobby.GetData("HostSteamID");

            if (ulong.TryParse(hostIDString, out ulong hostSteamID))
            {
                var transport = this.GetComponent<FacepunchTransport>();
                transport.targetSteamID = hostSteamID;

                // Pack your own ID and join
                SendPayload();
                bool startClientSuccess = this.StartClient();
                if (!startClientSuccess) throw new Exception("Netcode failed to start client.");
            }
            else
            {
                throw new Exception("Could not post the HostSteamID, either null or invalid: " + hostIDString);
            }
        } catch (Exception e) 
        {
            Debug.LogError($"Client encountered an error: {e.Message}");

            if (joinedSteam) 
            {
                lobby.Leave();
                Debug.LogError("Left Steam lobby due to netcode error");
            } return false;
        }
        Debug.Log($"{SteamClient.Name} has successfully joined the lobby.");
        LobbyJoined.Invoke();
        return true;
    }

    private void OnApproval(ConnectionApprovalRequest request, ConnectionApprovalResponse response) 
    {
        byte[] payload = request.Payload;
        ulong steamId = 0;
        string playerName = "";

        // The "Payload" is located in request.Payload
        if (payload != null && payload.Length >= 8)
        {
            // Grab the 1st 8 bytes
            steamId = BitConverter.ToUInt64(payload, 0);

            // Grab the remaining bytes
            playerName = System.Text.Encoding.UTF8.GetString(payload, 8, payload.Length - 8);

            // Store it temporarily using the ClientId as the key
            // Add code to whitelist or blacklist players from joining
            pendingSteamData[request.ClientNetworkId] = new PlayerData(steamId, request.ClientNetworkId, playerName);

            response.Approved = true;
            response.CreatePlayerObject = true;
        }
        else
        {
            // If they are the host, let them in even without a payload
            if (request.ClientNetworkId == ServerClientId)
            {
                steamId = SteamClient.SteamId;
                playerName = SteamClient.Name;
                pendingSteamData[request.ClientNetworkId] = new PlayerData(steamId, request.ClientNetworkId, playerName);

                response.Approved = true;
                response.CreatePlayerObject = true;
            }
            else
            {
                Debug.LogError("Join denied: No SteamID provided.");
                response.Approved = false;
                response.Reason = "Missing SteamID";
            }
        }

        // Code to handle spawning in a player
        if (response.Approved)
        {
            response.Position = Vector3.zero;
            if (IsServer)
            {
                spawnIndex++;
            }
            Debug.Log($"Spawned player for {playerName}");
        }
    }

    private void HandleConnection(NetworkManager manager, ConnectionEventData cd)
    {
        if (!IsServer) return;

        if (cd.EventType == ConnectionEvent.ClientConnected)
        {
            if (pendingSteamData.TryGetValue(cd.ClientId, out PlayerData playerData))
            {
                pendingSteamData.Remove(cd.ClientId);

                if (playerData.SteamID != 0)
                {
                    ConnectedPlayers.Add(playerData);
                    Debug.Log($"Player Connected! PlayerName: {playerData.PlayerName}, ClientId: {playerData.ClientID}, SteamId: {playerData.SteamID}");
                }
                else Debug.Log($"An error occurred connecting player to server, the following information was storeed- PlayerName: {playerData.PlayerName}, ClientId: {playerData.ClientID}, SteamId: {playerData.SteamID}");
            }

            else Debug.Log($"An error occurred connecting player to server, no pending information was found");
        }

        else if (cd.EventType == ConnectionEvent.ClientDisconnected)
        {
            for (int i = 0; i < ConnectedPlayers.Count; i++)
            {
                if (ConnectedPlayers[i].ClientID == cd.ClientId)
                {
                    Debug.Log($"Disconnected {ConnectedPlayers[i].PlayerName} from server.");
                    ConnectedPlayers.RemoveAt(i);
                    LobbyLeft.Invoke();
                    break;
                }
            }
        }
    }


    /// <summary>
    /// Converts the Steam ID and the Steam name of a user and packages it into a single byte[] array
    /// </summary>
    private void SendPayload() 
    {
        // 1. Convert the ulong steam ID into a raw byte array- this will always be 8 bytes, due to steam's id system
        byte[] id = BitConverter.GetBytes(SteamClient.SteamId);

        // 2. Convert the string steam name into a raw byte array
        byte[] name = System.Text.Encoding.UTF8.GetBytes(SteamClient.Name);

        // 3. Create the payload containing the exact amount of space needed to stitch the 2 arrays together
        byte[] payload = new byte[id.Length + name.Length];

        // Copy the id bytes into the start of the payload
        // Copies blocks from arrays
        // (src- material to copy, src offset- number into the src to go before beginning the copy, dst- original array, dst offset- distance out from the start of dst to paste, count- number of elements to paste between arrays)
        Buffer.BlockCopy(id, 0, payload, 0, id.Length);

        Buffer.BlockCopy(name, 0, payload, id.Length, name.Length);

        // NetworkConfig.ConnectionData can only send a byte array
        NetworkConfig.ConnectionData = payload;
    }
}
