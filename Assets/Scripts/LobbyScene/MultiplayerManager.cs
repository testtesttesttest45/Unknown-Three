using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MultiplayerManager : NetworkBehaviour
{
    public const int MAX_PLAYER_AMOUNT = 4;
    private const string PLAYER_PREFS_PLAYER_NAME_MULTIPLAYER = "PlayerNameMultiplayer";

    public static MultiplayerManager Instance { get; private set; }

    public static bool playMultiplayer = true;

    public event EventHandler OnTryingToJoinGame;
    public event EventHandler OnFailedToJoinGame;
    public event EventHandler OnPlayerDataNetworkListChanged;

    [SerializeField] private List<GameObject> playerModelPrefabs;

    public NetworkList<PlayerData> playerDataNetworkList;
    private string playerName;
    public const ulong BOT_CLIENT_ID = 9999;
    private bool IsBot(ulong clientId) => clientId >= 9000;
    private NetworkList<ulong> pendingSwapRequests;
    public NetworkList<ulong> rematchRequests;


    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);

        string nameFromCardGame = CardGameManager.PlayerAvatarName;
        if (!string.IsNullOrWhiteSpace(nameFromCardGame))
        {
            SetPlayerName(nameFromCardGame);
        }
        else
        {
            // fallback
            SetPlayerName(PlayerPrefs.GetString(PLAYER_PREFS_PLAYER_NAME_MULTIPLAYER, "PlayerName" + UnityEngine.Random.Range(100, 1000)));
        }

        playerDataNetworkList = new NetworkList<PlayerData>();
        playerDataNetworkList.OnListChanged += PlayerDataNetworkList_OnListChanged;
        pendingSwapRequests = new NetworkList<ulong>();
        rematchRequests = new NetworkList<ulong>();
    }


    private void Start()
    {
        if (!playMultiplayer)
        {
            // Singleplayer
            StartHost();
            Loader.LoadNetwork(Loader.Scene.CardGameScene);
        }
    }

    public string GetPlayerName()
    {
        return playerName;
    }

    public void SetPlayerName(string playerName)
    {
        this.playerName = playerName;

        PlayerPrefs.SetString(PLAYER_PREFS_PLAYER_NAME_MULTIPLAYER, playerName);
    }
    private void PlayerDataNetworkList_OnListChanged(NetworkListEvent<PlayerData> changeEvent)
    {
        OnPlayerDataNetworkListChanged?.Invoke(this, EventArgs.Empty);
        TryStartGameIfReady(); // whenever list changes, check if we can start the game
    }

    public void StartHost()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback += NetworkManager_ConnectionApprovalCallback;
        NetworkManager.Singleton.OnClientConnectedCallback += NetworkManager_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_Server_OnClientDisconnectCallback;
        NetworkManager.Singleton.StartHost();
    }


    private async void NetworkManager_Server_OnClientDisconnectCallback(ulong clientId)
    {
        for (int i = 0; i < playerDataNetworkList.Count; i++)
        {
            PlayerData playerData = playerDataNetworkList[i];
            if (playerData.clientId == clientId)
            {
                playerDataNetworkList.RemoveAt(i);

                var lobby = LobbyManager.Instance.GetLobby();
                if (lobby != null && !string.IsNullOrEmpty(playerData.playerId.ToString()) && playerData.clientId < 9000)
                {
                    try
                    {
                        await Unity.Services.Lobbies.LobbyService.Instance.RemovePlayerAsync(lobby.Id, playerData.playerId.ToString());

                        Debug.Log($"? Removed disconnected player {playerData.playerId.ToString()} from Unity Lobby.");
                    }
                    catch (LobbyServiceException e)
                    {
                        // "player not found" is a normal scenario if the player was already removed (by disconnection etc.)
                        if (e.Message != null && e.Message.Contains("player not found"))
                        {
                            Debug.LogWarning($"[Lobby] Tried to remove player {playerData.playerId}, but they were already removed.");
                        }
                        else
                        {
                            Debug.LogError("? Failed to remove player from lobby: " + e.Message);
                        }
                    }
                }
                break;
            }
        }
    }


    private void NetworkManager_OnClientConnectedCallback(ulong clientId)
    {
        playerDataNetworkList.Add(new PlayerData
        {
            clientId = clientId,
        });

        SetPlayerNameServerRpc(GetPlayerName());
        SetPlayerIdServerRpc(AuthenticationService.Instance.PlayerId);
        SetPlayerAvatarIndexServerRpc(CardGameManager.PlayerAvatarIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerAvatarIndexServerRpc(int avatarIndex, ServerRpcParams rpcParams = default)
    {
        int index = GetPlayerDataIndexFromClientId(rpcParams.Receive.SenderClientId);
        if (index != -1)
        {
            PlayerData data = playerDataNetworkList[index];
            data.avatarIndex = avatarIndex;
            playerDataNetworkList[index] = data;

            OnPlayerDataNetworkListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerNameServerRpc(string playerName, ServerRpcParams serverRpcParams = default)
    {
        int playerDataIndex = GetPlayerDataIndexFromClientId(serverRpcParams.Receive.SenderClientId);
        if (playerDataIndex != -1)
        {
            PlayerData playerData = playerDataNetworkList[playerDataIndex];
            playerData.playerName = playerName;
            playerDataNetworkList[playerDataIndex] = playerData;

            OnPlayerDataNetworkListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerIdServerRpc(string playerId, ServerRpcParams serverRpcParams = default)
    {
        int playerDataIndex = GetPlayerDataIndexFromClientId(serverRpcParams.Receive.SenderClientId);
        if (playerDataIndex != -1)
        {
            PlayerData playerData = playerDataNetworkList[playerDataIndex];
            playerData.playerId = playerId;
            playerDataNetworkList[playerDataIndex] = playerData;

            OnPlayerDataNetworkListChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    private void NetworkManager_ConnectionApprovalCallback(NetworkManager.ConnectionApprovalRequest connectionApprovalRequest, NetworkManager.ConnectionApprovalResponse connectionApprovalResponse)
    {
        if (SceneManager.GetActiveScene().name != Loader.Scene.InLobbyScene.ToString())
        {
            connectionApprovalResponse.Approved = false;
            connectionApprovalResponse.Reason = "Game has already started";
            return;
        }

        int botCount = 0;
        foreach (var pd in playerDataNetworkList)
            if (pd.clientId >= 9000) botCount++;

        int totalPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count + botCount;


        if (totalPlayers >= MAX_PLAYER_AMOUNT)
        {
            connectionApprovalResponse.Approved = false;
            connectionApprovalResponse.Reason = "Game is full";
            return;
        }

        connectionApprovalResponse.Approved = true;
    }


    public void StartClient()
    {
        OnTryingToJoinGame?.Invoke(this, EventArgs.Empty);

        NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_Client_OnClientDisconnectCallback;
        NetworkManager.Singleton.OnClientConnectedCallback += NetworkManager_Client_OnClientConnectedCallback;
        NetworkManager.Singleton.StartClient();
    }

    private void NetworkManager_Client_OnClientConnectedCallback(ulong clientId)
    {
        SetPlayerNameServerRpc(GetPlayerName());
        SetPlayerIdServerRpc(AuthenticationService.Instance.PlayerId);
        SetPlayerAvatarIndexServerRpc(CardGameManager.PlayerAvatarIndex);
    }



    private void NetworkManager_Client_OnClientDisconnectCallback(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId || clientId == NetworkManager.Singleton.LocalClientId)
        {
            ShowHostDisconnectedUI();
        }

        OnFailedToJoinGame?.Invoke(this, EventArgs.Empty);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        //rematchRequests.Clear();
        // before clearing, we should check if we are the host
        if (IsHost)
        {
            rematchRequests.Clear();
        }
    }



    private void ShowHostDisconnectedUI()
    {
        var ui = FindObjectOfType<DisconnectUI>(true);
        if (ui != null)
        {
            ui.gameObject.SetActive(true);
            ui.Show();
        }
    }

    public bool IsPlayerIndexConnected(int playerIndex)
    {
        return playerIndex < playerDataNetworkList.Count;
    }

    public int GetPlayerDataIndexFromClientId(ulong clientId)
    {
        for (int i = 0; i < playerDataNetworkList.Count; i++)
        {
            if (playerDataNetworkList[i].clientId == clientId)
            {
                return i;
            }
        }
        return -1;
    }

    public PlayerData GetPlayerDataFromClientId(ulong clientId)
    {
        foreach (PlayerData playerData in playerDataNetworkList)
        {
            if (playerData.clientId == clientId)
            {
                return playerData;
            }
        }
        return default;
    }

    public PlayerData GetPlayerData()
    {
        return GetPlayerDataFromClientId(NetworkManager.Singleton.LocalClientId);
    }

    public PlayerData GetPlayerDataFromPlayerIndex(int playerIndex)
    {
        return playerDataNetworkList[playerIndex];
    }


    public void KickPlayer(ulong clientId)
    {
        NetworkManager.Singleton.DisconnectClient(clientId);
        NetworkManager_Server_OnClientDisconnectCallback(clientId);
    }

    public bool IsBotPresent()
    {
        foreach (PlayerData player in playerDataNetworkList)
            if (player.clientId >= 9000)
                return true;
        return false;
    }

    public void AddBotPlayer()
    {
        // (unchanged logic, but we strongly suggest to set isSuperbot=false explicitly)
        if (playerDataNetworkList.Count >= MAX_PLAYER_AMOUNT) return;

        ulong botClientId = GetNextBotClientId();
        if (botClientId < 9000) return;

        int botNumber = GetNextBotNumber(prefix: "Bot ");
        int randomAvatarIndex = UnityEngine.Random.Range(0, 14);

        var botData = new PlayerData
        {
            clientId = botClientId,
            playerName = $"Bot {botNumber}",
            playerId = $"bot-id-{botNumber}",
            isReady = true,
            avatarIndex = randomAvatarIndex,
            isSuperbot = false // explicit
        };

        playerDataNetworkList.Add(botData);
    }

    public void AddSuperbotPlayer()
    {
        if (playerDataNetworkList.Count >= MAX_PLAYER_AMOUNT) return;

        ulong botClientId = GetNextBotClientId();
        if (botClientId < 9000) return;

        int num = GetNextBotNumber(prefix: "Superbot ");
        int randomAvatarIndex = UnityEngine.Random.Range(0, 14);

        var super = new PlayerData
        {
            clientId = botClientId,
            playerName = $"Superbot {num}",
            playerId = $"superbot-id-{num}",
            isReady = true,
            avatarIndex = randomAvatarIndex,
            isSuperbot = true
        };

        playerDataNetworkList.Add(super);
    }

    private int GetNextBotNumber(string prefix)
    {
        var used = new HashSet<int>();
        foreach (var pd in playerDataNetworkList)
        {
            if (pd.clientId >= 9000)
            {
                string name = pd.playerName.ToString();
                if (name.StartsWith(prefix))
                {
                    if (int.TryParse(name.Substring(prefix.Length), out int n))
                        used.Add(n);
                }
            }
        }
        int num = 1;
        while (used.Contains(num)) num++;
        return num;
    }

    public ulong GetNextBotClientId()
    {
        ulong id = BOT_CLIENT_ID;
        var used = new HashSet<ulong>();
        foreach (var pd in playerDataNetworkList) used.Add(pd.clientId);
        while (used.Contains(id) && id > 9000) id--;
        return id;
    }

    public bool IsSuperbotClientId(ulong clientId)
    {
        int idx = GetPlayerDataIndexFromClientId(clientId);
        if (idx == -1) return false;
        return playerDataNetworkList[idx].isSuperbot;
    }

    public void RemoveBotPlayer()
    {
        int removeIndex = -1;
        ulong highestBotId = 0;
        for (int i = 0; i < playerDataNetworkList.Count; i++)
        {
            if (playerDataNetworkList[i].clientId >= 9000)
            {
                if (playerDataNetworkList[i].clientId > highestBotId)
                {
                    highestBotId = playerDataNetworkList[i].clientId;
                    removeIndex = i;
                }
            }
        }
        if (removeIndex != -1)
            playerDataNetworkList.RemoveAt(removeIndex);
    }

    public int GetHumanPlayerCount()
    {
        int count = 0;
        foreach (var player in playerDataNetworkList)
            if (!IsBot(player.clientId)) count++;
        return count;
    }


    public void RemoveBotPlayerById(ulong botClientId)
    {
        for (int i = 0; i < playerDataNetworkList.Count; i++)
        {
            if (playerDataNetworkList[i].clientId == botClientId && playerDataNetworkList[i].clientId >= 9000)
            {
                playerDataNetworkList.RemoveAt(i);
                return;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetPlayerReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        int index = GetPlayerDataIndexFromClientId(rpcParams.Receive.SenderClientId);
        if (index != -1)
        {
            PlayerData data = playerDataNetworkList[index];
            data.isReady = true;
            playerDataNetworkList[index] = data;
        }

        // All players ready?
        bool allReady = true;
        for (int i = 0; i < playerDataNetworkList.Count; i++)
        {
            if (!playerDataNetworkList[i].isReady)
            {
                allReady = false;
                break;
            }
        }

        int totalPlayers = playerDataNetworkList.Count;
        bool validPlayerCount = (totalPlayers == MAX_PLAYER_AMOUNT);

        if (allReady && validPlayerCount)
        {
            LobbyManager.Instance?.DeleteLobby();
            NetworkManager.Singleton.SceneManager.LoadScene("CardGameScene", LoadSceneMode.Single);
        }
        else if (allReady && !validPlayerCount)
        {
            Debug.LogWarning($"Cannot start: {totalPlayers} player(s) in lobby. Need exactly {MAX_PLAYER_AMOUNT}.");
        }

        TryStartGameIfReady();
    }


    public static class TeamUtils
    {
        public static int GetTeamIndex(int playerIndex) => playerIndex % 2;

    }

    public int GetTeamIndexForClient(ulong clientId)
    {
        int playerIdx = GetPlayerDataIndexFromClientId(clientId);
        return TeamUtils.GetTeamIndex(playerIdx);
    }

    public int GetReadyPlayerCount()
    {
        int count = 0;
        foreach (var pd in playerDataNetworkList)
            if (pd.isReady) count++;
        return count;
    }

    public void TryStartGameIfReady()
    {
        if (!IsHost) return;

        bool allReady = true;
        for (int i = 0; i < playerDataNetworkList.Count; i++)
        {
            if (!playerDataNetworkList[i].isReady)
            {
                allReady = false;
                break;
            }
        }

        int totalPlayers = playerDataNetworkList.Count;
        bool validPlayerCount = (totalPlayers == MAX_PLAYER_AMOUNT);

        if (allReady && validPlayerCount)
        {
            LobbyManager.Instance?.DeleteLobby();
            NetworkManager.Singleton.SceneManager.LoadScene("CardGameScene", LoadSceneMode.Single);
        }
    }


    [ServerRpc(RequireOwnership = false)]
    public void TogglePlayerReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        int index = GetPlayerDataIndexFromClientId(rpcParams.Receive.SenderClientId);
        if (index != -1)
        {
            PlayerData data = playerDataNetworkList[index];
            data.isReady = !data.isReady;
            playerDataNetworkList[index] = data;
        }

        TryStartGameIfReady();
    }

    public void Cleanup()
    {
        // Netcode events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= NetworkManager_OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_Server_OnClientDisconnectCallback;
            NetworkManager.Singleton.ConnectionApprovalCallback -= NetworkManager_ConnectionApprovalCallback;
            NetworkManager.Singleton.OnClientConnectedCallback -= NetworkManager_Client_OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_Client_OnClientDisconnectCallback;
        }

        // NetworkList events
        if (playerDataNetworkList != null)
            playerDataNetworkList.OnListChanged -= PlayerDataNetworkList_OnListChanged;


        Instance = null;
    }

    public NetworkVariable<bool> ScoutsEnabled =
    new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [ServerRpc(RequireOwnership = false)]
    public void SetScoutsEnabledServerRpc(bool enabled)
    {
        ScoutsEnabled.Value = enabled;
    }
}