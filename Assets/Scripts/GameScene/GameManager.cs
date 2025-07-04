using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public event EventHandler OnStateChanged;
    public event EventHandler OnLocalGamePaused;
    public event EventHandler OnLocalGameUnpaused;
    public event EventHandler OnMultiplayerGamePaused;
    public event EventHandler OnMultiplayerGameUnpaused;
    public event EventHandler OnLocalPlayerReadyChanged;

    private enum State
    {
        WaitingToStart,
        CountdownToStart,
        GamePlaying,
        GameOver,
    }


    [SerializeField] private Transform playerPrefab;


    private NetworkVariable<State> state = new NetworkVariable<State>(State.WaitingToStart);
    private bool isLocalPlayerReady;
    private NetworkVariable<float> countdownToStartTimer = new NetworkVariable<float>(3f);
    private NetworkVariable<float> gamePlayingTimer = new NetworkVariable<float>(0f);
    private float gamePlayingTimerMax = 90f;
    private bool isLocalGamePaused = false;
    private NetworkVariable<bool> isGamePaused = new NetworkVariable<bool>(false);
    private Dictionary<ulong, bool> playerReadyDictionary;
    private Dictionary<ulong, bool> playerPausedDictionary;
    private Dictionary<ulong, bool> tutorialReadyDictionary = new Dictionary<ulong, bool>();
    private bool hasTriggeredCountdown = false;

    private NetworkVariable<float> heartbeatTime = new NetworkVariable<float>(0f);

    [Header("Environment Prefabs")]
    [SerializeField] private GameObject forestEnvironmentPrefab;
    [SerializeField] private GameObject hellEnvironmentPrefab;
    private GameObject activeEnvironment;

    [Header("Skybox Materials")]
    [SerializeField] private Material forestSkyboxMaterial;
    [SerializeField] private Material hellSkyboxMaterial;


   
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        playerReadyDictionary = new Dictionary<ulong, bool>();
        playerPausedDictionary = new Dictionary<ulong, bool>();

        
    }


    private void Start()
    {
        // Only check for missing humans in pure PvP games
        bool isBotGame = MultiplayerManager.Instance.IsBotPresent();
        if (!isBotGame && MultiplayerManager.Instance.GetHumanPlayerCount() < 2 && !GameOverUI.GameHasEnded)
        {
            
            var gameOverUI = FindObjectOfType<GameOverUI>(true);
            if (gameOverUI != null)
            {
                // Hide blocking UIs (tutorial, countdown, disconnect UI)
                var tutorialUI = FindObjectOfType<TutorialUI>(true);
                if (tutorialUI != null) tutorialUI.gameObject.SetActive(false);
                var countdownUI = GameStartCountdownUI.Instance;
                if (countdownUI != null) countdownUI.gameObject.SetActive(false);
                var disconnectUI = FindObjectOfType<HostDisconnectUI>(true);
                if (disconnectUI != null) disconnectUI.Hide();

                gameOverUI.ShowGameOver("The other player has disconnected", -1);
                GameOverUI.GameHasEnded = true;
            }
            Time.timeScale = 0f;
        }
    }


    public override void OnNetworkSpawn()
    {
        state.OnValueChanged += State_OnValueChanged;
        isGamePaused.OnValueChanged += IsGamePaused_OnValueChanged;

        if (IsServer)
        {
            // Ensure we only subscribe ONCE!
            NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;

            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= SceneManager_OnLoadEventCompleted;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += SceneManager_OnLoadEventCompleted;
        }
        if (IsClient && !IsHost)
        {
            Debug.Log("OnNetworkSpawn: Adding HostFreezeDetector on client.");
            if (FindObjectOfType<HostFreezeDetector>() == null)
                gameObject.AddComponent<HostFreezeDetector>();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= SceneManager_OnLoadEventCompleted;
        }
    }



    [ServerRpc(RequireOwnership = false)]
    public void SetTutorialReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        tutorialReadyDictionary[clientId] = true;

        SetLocalPlayerReadyClientRpc(clientId);

        // if all players ready, start countdown
        bool allReady = true;
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!tutorialReadyDictionary.ContainsKey(id) || !tutorialReadyDictionary[id])
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            state.Value = State.CountdownToStart;
        }
    }

    [ClientRpc]
    private void SetLocalPlayerReadyClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;

        isLocalPlayerReady = true;
        OnLocalPlayerReadyChanged?.Invoke(this, EventArgs.Empty);
    }



    public bool IsPlayerTutorialReady(ulong clientId)
    {
        return tutorialReadyDictionary.ContainsKey(clientId) && tutorialReadyDictionary[clientId];
    }



    private void SceneManager_OnLoadEventCompleted(
     string sceneName,
     UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
     List<ulong> clientsCompleted,
     List<ulong> clientsTimedOut)
    {
        CleanupUIState();
        SpawnEnvironment();

        foreach (var player in FindObjectsOfType<NetworkObject>())
        {
            if (player.GetComponent<Player>() != null || player.GetComponent<Bot>() != null)
            {
                if (player.IsSpawned)
                {
                    player.Despawn(true); // true = destroy on clients
                }
                else
                {
                    Destroy(player.gameObject);
                }
            }
        }

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            Transform playerTransform = Instantiate(playerPrefab);
            playerTransform.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }

    }

    private void SpawnEnvironment()
    {
        if (!IsServer) return;

        int totalPlayers = MultiplayerManager.Instance.playerDataNetworkList.Count;
        bool useForest = (totalPlayers == 2);

        SpawnEnvironmentClientRpc(useForest);

        SetSkyboxClientRpc(useForest);

        PlayBGMClientRpc(useForest);
    }



    [ClientRpc]
    private void SpawnEnvironmentClientRpc(bool useForest)
    {
        var prefab = useForest ? forestEnvironmentPrefab : hellEnvironmentPrefab;
        if (activeEnvironment != null)
            Destroy(activeEnvironment);
        activeEnvironment = Instantiate(prefab, prefab.transform.position, prefab.transform.rotation);
    }

    [ClientRpc]
    private void PlayBGMClientRpc(bool useForest)
    {
        SoundManager.Instance?.PlayBGMByEnvironment(useForest);
    }



    [ClientRpc]
    private void SetSkyboxClientRpc(bool useForest)
    {
        if (useForest)
        {
            if (forestSkyboxMaterial != null)
                RenderSettings.skybox = forestSkyboxMaterial;
            else
                Debug.LogWarning("Forest skybox material missing!");
        }
        else
        {
            if (hellSkyboxMaterial != null)
                RenderSettings.skybox = hellSkyboxMaterial;
            else
                Debug.LogWarning(" Hell skybox material missing!");
        }
    }




    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
    {
        if (GameOverUI.GameHasEnded) return;
        var gameOverUI = FindObjectOfType<GameOverUI>(true);
        // --- Pre-game disconnect: handle if only one human left
        if (IsServer && !IsGamePlaying() && !IsGameOver())
        {
            int humansLeft = MultiplayerManager.Instance.GetHumanPlayerCount();

            if (humansLeft <= 1)
            {
                Debug.LogWarning("All clients gone, abort match and show GameOverUI.");

                HideBlockingUIs();


                if (gameOverUI != null)
                {
                    gameOverUI.ShowGameOver("The other player has disconnected", -1);
                }

                var disconnectUI = FindObjectOfType<HostDisconnectUI>(true);
                if (disconnectUI != null) disconnectUI.Hide();

                state.Value = State.GameOver;
                GameOverUI.GameHasEnded = true;

                Time.timeScale = 0f;
                return;
            }
        }

        if (!IsServer) return;
        if (!IsGamePlaying()) return;

        Debug.Log($"Client {clientId} disconnected during game!");

        var timer = FindObjectOfType<GameTimer>();
        if (timer != null)
        {
            timer.isTimerRunning = false;
        }

        if (gameOverUI != null)
        {
            gameOverUI.ShowGameOver("The other player has disconnected", -1);
        }

        GameOverUI.GameHasEnded = true;
    }


    private void HideBlockingUIs()
    {
        var tutorialUI = GameObject.FindObjectOfType<TutorialUI>(true);
        if (tutorialUI != null) tutorialUI.gameObject.SetActive(false);

        var countdownUI = GameStartCountdownUI.Instance;
        if (countdownUI != null) countdownUI.gameObject.SetActive(false);
    }

    private void ShowHostDisconnectUI()
    {

        HostDisconnectUI hostDisconnectUI = FindObjectOfType<HostDisconnectUI>(true);
        if (hostDisconnectUI != null)
        {
            hostDisconnectUI.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("HostDisconnectUI not found");
        }
    }


    private void IsGamePaused_OnValueChanged(bool previousValue, bool newValue)
    {
        if (isGamePaused.Value)
        {
            Time.timeScale = 0f;

            OnMultiplayerGamePaused?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Time.timeScale = 1f;

            OnMultiplayerGameUnpaused?.Invoke(this, EventArgs.Empty);
        }
    }

    private void State_OnValueChanged(State previousValue, State newValue)
    {
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GameInput_OnInteractAction(object sender, EventArgs e)
    {
        if (state.Value == State.WaitingToStart)
        {
            isLocalPlayerReady = true;
            OnLocalPlayerReadyChanged?.Invoke(this, EventArgs.Empty);

            SetPlayerReadyServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerReadyServerRpc(ServerRpcParams serverRpcParams = default)
    {
        playerReadyDictionary[serverRpcParams.Receive.SenderClientId] = true;

        bool allClientsReady = true;
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!playerReadyDictionary.ContainsKey(clientId) || !playerReadyDictionary[clientId])
            {
                // This player is NOT ready
                allClientsReady = false;
                break;
            }
        }

        if (allClientsReady)
        {
            state.Value = State.CountdownToStart;
        }
    }

    private void GameInput_OnPauseAction(object sender, EventArgs e)
    {
        TogglePauseGame();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsServer)
        {
            return;
        }

        heartbeatTime.Value = (float)NetworkManager.Singleton.ServerTime.Time;

        switch (state.Value)
        {
            case State.WaitingToStart:
                break;
            case State.CountdownToStart:
                countdownToStartTimer.Value -= Time.deltaTime;

                if (!hasTriggeredCountdown)
                {
                    hasTriggeredCountdown = true;
                    StartCountdownClientRpc();
                }

                if (countdownToStartTimer.Value < 0f)
                {
                    state.Value = State.GamePlaying;
                    gamePlayingTimer.Value = gamePlayingTimerMax;
                }
                break;
            case State.GamePlaying:
                gamePlayingTimer.Value -= Time.deltaTime;
                if (gamePlayingTimer.Value < 0f)
                {
                    state.Value = State.GameOver;
                }
                break;
            case State.GameOver:
                break;
        }
    }

    public bool IsGamePlaying()
    {
        return state.Value == State.GamePlaying;
    }

    public bool IsCountdownToStartActive()
    {
        return state.Value == State.CountdownToStart;
    }

    public float GetCountdownToStartTimer()
    {
        return countdownToStartTimer.Value;
    }

    public bool IsGameOver()
    {
        return state.Value == State.GameOver;
    }

    public bool IsWaitingToStart()
    {
        return state.Value == State.WaitingToStart;
    }

    public bool IsLocalPlayerReady()
    {
        return isLocalPlayerReady;
    }

    public float GetGamePlayingTimerNormalized()
    {
        return 1 - (gamePlayingTimer.Value / gamePlayingTimerMax);
    }

    public void TogglePauseGame()
    {
        isLocalGamePaused = !isLocalGamePaused;
        if (isLocalGamePaused)
        {
            PauseGameServerRpc();

            OnLocalGamePaused?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            UnpauseGameServerRpc();

            OnLocalGameUnpaused?.Invoke(this, EventArgs.Empty);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PauseGameServerRpc(ServerRpcParams serverRpcParams = default)
    {
        playerPausedDictionary[serverRpcParams.Receive.SenderClientId] = true;

        // TestGamePausedState();
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnpauseGameServerRpc(ServerRpcParams serverRpcParams = default)
    {
        playerPausedDictionary[serverRpcParams.Receive.SenderClientId] = false;
    }

    [ClientRpc]
    private void StartCountdownClientRpc()
    {
        var ui = GameStartCountdownUI.Instance;
        if (ui != null)
        {
            ui.StartCountdown();
        }
    }

    private float pausedAt = -1f;

    void OnApplicationPause(bool pause)
    {
        if (!IsHost) return;

        if (pause)
        {
            pausedAt = Time.realtimeSinceStartup;
        }
        else
        {
            if (pausedAt > 0)
            {
                float pausedDuration = Time.realtimeSinceStartup - pausedAt;
                if (pausedDuration >= 5f)
                {
                    ShowHostDisconnectedUIForSelf();
                }
                pausedAt = -1f;
            }
        }
    }

    private void CleanupUIState()
    {
        Debug.Log("CleanupUIState called in GameManager.cs)");

        GameOverUI.GameHasEnded = false;
        GamePauseUI.ResetState();
        GamePauseUI.Instance = null;

        var disconnectUI = FindObjectOfType<HostDisconnectUI>(true);
        if (disconnectUI != null)
        {
            disconnectUI.Hide();
        }

        var detector = FindObjectOfType<HostFreezeDetector>();
        if (detector != null)
        {
            Destroy(detector);
        }
    }


    private void ShowHostDisconnectedUIForSelf()
    {
        var hostDisconnectUI = FindObjectOfType<HostDisconnectUI>(true);
        if (hostDisconnectUI != null)
        {
            hostDisconnectUI.Show();
        }

        Time.timeScale = 0f;
    }

}