using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using WebSocketSharp;

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

            // 👇 Fire update event manually so UI refreshes immediately
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

            // 👇 Trigger UI refresh
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
        if (SceneManager.GetActiveScene().name != Loader.Scene.CharacterSelectScene.ToString())
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
        if (GameOverUI.GameHasEnded)
        {
            return;
        }

        var gameOverUI = GameObject.FindObjectOfType<GameOverUI>(true);
        if (gameOverUI != null)
        {
            var tutorialUI = GameObject.FindObjectOfType<TutorialUI>(true);
            if (tutorialUI != null) tutorialUI.gameObject.SetActive(false);

            var disconnectUI = GameObject.FindObjectOfType<HostDisconnectUI>(true);
            if (disconnectUI != null) disconnectUI.Hide();

            gameOverUI.ShowGameOver("The other player has disconnected", -1);
            GameOverUI.GameHasEnded = true;
            return;
        }

        // Fallback if GameOverUI is not found
        HostDisconnectUI hostDisconnectUI = FindObjectOfType<HostDisconnectUI>(true);
        if (hostDisconnectUI != null)
        {
            hostDisconnectUI.gameObject.SetActive(true);
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
        Debug.Log("ADDING BOTS");
        if (playerDataNetworkList.Count >= MAX_PLAYER_AMOUNT)
            return;

        ulong botClientId = BOT_CLIENT_ID;
        HashSet<ulong> usedIds = new HashSet<ulong>();
        foreach (var pd in playerDataNetworkList)
            usedIds.Add(pd.clientId);

        while (usedIds.Contains(botClientId) && botClientId > 9000)
            botClientId--;

        if (usedIds.Contains(botClientId))
            return;

        HashSet<int> usedNumbers = new HashSet<int>();
        foreach (var pd in playerDataNetworkList)
        {
            if (pd.clientId >= 9000)
            {
                string nameStr = pd.playerName.ToString();
                if (nameStr.StartsWith("Bot "))
                {
                    if (int.TryParse(nameStr.Substring(4), out int n))
                        usedNumbers.Add(n);
                }
            }
        }

        int botNumber = 1;
        while (usedNumbers.Contains(botNumber))
            botNumber++;

        int randomAvatarIndex = UnityEngine.Random.Range(0, 14);

        PlayerData botData = new PlayerData
        {
            clientId = botClientId,
            playerName = $"Bot {botNumber}",
            playerId = $"bot-id-{botNumber}",
            isReady = true,
            avatarIndex = randomAvatarIndex
        };


        playerDataNetworkList.Add(botData);
    }

    public ulong GetNextBotClientId()
    {
        ulong id = BOT_CLIENT_ID;
        HashSet<ulong> used = new HashSet<ulong>();
        foreach (var pd in playerDataNetworkList)
            used.Add(pd.clientId);

        while (used.Contains(id) && id > 9000) id--;
        return id;
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
        {
            if (player.clientId != BOT_CLIENT_ID)
                count++;
        }
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

        // Even player count? 2? 4?
        int totalPlayers = playerDataNetworkList.Count;
        bool validPlayerCount = (totalPlayers == 2 || totalPlayers == 4);

        if (allReady && validPlayerCount)
        {
            LobbyManager.Instance?.DeleteLobby();

            NetworkManager.Singleton.SceneManager.LoadScene("CardGameScene", LoadSceneMode.Single);
        }
        else if (allReady && !validPlayerCount)
        {
            Debug.LogWarning($"Cannot start: Only {totalPlayers} player(s) ready! Game only supports 1v1 or 2v2");
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

    private int GetTotalHumanPlayers()
    {
        int count = 0;
        foreach (var pd in playerDataNetworkList)
            if (!IsBot(pd.clientId)) count++;
        return count;
    }

    private bool IsBot(ulong clientId) => clientId >= 9000;


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
        bool validPlayerCount = (totalPlayers == 2 || totalPlayers == 4);

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


}