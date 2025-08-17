using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GamePlayManager : NetworkBehaviour
{
    [Header("Sound")]
    public AudioClip music_win_clip;
    public AudioClip music_loss_clip;
    public AudioClip draw_card_clip;
    public AudioClip throw_card_clip;
    public AudioClip normal_click;
    public AudioClip special_click;
    public AudioClip exposed;
    public AudioClip choose_color_clip;

    public float cardDealTime = 3f;
    public Card _cardPrefab;
    public Transform cardDeckTransform;
    public Image cardWastePile;
    public GameObject playerCardsPanel;
    public GameObject arrowObject, arrowObject2, unoBtn, cardDeckBtn;
    public Popup colorChoose, playerChoose, noNetwork;
    public GameObject loadingView, rayCastBlocker;
    public Animator cardEffectAnimator;
    public ParticleSystem wildCardParticle;
    public GameObject menuButton;
    public int previousPlayerIndex = -1;
    public Coroutine turnTimeoutCoroutine;
    
    private bool isPeekingPhase = false;
    public float peekTime = 6f;
    public bool wasteInteractionStarted = false;

    [Header("Player Setting")]
    public List<Player2> players;
    public TextAsset multiplayerNames;
    public TextAsset computerProfiles;
    public bool clockwiseTurn = true;
    public int currentPlayerIndex = 0;
    public Player2 CurrentPlayer { get { return players[currentPlayerIndex]; } }

    [Header("Game Over")]
    public GameObject gameOverPopup;
    public ParticleSystem starParticle;
    public List<GameObject> playerObject;
    public GameObject loseTimerAnimation;
    public GameObject screenCanvas;

    public List<SerializableCard> cards;
    public List<SerializableCard> wasteCards;
    private int peekedDeckIndex = -1;
    private Coroutine turnTimerCoroutine;
    public float turnTimerDuration = 10f;
    public float turnTimerLeft = 0f;
    public bool isTurnEnding = false;
    public bool deckInteractionLocked = false;
    public bool isKingRefillPhase = false;

    public Card peekedCard = null;
    public bool hasPeekedCard = false;
    public Dictionary<ulong, SerializableCard> peekedCardsByClientId = new Dictionary<ulong, SerializableCard>();

    [Header("Special Card Audio")]
    public AudioClip jackVoiceClip;
    public AudioClip queenVoiceClip;
    public AudioClip kingVoiceClip;
    public AudioClip fiendVoiceClip;
    public AudioClip nemesisVoiceClip;
    public AudioClip fiendRevengeVoiceClip;
    public AudioClip jackSpecialVoiceClip;
    public AudioClip goldenJackVoiceClip;
    public AudioClip goldenJackRevengeVoiceClip;

    public AudioSource _audioSource;

    private HashSet<ulong> readyClientIds = new HashSet<ulong>();

    public WinnerUI winnerUI;
    public TMPro.TextMeshProUGUI remainingCardsCounterText;
    private bool turnEndedByTimeout = false;

    [Header("Lucky Wheel")]
    public WheelSpinUI wheelUI;
    public float wheelSpinDuration = 3f;
    private int pendingStartGlobalIndex = 0;

    [Header("Bot AI Tuning")]
    [Tooltip("Bot will only consider waste pile if card value is <= this value.")]
    public int botWastePileMinValue = 3;

    [Tooltip("Chance (percent) for bot to draw a minimum card from waste pile (0-100).")]
    [Range(0, 100)]
    public int botWastePileDrawChance = 20;

    [Tooltip("Chance (percent) for bot to do a replace after deck draw (0-100).")]
    [Range(0, 100)]
    public int botDeckReplaceChance = 20;

    [Header("Superbot AI Tuning")]
    [Tooltip("Chance (%) that a Superbot replaces from DECK if the drawn numeric card is lower than one in its hand.")]
    [Range(0, 100)] public int superbotDeckSmartReplaceChance = 100;

    [Tooltip("Chance (%) that a Superbot replaces from WASTE if the top numeric card is lower than one in its hand.")]
    [Range(0, 100)] public int superbotWasteSmartReplaceChance = 100;

    [Header("Special Avatar Sprites")]
    public Sprite avatarJack;
    public Sprite avatarQueen;
    public Sprite avatarKing;
    public Sprite avatarFiend;
    public Sprite avatarGoldenJack;
    public Sprite avatarNemesis;
    [Tooltip("How long the power avatar stays before reverting")]
    public float specialAvatarDuration = 2f;
    private List<Sprite> baseAvatarSprites;

    private Dictionary<int, Coroutine> avatarRevertBySeat = new Dictionary<int, Coroutine>();

    public int currentPowerOwnerGlobalSeat = -1;
    public CardValue currentPowerValue = 0;
    private Coroutine safetyEndCoroutine;
    private Coroutine hostHeartbeatCo;
    private Coroutine clientWatchdogCo;
    private float lastHostBeatTime = -1f;
    [SerializeField] private float hostBeatInterval = 0.5f;
    [SerializeField] private float hostFreezeThreshold = 1.6f;
    [SerializeField] private float hostGoneThreshold = 8f;
    private bool loadingShown = false;
    private System.DateTime? _hostPausedAtUtc;
    [SerializeField] private float hostAwayAssumeDeadSeconds = 20f;
    private bool _serverTurnResolved = false;
    private bool _gameStarted = false;
    public bool _peekPhaseStarted = false;
    public bool _wheelPhaseStarted = false;
    public bool _hostWheelInProgress = false;
    private Coroutine _hostAfterWheelCo;
    private Coroutine _wheelSpinRoutine;

    [Header("Good Kill SFX")]
    public AudioClip goodKill; 
    public AudioClip goodKillEnhanced;

    [Header("Spotlight Celebration")]
    public GameObject spotlightPanel;
    public ParticleSystem spotlightConfetti;
    public GameObject spotlight;
    public Transform[] spotlightTargets;
    [Tooltip("Degrees to add after aiming (0 if the art points straight up at Z=0).")]
    public float spotlightZArtOffset = 0f;
    [Header("UI Animation")]
    public Transform cardAnimRoot;

    private Transform AnimRoot => cardAnimRoot != null ? cardAnimRoot : this.transform;

    [Header("Tooltips")]
    public GameObject tooltipParent;
    [SerializeField] private Button tooltipDimmedButton;
    public TMPro.TextMeshProUGUI tooltipText;
    [SerializeField] private Button tooltipPopupButton;
    [SerializeField] private CanvasGroup tooltipCanvasGroup;
    [SerializeField] private float fadeDuration = 0.25f;
    private Coroutine tooltipFadeRoutine;

    private int _turnSerial = 0;
    private Coroutine _pendingAdvanceCo;
    public int CurrentTurnSerial => _turnSerial;

    public static bool GameHasEnded { get; private set; } = false;
    public static void ResetGameHasEnded()
    {
        GameHasEnded = false;
    }

    void OnApplicationPause(bool paused)
    {
        if (!IsHost) return;

        if (paused)
        {
            if (_hostPausedAtUtc == null) _hostPausedAtUtc = System.DateTime.UtcNow;
        }
        else
        {
            CheckHostLongPauseAndEnd();
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // return if unity editor
        if (Application.isEditor) return;
        if (!IsHost) return;

        if (!hasFocus)
        {
            if (_hostPausedAtUtc == null) _hostPausedAtUtc = System.DateTime.UtcNow;
        }
        else
        {
            CheckHostLongPauseAndEnd();
        }
    }

    private void CheckHostLongPauseAndEnd()
    {
        if (_hostPausedAtUtc == null) return;

        double awaySec = (System.DateTime.UtcNow - _hostPausedAtUtc.Value).TotalSeconds;
        _hostPausedAtUtc = null;

        if (awaySec >= hostAwayAssumeDeadSeconds)
        {
            AssumeSessionDeadLocally($"[Host] Away {awaySec:0.0}s ≥ {hostAwayAssumeDeadSeconds}s — ending host session.");
        }
    }

    private void AssumeSessionDeadLocally(string reason)
    {
        Debug.LogWarning(reason);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        var ui = FindObjectOfType<DisconnectUI>(true);
        if (ui != null) ui.Show();
    }

    public CardType CurrentType
    {
        get { return _currentType; }
        set { _currentType = value; cardWastePile.color = value.GetColor(); }
    }

    public CardValue CurrentValue
    {
        get { return _currentValue; }
        set { _currentValue = value; }
    }

    [SerializeField] CardType _currentType;
    [SerializeField] CardValue _currentValue;

    public static GamePlayManager instance;


    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.spatialBlend = 0f;

        if (tooltipParent != null)
        {
            if (tooltipCanvasGroup == null)
                tooltipCanvasGroup = tooltipParent.GetComponent<CanvasGroup>() ?? tooltipParent.AddComponent<CanvasGroup>();

            tooltipCanvasGroup.alpha = 0f;
            tooltipParent.SetActive(false);
            WireTooltipClickToClose();
        }
    }

    void Start()
    {
        Application.targetFrameRate = 60;
        instance = this;
        Input.multiTouchEnabled = false;
        previousPlayerIndex = -1;

        

        if (tooltipParent != null) tooltipParent.SetActive(false);
        WireTooltipClickToClose();

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < playerList.Count; i++)
            Debug.Log($"[SeatSetup] Global seat {i}: clientId={playerList[i].clientId} (isMine={NetworkManager.Singleton.LocalClientId == playerList[i].clientId})");
    }

    public override void OnNetworkSpawn()
    {
        SetupAllPlayerPanels();

        if (IsHost)
        {
            if (hostHeartbeatCo != null) StopCoroutine(hostHeartbeatCo);
            hostHeartbeatCo = StartCoroutine(HostHeartbeatRoutine());

            lastHostBeatTime = Time.unscaledTime;

            readyClientIds.Add(NetworkManager.Singleton.LocalClientId);
            if (readyClientIds.Count == MultiplayerManager.Instance.playerDataNetworkList.Count)
                StartMultiplayerGame();
        }
        else
        {
            lastHostBeatTime = Time.unscaledTime;
            if (clientWatchdogCo != null) StopCoroutine(clientWatchdogCo);
            clientWatchdogCo = StartCoroutine(ClientWatchdogRoutine());

            NotifyReadyServerRpc();
        }
    }

    private IEnumerator HostHeartbeatRoutine()
    {
        var wait = new WaitForSecondsRealtime(hostBeatInterval);
        while (NetworkManager != null && NetworkManager.IsListening && IsHost)
        {
            var targets = GetAllHumanClientIds();
            if (targets.Count > 0)
            {
                HeartbeatClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets }
                });
            }
            yield return wait;
        }
    }

    [ClientRpc]
    private void HeartbeatClientRpc(ClientRpcParams rpcParams = default)
    {
        if (IsHost) return; // host ignores
        lastHostBeatTime = Time.unscaledTime;

        if (loadingShown) HideLoadingUI();
    }

    private IEnumerator ClientWatchdogRoutine()
    {
        var wait = new WaitForSecondsRealtime(0.1f);
        while (NetworkManager != null && NetworkManager.IsListening && !IsHost)
        {
            float dt = Time.unscaledTime - lastHostBeatTime;

            if (dt > hostFreezeThreshold && !loadingShown) ShowLoadingUI();
            else if (dt <= hostFreezeThreshold && loadingShown) HideLoadingUI();

            if (dt > hostGoneThreshold)
            {
                // Optional: give it a little more grace before declaring death
                const float hardAssumeDeadAfter = 20f; // tune this as you like
                if (dt > hardAssumeDeadAfter)
                {
                    ForceAssumeDisconnected("[Watchdog] No host heartbeat for too long; forcing local disconnect.");
                    yield break;
                }
            }

            yield return wait;
        }
    }

    private void ForceAssumeDisconnected(string reason)
    {
        Debug.LogWarning(reason);

        HideLoadingUI();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        var ui = FindObjectOfType<DisconnectUI>(true);
        if (ui != null) ui.Show();
    }

    private void ShowLoadingUI()
    {
        if (loadingView != null) loadingView.SetActive(true);
        if (rayCastBlocker != null) rayCastBlocker.SetActive(true);
        loadingShown = true;
    }

    private void HideLoadingUI()
    {
        if (loadingView != null) loadingView.SetActive(false);
        if (rayCastBlocker != null) rayCastBlocker.SetActive(false);
        loadingShown = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void NotifyReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        readyClientIds.Add(senderId);

        if (readyClientIds.Count == MultiplayerManager.Instance.playerDataNetworkList.Count)
        {
            StartMultiplayerGame();
        }
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "CardGameScene")
        {
            Debug.Log("[GamePlayManager] Scene loaded! Setting up player panels.");
            SetupNetworkedPlayerSeats();
        }
    }

    public void SetupAllPlayerPanels()
    {
        Transform playersRoot = screenCanvas.transform.Find("Players");
        players = new List<Player2>();

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < playerList.Count; i++)
        {
            var panel = playersRoot.Find($"PlayerPanel_{i + 1}");
            if (panel == null)
            {
                Debug.LogError($"PlayerPanel_{i + 1} not found in Players!");
            }
            var p2 = panel.GetComponent<Player2>();
            if (p2 == null)
            {
                Debug.LogError($"PlayerPanel_{i + 1} is missing Player2!");
            }
            if (playerList[i].clientId >= 9000)
                panel.gameObject.SetActive(false);
            else
                panel.gameObject.SetActive(true);

            players.Add(p2);
        }


    }

    public List<ulong> seatOrderGlobal = new List<ulong>();

    public void StartMultiplayerGame()
    {
        if (_gameStarted) { Debug.Log("[Game] StartMultiplayerGame called again; skipping."); return; }
        _gameStarted = true;

        _peekPhaseStarted = false;
        _wheelPhaseStarted = false;
        _hostWheelInProgress = false;
        if (GameHasEnded) return;
        if (IsHost)
        {
            // Freeze the seat order once
            seatOrderGlobal.Clear();
            foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
                seatOrderGlobal.Add(pd.clientId);
        }
        menuButton.SetActive(true);
        if (cardDeckTransform != null)
            cardDeckTransform.gameObject.SetActive(true);
        if (cardWastePile != null)
            cardWastePile.gameObject.SetActive(true);

        if (!IsHost) return;

        currentPlayerIndex = Random.Range(0, players.Count);

        CreateDeck();
        cards.Shuffle();

        int cardsPerPlayer = 3;
        int playerCount = players.Count;
        int needed = playerCount * cardsPerPlayer;
        if (cards.Count < needed)
        {
            Debug.LogError($"[StartMultiplayerGame] Not enough cards in deck! Need {needed}, have {cards.Count}. ABORT DEAL.");
            return;
        }

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        ulong[] clientIds = new ulong[playerCount];
        SerializableCard[] allCards = new SerializableCard[needed];

        for (int playerIdx = 0; playerIdx < playerCount; playerIdx++)
        {
            clientIds[playerIdx] = playerList[playerIdx].clientId;
            for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
            {
                int index = playerIdx * cardsPerPlayer + cardSlot;
                allCards[index] = cards[0];
                cards.RemoveAt(0);
            }
        }

        for (int i = 0; i < allCards.Length; i++)
        {
            if (IsCardDefault(allCards[i]))
                Debug.LogError($"[ASSERT] allCards[{i}] is default! This will cause missing cards.");
        }


        DealCardsClientRpc(clientIds, allCards, cardsPerPlayer, playerCount);

        UpdateDeckVisualClientRpc(cards.ToArray());
    }

    [ClientRpc]
    void DealCardsClientRpc(ulong[] clientIds, SerializableCard[] allCardsFlat, int cardsPerPlayer, int playerCount)
    {
        seatOrderGlobal = new List<ulong>(clientIds);
        for (int globalSeat = 0; globalSeat < playerCount; globalSeat++)
        {
            ulong targetClientId = clientIds[globalSeat];
            int localSeat = MapClientIdToLocalSeat(targetClientId);

            if (localSeat < 0 || localSeat >= players.Count)
            {
                Debug.LogError($"[DealCardsClientRpc] Skipping invalid seat: localSeat={localSeat}, players.Count={players.Count}");
                continue;
            }

            var hand = new List<SerializableCard>();
            for (int c = 0; c < cardsPerPlayer; c++)
                hand.Add(allCardsFlat[globalSeat * cardsPerPlayer + c]);

            AssignHandToSeat(localSeat, hand);
        }

        for (int seat = 0; seat < playerCount; seat++)
            players[seat].cardsPanel.UpdatePos();

        if (!_peekPhaseStarted)
        {
            _peekPhaseStarted = true;
            StartCoroutine(StartPeekingPhase());
        }
        else
        {
            Debug.Log("[Peek] StartPeekingPhase called again; skipping.");
        }
    }

    public List<ulong> GetAllHumanClientIds()
    {
        var ids = new List<ulong>();
        foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
            if (pd.clientId < 9000) ids.Add(pd.clientId);
        return ids;
    }

    void BeginLuckyWheelPhase()
    {
        if (_wheelPhaseStarted)
        {
            Debug.Log("[Wheel] BeginLuckyWheelPhase called again; skipping.");
            return;
        }
        _wheelPhaseStarted = true;

        if (screenCanvas != null)
        {
            var disconnectUI = screenCanvas.GetComponentInChildren<DisconnectUI>();
            if (disconnectUI != null && disconnectUI.gameObject.activeSelf)
            {
                Debug.LogWarning("[GamePlayManager] Disconnect UI is active, skipping Lucky Wheel phase.");
                _wheelPhaseStarted = false; // allow later retry
                return;
            }
        }

        if (wheelUI == null)
        {
            if (IsHost)
            {
                pendingStartGlobalIndex = Random.Range(0, seatOrderGlobal.Count);
                StartPlayerTurnForAllClientRpc(pendingStartGlobalIndex);
            }
            _wheelPhaseStarted = false; // nothing to show
            return;
        }

        Debug.Log("[Wheel] BeginLuckyWheelPhase starting.");
        BuildWheelVisualsLocal();
        wheelUI.Show();

        if (IsHost) HostPickWinnerAndSpin();
    }

    void HostPickWinnerAndSpin()
    {
        if (_hostWheelInProgress)
        {
            Debug.Log("[Wheel] HostPickWinnerAndSpin reentry; skipping.");
            return;
        }
        _hostWheelInProgress = true;

        int pCount = seatOrderGlobal.Count;
        if (pCount == 0) { _hostWheelInProgress = false; _wheelPhaseStarted = false; return; }

        int winnerGlobalSeat = Random.Range(0, pCount);

        var candidatePockets = new List<int>(8);
        for (int i = 0; i < 8; i++)
            if (i % pCount == winnerGlobalSeat) candidatePockets.Add(i);

        int spinSeed = Random.Range(int.MinValue / 2, int.MaxValue / 2);
        int chosenPocketIndex = candidatePockets[Mathf.Abs(spinSeed) % candidatePockets.Count];
        int extraSpins = Random.Range(3, 5);

        float slice = 360f / 8f;
        float pocketCenter = chosenPocketIndex * slice;
        float finalZ = wheelUI.pointerOffsetDegrees + pocketCenter + extraSpins * 360f;

        pendingStartGlobalIndex = winnerGlobalSeat;

        SpinWheelAbsoluteClientRpc(winnerGlobalSeat, finalZ, wheelSpinDuration);

        if (_hostAfterWheelCo != null) StopCoroutine(_hostAfterWheelCo);
        _hostAfterWheelCo = StartCoroutine(HostAfterWheelRoutine(wheelSpinDuration, winnerGlobalSeat));
    }

    

    [ClientRpc]
    void SpinWheelAbsoluteClientRpc(int winnerGlobalSeat, float finalZ, float duration)
    {
        if (wheelUI == null) return;
        if (_wheelSpinRoutine != null) { StopCoroutine(_wheelSpinRoutine); _wheelSpinRoutine = null; }
        _wheelSpinRoutine = StartCoroutine(SpinWheelToAngle(finalZ, duration));
    }

    [ClientRpc]
    void HideWheelClientRpc()
    {
        if (_wheelSpinRoutine != null) { StopCoroutine(_wheelSpinRoutine); _wheelSpinRoutine = null; }
        if (wheelUI != null)
        {
            wheelUI.OnPocketTick = null; // clear callback to be safe
            wheelUI.Hide();
        }
    }

    IEnumerator SpinWheelToAngle(float finalZ, float duration)
    {
        if (!wheelUI.gameObject.activeInHierarchy) wheelUI.Show();
        yield return null;

        wheelUI.OnPocketTick = () =>
        {
            CardGameManager.PlaySound(GamePlayManager.instance.throw_card_clip);
        };

        yield return wheelUI.SpinTo(finalZ, duration);
    }

    private IEnumerator HostAfterWheelRoutine(float delay, int winnerGlobalSeat)
    {
        yield return new WaitForSeconds(delay + 0.05f);

        ulong winnerCid = GetClientIdFromGlobalSeat(winnerGlobalSeat);

        // Targeted + local guard in RPC
        var targets = new List<ulong>();
        if (winnerCid < 9000) targets.Add(winnerCid);

        if (targets.Count > 0)
        {
            Debug.Log($"[Confetti] Host sending to {winnerCid}");
            PlayWinnerConfettiClientRpc(
                winnerCid,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
            );
        }
        else if (winnerCid == NetworkManager.ServerClientId)
        {
            // Winner is the host: play locally without RPC (optional)
            Debug.Log("[Confetti] Host is winner; playing locally.");
            if (wheelUI != null) wheelUI.PlayLocalWinFX();
        }

        yield return new WaitForSeconds(2f);
        HideWheelClientRpc();

        _hostWheelInProgress = false;
        _wheelPhaseStarted = false; // allow future wheel phases

        StartPlayerTurnForAllClientRpc(winnerGlobalSeat);

        if (winnerCid >= 9000)
            StartCoroutine(RunBotTurn(winnerGlobalSeat));
    }

    void BuildWheelVisualsLocal()
    {
        int pCount = seatOrderGlobal.Count;

        for (int pocket = 0; pocket < 8; pocket++)
        {
            int globalSeat = pocket % pCount;
            int localSeat = GetLocalIndexFromGlobal(globalSeat);

            var p2 = (localSeat >= 0 && localSeat < players.Count) ? players[localSeat] : null;
            var sprite = (p2 != null && p2.avatarImage != null) ? p2.avatarImage.sprite : null;
            string label = (p2 != null) ? p2.playerName : $"Player {globalSeat + 1}";

            wheelUI.SetPocket(pocket, sprite, label);
        }
    }

    void AssignHandToSeat(int localSeat, List<SerializableCard> hand)
    {
        var player = players[localSeat];
        player.cardsPanel.Clear();

        for (int j = 0; j < 3; j++)
        {
            Card card = null;
            if (j < hand.Count && !IsCardDefault(hand[j]))

            {
                var sc = hand[j];
                card = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, player.cardsPanel.transform);
                card.Type = sc.Type;
                card.Value = sc.Value;
                card.IsOpen = (localSeat == 0);
                card.CalcPoint();
                card.name = $"{sc.Type}_{sc.Value}";
                card.localSeat = localSeat;
                card.cardIndex = j;
                card.IsClickable = true;
                card.onClick = null;
                CardGameManager.PlaySound(throw_card_clip);
            }
            player.AddCard(card, j);
        }
    }

    bool IsCardDefault(SerializableCard sc)
    {
        return sc.Equals(default(SerializableCard));
    }

    public void SetupNetworkedPlayerSeats()
    {
        for (int i = 0; i < players.Count; i++)
        {
            players[i].gameObject.SetActive(true);
            players[i].CardPanelBG.SetActive(true);
        }

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int totalPlayers = playerList.Count;
        int myIndex = MultiplayerManager.Instance.GetPlayerDataIndexFromClientId(Unity.Netcode.NetworkManager.Singleton.LocalClientId);

        for (int seat = 0; seat < totalPlayers; seat++)
        {
            int dataIndex = (myIndex + seat) % totalPlayers;
            PlayerData pd = playerList[dataIndex];
            Player2 p2 = players[seat];

            p2.SetAvatarProfile(new AvatarProfile
            {
                avatarIndex = pd.avatarIndex,
                avatarName = pd.playerName.ToString()
            });
            p2.isUserPlayer = (pd.clientId == NetworkManager.Singleton.LocalClientId);
        }

        if (baseAvatarSprites == null || baseAvatarSprites.Count != players.Count)
            baseAvatarSprites = new List<Sprite>(new Sprite[players.Count]);

        for (int seat = 0; seat < players.Count; seat++)
        {
            var p2 = players[seat];
            if (p2 != null && p2.avatarImage != null)
                baseAvatarSprites[seat] = p2.avatarImage.sprite; // store normal avatar for revert
        }
    }

    [ClientRpc]
    void BeginPowerAvatarClientRpc(int globalPlayerIndex, int powerValue, ClientRpcParams rpcParams = default)
    {
        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex < 0 || localIndex >= players.Count) return;
        var p = players[localIndex]; if (p?.avatarImage == null) return;

        Sprite s = null;
        switch ((CardValue)powerValue)
        {
            case CardValue.Jack: s = avatarJack; break;
            case CardValue.Queen: s = avatarQueen; break;
            case CardValue.King: s = avatarKing; break;
            case CardValue.Fiend: s = avatarFiend; break;
            case CardValue.GoldenJack: s = avatarGoldenJack; break;
            case CardValue.Nemesis: s = avatarNemesis; break;
        }
        if (s == null) return;

        // apply
        p.avatarImage.sprite = s;

        // stop any pending revert coroutine for this seat
        if (avatarRevertBySeat.TryGetValue(localIndex, out var running) && running != null)
            StopCoroutine(running);
        avatarRevertBySeat[localIndex] = null;
    }

    [ClientRpc]
    public void EndPowerAvatarClientRpc(int globalPlayerIndex, ClientRpcParams rpcParams = default)
    {
        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex < 0 || localIndex >= players.Count) return;
        var p = players[localIndex]; if (p?.avatarImage == null) return;

        if (baseAvatarSprites != null && localIndex < baseAvatarSprites.Count && baseAvatarSprites[localIndex] != null)
            p.avatarImage.sprite = baseAvatarSprites[localIndex];

        if (avatarRevertBySeat.TryGetValue(localIndex, out var running) && running != null)
            StopCoroutine(running);
        avatarRevertBySeat[localIndex] = null;
    }

    IEnumerator SafetyEndPowerAvatarAfter(int globalSeat, float softSeconds, float hardCapSeconds = 20f)
    {
        if (safetyEndCoroutine != null) StopCoroutine(safetyEndCoroutine);
        safetyEndCoroutine = StartCoroutine(_());
        IEnumerator _()
        {
            float waited = 0f;
            var wait = new WaitForSecondsRealtime(0.1f); // unaffected by timeScale/timer UI freezes

            while (waited < hardCapSeconds)
            {
                bool jackBusy = (Jack.Instance != null) && Jack.Instance.IsPowerFlowActive;

                if (!jackBusy && waited >= softSeconds) break;

                waited += 0.1f;
                yield return wait;
            }

            // Make sure we still intend to end this same seat’s power
            if (currentPowerOwnerGlobalSeat == globalSeat)
            {
                var targets = GetAllHumanClientIds();
                if (targets.Count > 0)
                    EndPowerAvatarClientRpc(globalSeat, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = targets }
                    });
                currentPowerOwnerGlobalSeat = -1;
            }

            safetyEndCoroutine = null;
        }
        yield break;
    }

    public void EndAvatarForSeatFromServer(int globalSeat)
    {
        if (!IsHost) return;
        var targets = new List<ulong>();
        foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
            if (pd.clientId < 9000) targets.Add(pd.clientId);

        if (targets.Count > 0)
            EndPowerAvatarClientRpc(
                globalSeat,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
            );

        if (currentPowerOwnerGlobalSeat == globalSeat)
            currentPowerOwnerGlobalSeat = -1;
    }

    public void CreateDeck()
    {
        cards = new List<SerializableCard>();
        wasteCards = new List<SerializableCard>();

        List<CardValue> allValues = new List<CardValue>
        {
             CardValue.Ten,
            CardValue.Jack, CardValue.Queen, CardValue.King, CardValue.Fiend
        };

        // purple only have King, Queen, Jack
        List<CardValue> purpleValues = new List<CardValue>
        {
            CardValue.Jack, CardValue.Queen, CardValue.King
        };

        // Red, Yellow, Green, Blue
        for (int j = 0; j < 4; j++)
        {
            foreach (var val in allValues)
            {
                var card = new SerializableCard((CardType)j, val);
                cards.Add(card);
            }
        }

        foreach (var val in purpleValues)
        {
            var card = new SerializableCard(CardType.Purple, val);
            cards.Add(card);
        }


        cards.Add(new SerializableCard(CardType.Gold, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Gold, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Gold, CardValue.GoldenJack));
        cards.Add(new SerializableCard(CardType.Gold, CardValue.GoldenJack));
        cards.Add(new SerializableCard(CardType.Gold, CardValue.GoldenJack));
        cards.Add(new SerializableCard(CardType.AntiMatter, CardValue.Nemesis));
        cards.Add(new SerializableCard(CardType.AntiMatter, CardValue.Nemesis));
        cards.Add(new SerializableCard(CardType.AntiMatter, CardValue.Nemesis));
        cards.Add(new SerializableCard(CardType.AntiMatter, CardValue.Nemesis));
        cards.Add(new SerializableCard(CardType.AntiMatter, CardValue.Nemesis));
        cards.Add(new SerializableCard(CardType.AntiMatter, CardValue.Nemesis));

        Debug.Log($"[CreateDeck] Deck created with {cards.Count} cards.");
        UpdateRemainingCardsCounter();
    }

    public void BeginTemporaryAvatarFromServer(int globalPlayerIndex, CardValue power, float? durationOverride = null)
    {
        if (!IsHost) return;

        currentPowerOwnerGlobalSeat = globalPlayerIndex;
        currentPowerValue = power;

        var targets = GetAllHumanClientIds();
        if (targets.Count == 0) return;

        BeginPowerAvatarClientRpc(
            globalPlayerIndex,
            (int)power,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
        );

        float dur = durationOverride ?? specialAvatarDuration;
        StartCoroutine(SafetyEndPowerAvatarAfter(globalPlayerIndex, dur));
    }

    private IEnumerator StartPeekingPhase()
    {
        isPeekingPhase = true;

        yield return null;
        while ((loadingView != null && loadingView.activeInHierarchy)
             || tooltipParent == null
             || tooltipText == null
             || tooltipCanvasGroup == null)
        {
            // Try to self-heal refs if you like:
            if (tooltipParent != null && tooltipCanvasGroup == null)
                tooltipCanvasGroup = tooltipParent.GetComponent<CanvasGroup>() ?? tooltipParent.AddComponent<CanvasGroup>();
            yield return null;
        }
        ShowTooltipOverlay("The Goal of this game is to have the lowest card values in your hands! Start by looking at your LEFT and RIGHT hand cards!");
        int localPlayerIndex = 0;
        var myCards = players[localPlayerIndex].cardsPanel.cards;
        players[localPlayerIndex].ShowMessage("Peek Time", false, peekTime);

        arrowObject.SetActive(false);
        arrowObject2.SetActive(false);
        DisableDeckClickability();
        for (int i = 0; i < cardDeckTransform.childCount; i++)
        {
            var card = cardDeckTransform.GetChild(i).GetComponent<Card>();
            if (card != null)
            {
                card.IsClickable = false;
                card.onClick = null;
            }
        }

        for (int i = 0; i < myCards.Count; i++)
        {
            var card = myCards[i];
            if (card == null) continue;
            bool canPeek = (i == 0 || i == 2);
            card.IsClickable = canPeek;
            card.PeekMode = canPeek;
            card.IsOpen = false;
            card.onClick = null;

            card.ShowGlow(canPeek);

            if (canPeek)
            {
                card.onClick = (c) =>
                {
                    if (c.PeekMode && c.IsClickable)
                    {
                        c.IsOpen = true;
                        c.ShowGlow(false);
                    }
                };
            }
            else
            {
                card.ShowGlow(false);
            }
        }

        for (int pi = 1; pi < players.Count; pi++)
        {
            foreach (var card in players[pi].cardsPanel.cards)
            {
                if (card == null) continue;
                card.IsClickable = false;
                card.PeekMode = false;
                card.onClick = null;
            }
        }

        yield return new WaitForSeconds(peekTime);

        for (int i = 0; i < myCards.Count; i++)
        {
            var card = myCards[i];
            if (card == null) continue;
            card.IsOpen = false;
            card.IsClickable = false;
            card.PeekMode = false;
            card.onClick = null;
            card.ShowGlow(false);
        }

        isPeekingPhase = false;
        BeginLuckyWheelPhase();
    }

    public void DisableDeckClickability()
    {
        arrowObject.SetActive(false);
        arrowObject2.SetActive(false);
        int n = cardDeckTransform.childCount;
        for (int i = 0; i < n; i++)
        {
            Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
            if (c == null) continue;
            c.IsClickable = false;
            c.onClick = null;
        }
    }

    private int MyGlobalSeat()
    {
        if (seatOrderGlobal == null || seatOrderGlobal.Count == 0) return 0;
        ulong myId = NetworkManager.Singleton.LocalClientId;
        return seatOrderGlobal.IndexOf(myId);
    }

    public int GetGlobalIndexFromLocal(int localIndex)
    {
        int n = seatOrderGlobal.Count;
        if (n == 0) return 0;
        return (MyGlobalSeat() + localIndex) % n;
    }

    public int GetLocalIndexFromGlobal(int globalIndex)
    {
        int n = seatOrderGlobal.Count;
        if (n == 0) return 0;
        return (globalIndex - MyGlobalSeat() + n) % n;
    }

    int MapClientIdToLocalSeat(ulong clientId)
    {
        int global = seatOrderGlobal.IndexOf(clientId);
        if (global < 0) return -1;
        return GetLocalIndexFromGlobal(global);
    }

    public bool IsMyTurn()
    {
        if (seatOrderGlobal.Count == 0) return false;
        int globalTurn = GetGlobalIndexFromLocal(currentPlayerIndex);
        ulong turnCid = seatOrderGlobal[globalTurn];
        return turnCid == NetworkManager.Singleton.LocalClientId;
    }

    public int GetPlayerIndexFromClientId(ulong clientId)
    {
        return seatOrderGlobal.IndexOf(clientId);
    }

    public void NextPlayerIndex()
    {
        int step = clockwiseTurn ? 1 : -1;
        // Do NOT skip seats based on isInRoom; disconnected players still occupy a seat.
        currentPlayerIndex = Mod(currentPlayerIndex + step, players.Count);
    }

    private bool TryResolveTurnOnce(ulong actorClientId)
    {
        if (!IsServer) return false;

        // must be current player's turn
        int currentGlobalSeat = GetGlobalIndexFromLocal(currentPlayerIndex);
        ulong expectedClientId = seatOrderGlobal[currentGlobalSeat];
        if (actorClientId != expectedClientId) return false;

        if (_serverTurnResolved) return false;  // already handled
        _serverTurnResolved = true;
        return true;
    }


    [ClientRpc]
    public void StartPlayerTurnForAllClientRpc(int globalPlayerIndex)
    {
        if (IsServer) _serverTurnResolved = false;
        if (Fiend.Instance != null)
            Fiend.Instance.HideFiendPopup();
        turnTimerLeft = turnTimerDuration;
        wasteInteractionStarted = false;
        deckInteractionLocked = false;
        foreach (var p in players)
            p.wasTimeout = false;
        if (cards.Count == 0)
        {
            DisableAllHandCardGlowAllPlayers();
            if (IsHost)
                StartCoroutine(ShowGameOverAfterDelay(1.5f));
            return;
        }
        isTurnEnding = false;
        Jack.Instance.isJackRevealPhase = false;
        ulong curClientId = seatOrderGlobal[GetGlobalIndexFromLocal(currentPlayerIndex)];
        peekedCardsByClientId.Remove(curClientId);
        hasPeekedCard = false;
        peekedCard = null;
        DisableAllHandCardGlowAllPlayers();
        unoBtn.SetActive(false);
        arrowObject.SetActive(false);
        arrowObject2.SetActive(false);

        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);

        if (previousPlayerIndex >= 0 && previousPlayerIndex < players.Count)
            players[previousPlayerIndex].OnTurnEnd();

        currentPlayerIndex = localIndex;
        CurrentPlayer.OnTurn();
        previousPlayerIndex = currentPlayerIndex;

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        ulong turnClientId = seatOrderGlobal[globalPlayerIndex];
        bool isMyTurnNow = (turnClientId == myClientId);

        if (isMyTurnNow && players[0].isUserPlayer)
        {
            deckInteractionLocked = false;
            EnableDeckClick();
            UpdateDeckClickability();
            arrowObject2.SetActive(ShouldShowWasteArrow());
        }
        else
        {
            arrowObject.SetActive(false);
            arrowObject2.SetActive(false);
            UpdateDeckClickability(); // this will disable deck due to IsMyTurn()==false
        }


        RefreshWasteInteractivity();

        if (IsHost)
        {
            _turnSerial++;

            if (_pendingAdvanceCo != null)
            {
                StopCoroutine(_pendingAdvanceCo);
                _pendingAdvanceCo = null;
            }
            if (turnTimeoutCoroutine != null)
            {
                StopCoroutine(turnTimeoutCoroutine);
                turnTimeoutCoroutine = null;
            }

            turnTimeoutCoroutine = StartCoroutine(HostTurnTimeoutRoutine(_turnSerial));
        }
    }

    private Card GetTopWasteCard()
    {
        if (cardWastePile == null || cardWastePile.transform.childCount == 0)
            return null;
        var top = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1);
        return top != null ? top.GetComponent<Card>() : null;
    }

    private IEnumerator ShowGameOverAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetupGameOver();
    }

    [ClientRpc]
    public void PlayDrawCardSoundClientRpc()
    {
        if (!CardGameManager.IsSound) return;
        if (_audioSource != null && draw_card_clip != null)
            _audioSource.PlayOneShot(draw_card_clip);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestWasteCardSwapServerRpc(int handIndex, SerializableCard newCard, SerializableCard replacedCard, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!TryResolveTurnOnce(clientId)) return;
        int playerIndex = GetPlayerIndexFromClientId(clientId);
        Debug.Log($"[WasteCardSwap] ServerRpc called by clientId={clientId}, playerIndex={playerIndex} (should be bot), handIndex={handIndex}");
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();

        if (playerIndex < 0 || playerIndex >= players.Count) return;

        wasteCards.Add(replacedCard);

        turnEndedByTimeout = false;
        ReplaceHandCardClientRpc(playerIndex, handIndex, newCard, replacedCard, cards.ToArray(), wasteCards.ToArray(), true);

        RemoveTopWasteCardClientRpc();

        peekedCardsByClientId.Remove(clientId);
        hasPeekedCard = false;
        peekedCard = null;
        wasteInteractionStarted = false;
    }

    [ClientRpc]
    void RemoveTopWasteCardClientRpc()
    {
        if (cardWastePile.transform.childCount == 0) return;
        Transform top = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1);
        if (top != null)
            Destroy(top.gameObject);
        if (cardWastePile.transform.childCount > 0)
        {
            var newTop = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1)
                                                .GetComponent<Card>();
            if (newTop != null && newTop.cursedOutline != null)
            {
                newTop.cursedOutline.SetActive(newTop.IsCursed);
                if (newTop.IsCursed && newTop.cursedOutline.transform.childCount > 0)
                    newTop.cursedOutline.transform.GetChild(0).gameObject.SetActive(true);
            }
        }
    }

    private void EndTurnAndGoNextPlayer()
    {
        if (isTurnEnding) return;
        isTurnEnding = true;

        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }

        CurrentPlayer.OnTurnEnd();
        EndTurnForAllClientRpc();

        if (IsHost)
        {
            NextPlayerTurn();
        }

        if (_pendingAdvanceCo != null)
        {
            StopCoroutine(_pendingAdvanceCo);
            _pendingAdvanceCo = null;
        }
    }

    public void RefreshWasteInteractivity()
    {
        foreach (Transform child in cardWastePile.transform)
        {
            var wp = child.GetComponent<WastePile>();
            if (wp != null)
            {
                wp.CancelChoosing();
                wp.ForceUpdateClickable();
            }
        }
    }

    public void EndCurrentPowerAvatarFromServer()
    {
        if (!IsHost) return;
        if (currentPowerOwnerGlobalSeat < 0) return;

        var targets = GetAllHumanClientIds();
        if (targets.Count > 0)
            EndPowerAvatarClientRpc(
                currentPowerOwnerGlobalSeat,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
            );
        currentPowerOwnerGlobalSeat = -1;
    }

    private void ResetAndRestartTurnTimerCoroutine()
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        turnTimerLeft = turnTimerDuration;
        turnTimeoutCoroutine = StartCoroutine(HostTurnTimeoutRoutine(_turnSerial));
    }

    public void DisableAllHandCardGlowAllPlayers()
    {
        foreach (var p in players)
            foreach (var card in p.cardsPanel.cards)
            {
                if (card == null) continue;
                card.ShowGlow(false);
                card.IsClickable = false;
                card.onClick = null;
            }
    }

    [ClientRpc]
    public void EndTurnForAllClientRpc()
    {
        foreach (var p in players)
            p.OnTurnEnd();
        HideTooltipOverlay();
    }

    public void NextPlayerTurn()
    {
        if (!IsHost) return;
        // clear any temp avatar effects
        var targets = GetAllHumanClientIds();
        if (targets.Count > 0)
            EndAllPowerAvatarsClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targets }
            });
        currentPowerOwnerGlobalSeat = -1;
        currentPowerValue = 0;

        NextPlayerIndex();
        int globalIndex = GetGlobalIndexFromLocal(currentPlayerIndex);
        StartPlayerTurnForAllClientRpc(globalIndex);

        if (IsCurrentPlayerBot())
            StartCoroutine(RunBotTurn(globalIndex));
    }

    public IEnumerator RunBotTurn(int botGlobalIndex)
    {
        if (Fiend.Instance != null)
            Fiend.Instance.HideFiendPopup();

        int localBotIndex = GetLocalIndexFromGlobal(botGlobalIndex);
        ulong botClientId = GetClientIdFromGlobalSeat(botGlobalIndex);

        // small "thinking" delay
        yield return new WaitForSeconds(Random.Range(1f, 2f));
        if (cards.Count == 0) yield break;

        bool isSuper = MultiplayerManager.Instance != null &&
                       MultiplayerManager.Instance.IsSuperbotClientId(botClientId);

        if (isSuper)
        {
            var hand = players[localBotIndex].cardsPanel.cards;

            // Try WASTE if available and better card than hand
            Card wasteCard = GetTopWasteCard();
            if (wasteCard != null && (wasteCard.killedOutline == null || !wasteCard.killedOutline.activeSelf))
            {
                int wScore = CardScore(wasteCard.Value);
                int replaceIdx = FindIndexOfHighestHandCardGreaterThanScore(hand, wScore); // includes non-numbered (10)
                if (replaceIdx != -1)
                {
                    yield return new WaitForSeconds(Random.Range(1f, 2f));

                    var wasteSer = new SerializableCard(wasteCard.Type, wasteCard.Value, wasteCard.IsCursed);
                    var replaced = hand[replaceIdx];
                    var replacedSer = new SerializableCard(replaced.Type, replaced.Value, replaced.IsCursed);

                    peekedCardsByClientId[botClientId] = wasteSer;

                    if (NetworkManager.Singleton.IsHost)
                        DoWasteCardSwapForBot(localBotIndex, replaceIdx, wasteSer, replacedSer);

                    yield break;
                }
            }

            // Otherwise check top DECK card
            SerializableCard topDeckCard = cards[cards.Count - 1];
            peekedCardsByClientId[botClientId] = topDeckCard;

            yield return new WaitForSeconds(Random.Range(1f, 2f));

            int dScore = CardScore(topDeckCard.Value);
            {
                int replaceIdx = FindIndexOfHighestHandCardGreaterThanScore(hand, dScore);
                if (replaceIdx != -1)
                {
                    if (NetworkManager.Singleton.IsHost)
                        DoDeckReplaceForBot(localBotIndex, replaceIdx);

                    yield break;
                }
            }

            // else just discard / activate
            RequestDiscardPeekedCardServerRpc(topDeckCard.Value, botClientId);
            yield break;
        }

        // normal bot logic.
        {
            Card wasteCard = GetTopWasteCard();
            bool canTakeWaste = false;
            SerializableCard wasteSerFallback = default;

            if (wasteCard != null && (wasteCard.killedOutline == null || !wasteCard.killedOutline.activeSelf))
            {
                if ((int)wasteCard.Value <= botWastePileMinValue && Random.Range(0, 100) < botWastePileDrawChance)
                {
                    canTakeWaste = true;
                    wasteSerFallback = new SerializableCard(wasteCard.Type, wasteCard.Value, wasteCard.IsCursed);
                }
            }

            if (canTakeWaste)
            {
                yield return new WaitForSeconds(Random.Range(1f, 2f));

                var botHand = players[localBotIndex].cardsPanel.cards;
                int swapHandIdx = Random.Range(0, botHand.Count);

                peekedCardsByClientId[botClientId] = wasteSerFallback;

                if (NetworkManager.Singleton.IsHost)
                {
                    SerializableCard replacedCard = new SerializableCard(botHand[swapHandIdx].Type, botHand[swapHandIdx].Value, botHand[swapHandIdx].IsCursed);
                    DoWasteCardSwapForBot(localBotIndex, swapHandIdx, wasteSerFallback, replacedCard);
                }
                yield break;
            }
        }

        // deck draw: chance-based replace or discard
        {
            float roll = Random.Range(0, 100);
            bool doReplace = roll < botDeckReplaceChance;

            if (cards.Count == 0) yield break;
            SerializableCard topDeckCard = cards[cards.Count - 1];
            peekedCardsByClientId[botClientId] = topDeckCard;

            yield return new WaitForSeconds(Random.Range(1f, 2f));

            if (doReplace)
            {
                var botHand = players[localBotIndex].cardsPanel.cards;
                int replaceIdx = Random.Range(0, botHand.Count);

                if (NetworkManager.Singleton.IsHost)
                    DoDeckReplaceForBot(localBotIndex, replaceIdx);
            }
            else
            {
                RequestDiscardPeekedCardServerRpc(topDeckCard.Value, botClientId);
            }
        }
    }

    private void DoDeckReplaceForBot(int botLocalIndex, int handIndex)
    {
        int globalIndex = GetGlobalIndexFromLocal(botLocalIndex);

        // Clear any bot peek entry
        ulong botClientId = GetClientIdFromGlobalSeat(globalIndex);
        peekedCardsByClientId.Remove(botClientId);

        if (cards.Count == 0) return;

        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);
        UpdateRemainingCardsCounter();

        Player2 p = players[botLocalIndex];
        Card replacedCard = p.cardsPanel.cards[handIndex];
        SerializableCard replacedSer = new SerializableCard(replacedCard.Type, replacedCard.Value, replacedCard.IsCursed);

        wasteCards.Add(replacedSer);

        ReplaceHandCardClientRpc(
            globalIndex, handIndex, drawn, replacedSer,
            cards.ToArray(), wasteCards.ToArray(), false
        );

        if (turnTimeoutCoroutine != null) { StopCoroutine(turnTimeoutCoroutine); turnTimeoutCoroutine = null; }
        FreezeTimerUI();
        StartCoroutine(WaitThenAdvanceBotTurn(_turnSerial));
    }

    private void DoWasteCardSwapForBot(int botLocalIndex, int handIndex, SerializableCard newCard, SerializableCard replacedCard)
    {
        int globalIndex = GetGlobalIndexFromLocal(botLocalIndex);

        wasteCards.Add(replacedCard);

        ReplaceHandCardClientRpc(
            globalIndex, handIndex, newCard, replacedCard,
            cards.ToArray(), wasteCards.ToArray(), true
        );

        if (turnTimeoutCoroutine != null) { StopCoroutine(turnTimeoutCoroutine); turnTimeoutCoroutine = null; }
        FreezeTimerUI();
        StartCoroutine(WaitThenAdvanceBotTurn(_turnSerial));

        RemoveTopWasteCardClientRpc();

        ulong botClientId = GetClientIdFromGlobalSeat(globalIndex);
        peekedCardsByClientId.Remove(botClientId);
        hasPeekedCard = false;
        peekedCard = null;
        wasteInteractionStarted = false;
    }

    public ulong GetClientIdFromGlobalSeat(int globalSeat)
    {
        if (globalSeat < 0 || globalSeat >= seatOrderGlobal.Count) return ulong.MaxValue;
        return seatOrderGlobal[globalSeat];
    }

    public int StableLocalToGlobal(int localSeat)
    {
        // where am I (globally) in the frozen order?
        var myId = NetworkManager.Singleton.LocalClientId;
        int myGlobal = seatOrderGlobal.IndexOf(myId);
        if (myGlobal < 0) myGlobal = 0;
        int n = seatOrderGlobal.Count;
        return ((myGlobal + localSeat) % n + n) % n;
    }

    public bool IsCurrentPlayerBot()
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int globalIndex = GetGlobalIndexFromLocal(currentPlayerIndex);
        if (globalIndex < 0 || globalIndex >= seatOrderGlobal.Count) return false;
        return seatOrderGlobal[globalIndex] >= 9000;
    }

    public void EnableDeckClick()
    {
        arrowObject.SetActive(true);
    }

    [ClientRpc]
    void BroadcastTurnTimerClientRpc(float secondsLeft, float totalSeconds)
    {
        turnTimerLeft = secondsLeft;

        for (int i = 0; i < players.Count; i++)
        {
            if (i == currentPlayerIndex)
            {
                players[i].SetTimerVisible(true);
                players[i].UpdateTurnTimerUI(secondsLeft, totalSeconds);
            }
            else
            {
                players[i].SetTimerVisible(false);
            }
        }
    }

    [ClientRpc]
    void ResetTurnTimerClientRpc(int playerIndex, float totalSeconds)
    {
        // NEW: prime local copy on all peers
        turnTimerLeft = totalSeconds;

        for (int i = 0; i < players.Count; i++)
        {
            players[i].SetTimerVisible(i == playerIndex);
            players[i].UpdateTurnTimerUI(totalSeconds, totalSeconds);
        }
    }

    private IEnumerator HostTurnTimeoutRoutine(int expectedSerial)
    {
        turnTimerLeft = turnTimerDuration;
        while (turnTimerLeft > 0f)
        {
            BroadcastTurnTimerClientRpc(turnTimerLeft, turnTimerDuration);
            yield return new WaitForSeconds(0.1f);
            turnTimerLeft -= 0.1f;
        }

        // If the turn changed while we were counting down, abort.
        if (expectedSerial != _turnSerial) yield break;

        BroadcastTurnTimerClientRpc(0f, turnTimerDuration);
        HardCancelInteractivityClientRpc();

        ulong currentTurnClientId = seatOrderGlobal[GetGlobalIndexFromLocal(currentPlayerIndex)];
        turnEndedByTimeout = true;
        ShowTimeoutMessageClientRpc(GetGlobalIndexFromLocal(currentPlayerIndex));

        if (peekedCardsByClientId.ContainsKey(currentTurnClientId))
        {
            var serCard = peekedCardsByClientId[currentTurnClientId];
            DiscardPeekedCardServerRpc((int)serCard.Type, (int)serCard.Value);
            yield return new WaitForSeconds(0.3f);
        }

        if (isTurnEnding) yield break;

        // Advance after a brief pause, still serial-guarded
        _pendingAdvanceCo = StartCoroutine(DelayedNextPlayerTurn(1f, expectedSerial));
    }


    public void UpdateDeckClickability()
    {
        if ((Jack.Instance != null && Jack.Instance.isJackRevealPhase)
        || isPeekingPhase
        || (Nemesis.Instance != null && Nemesis.Instance.isNemesisPhase))
        {
            DisableDeckClickability();
            return;
        }

        if (!deckInteractionLocked && players != null && players.Count > 0 && players[0].isUserPlayer && IsMyTurn() && !hasPeekedCard)
        {
            EnableDeckClick();
            int n = cardDeckTransform.childCount;
            for (int i = 0; i < n; i++)
            {
                Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
                if (c == null) continue;
                c.IsClickable = true;
                c.onClick = (card) =>
                {
                    OnDeckClickedByPlayer();
                };
            }
        }
        else
        {
            DisableDeckClickability();
        }
    }

    public void UpdateDeckVisualLocal(SerializableCard[] deckCards)
    {
        for (int i = cardDeckTransform.childCount - 1; i >= 0; i--)
            Destroy(cardDeckTransform.GetChild(i).gameObject);

        cards = new List<SerializableCard>(deckCards);

        for (int i = 0; i < deckCards.Length; i++)
        {
            var sc = deckCards[i];
            var card = Instantiate(_cardPrefab, cardDeckTransform);
            card.IsOpen = false;
            card.Type = sc.Type;
            card.Value = sc.Value;
            card.CalcPoint();
            card.SetCursed(sc.IsCursed);
            card.name = $"{sc.Type}_{sc.Value}";
            card.transform.localPosition = new Vector3(Random.Range(-2f, 2f), 0, -i * 1.15f);
        }

        UpdateDeckClickability();
        UpdateRemainingCardsCounter();
    }

    [ClientRpc]
    public void UpdateDeckVisualClientRpc(SerializableCard[] deckCards)
    {
        UpdateDeckVisualLocal(deckCards);
    }
    
    public void OnDeckClickedByPlayer()
    {
        if (isKingRefillPhase) return;
        WastePile waste = GameObject.FindObjectOfType<WastePile>();
        if (waste != null)
        {
            waste.CancelChoosing();
            GamePlayManager.instance.wasteInteractionStarted = false;
        }

        if (players == null || players.Count == 0 || players[0] == null) return;
        if (unoBtn == null) return;

        if (!players[0].isUserPlayer || !IsMyTurn()) return;
        if (hasPeekedCard) return;
        if (cardDeckTransform.childCount == 0) return;

        if (arrowObject) arrowObject.SetActive(false);
        if (arrowObject2) arrowObject2.SetActive(false);

        peekedDeckIndex = cards.Count - 1;

        Transform topCardTf = cardDeckTransform.GetChild(cardDeckTransform.childCount - 1);
        Card card = topCardTf.GetComponent<Card>();
        if (card == null) return;

        card.IsOpen = true;

        peekedCard = card;
        hasPeekedCard = true;

        unoBtn.SetActive(true);
        unoBtn.GetComponent<Button>().interactable = true;
        unoBtn.GetComponent<Button>().onClick.RemoveAllListeners();
        unoBtn.GetComponent<Button>().onClick.AddListener(OnDiscardClicked);

        string btnText = IsSpecialCard(card.Value) ? "ACTIVATE" : "DISCARD";
        unoBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = btnText;

        EnableHandCardReplacementGlow();

        ulong myClientId = NetworkManager.Singleton.LocalClientId;

        var peekedSerCard = new SerializableCard(card.Type, card.Value, card.IsCursed);
        peekedCardsByClientId[myClientId] = peekedSerCard;

        if (!IsHost || (IsHost && !NetworkManager.Singleton.IsServer))
            NotifyHostPeekedCardServerRpc((int)card.Type, (int)card.Value);
    }

    public void NotifyWasteInteractionStarted()
    {
        wasteInteractionStarted = true;
        if (arrowObject2) arrowObject2.SetActive(false);
    }


    [ServerRpc(RequireOwnership = false)]
    public void NotifyHostPeekedCardServerRpc(int cardType, int cardValue, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        var peekedCard = new SerializableCard((CardType)cardType, (CardValue)cardValue);
        peekedCardsByClientId[clientId] = peekedCard;

        Debug.Log($"Client {clientId} peeked card: {peekedCard.Type} {peekedCard.Value}");
    }

    public void EnableHandCardReplacementGlow()
    {
        if (isKingRefillPhase) return;
        if (!IsMyTurn() || !players[0].isUserPlayer) return;
        for (int i = 0; i < players[0].cardsPanel.cards.Count; i++)
        {
            var handCard = players[0].cardsPanel.cards[i];
            handCard.ShowGlow(true);
            handCard.IsClickable = true;
            int handIndex = i;
            handCard.onClick = null;
            handCard.onClick = (clickedCard) =>
            {
                DisableAllHandCardGlow();
                OnHandCardReplaceRequested(handIndex);
            };
        }
    }

    public void OnDiscardClicked()
    {
        if (!hasPeekedCard || peekedCard == null) return;
        if (!unoBtn.GetComponent<Button>().interactable) return;

        deckInteractionLocked = true;
        arrowObject.SetActive(false);
        arrowObject2.SetActive(false);
        int n = cardDeckTransform.childCount;
        for (int i = 0; i < n; i++)
        {
            Card c = cardDeckTransform.GetChild(i).GetComponent<Card>();
            if (c != null)
            {
                c.IsClickable = false;
                c.onClick = null;
            }
        }

        if (!hasPeekedCard || peekedCard == null) return;

        var discardValue = peekedCard.Value;
        unoBtn.SetActive(false);
        DisableAllHandCardGlow();
        RequestDiscardPeekedCardServerRpc(discardValue);
    }

    [ServerRpc(RequireOwnership = false)]
    void DiscardPeekedCardServerRpc(int cardType, int cardValue, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        if (!TryResolveTurnOnce(seatOrderGlobal[GetGlobalIndexFromLocal(currentPlayerIndex)])) return;
        var currentTurnClientId = seatOrderGlobal[GetGlobalIndexFromLocal(currentPlayerIndex)];
        peekedCardsByClientId.Remove(currentTurnClientId);
        if (cards.Count == 0) return;

        SerializableCard topCard = cards[cards.Count - 1];

        if ((int)topCard.Type != cardType || (int)topCard.Value != cardValue)
        {
            EndTurnAndGoNextPlayer();
            return;
        }


        cards.RemoveAt(cards.Count - 1);
        UpdateRemainingCardsCounter();
        wasteCards.Add(topCard);

        ShowWastePileCardClientRpc(cardType, cardValue, topCard.IsCursed);

        Debug.Log($"Remaining cards left in Deck: {cards.Count}");

        DiscardPeekedCardClientRpc(cardType, cardValue, cards.ToArray());

        ForceDiscardClientRpc();

        EndTurnAndGoNextPlayer();
    }

    [ClientRpc]
    void ForceDiscardClientRpc()
    {
        if (peekedCard != null)
        {
            Destroy(peekedCard.gameObject);
            peekedCard = null;
            hasPeekedCard = false;
            wasteInteractionStarted = false;
        }
        deckInteractionLocked = false;
        unoBtn.SetActive(false);
    }

    [ClientRpc]
    void DiscardPeekedCardClientRpc(int cardType, int cardValue, SerializableCard[] deckCards)
    {
        UpdateDeckVisualLocal(deckCards);
        if (peekedCard != null)
        {
            Destroy(peekedCard.gameObject);
            peekedCard = null;
            hasPeekedCard = false;
            wasteInteractionStarted = false;
        }
        deckInteractionLocked = false;
        unoBtn.SetActive(false);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestDiscardPeekedCardServerRpc(CardValue discardValue, ulong actorClientId = ulong.MaxValue, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = (actorClientId == ulong.MaxValue)
            ? rpcParams.Receive.SenderClientId
            : actorClientId;
        int playerIndex = GetPlayerIndexFromClientId(senderClientId);
        if (!TryResolveTurnOnce(senderClientId)) return;
        peekedCardsByClientId.Remove(senderClientId);


        if (playerIndex < 0 || cards.Count == 0)
        {
            Debug.LogWarning("[RequestDiscardPeekedCardServerRpc] Invalid playerIndex or no cards!");
            return;
        }
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();

        if (playerIndex < 0 || cards.Count == 0) return;

        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);
        UpdateRemainingCardsCounter();

        wasteCards.Add(drawn);

        if (drawn.Value == CardValue.Jack || drawn.Value == CardValue.Queen || drawn.Value == CardValue.King
    || drawn.Value == CardValue.Fiend || drawn.Value == CardValue.GoldenJack || drawn.Value == CardValue.Nemesis)
        {
            PlaySpecialCardVoiceClientRpc((int)drawn.Value);

            if (Jack.Instance != null)
            {
                if (drawn.Value == CardValue.Jack) Jack.Instance.MarkVoStart(CardValue.Jack);
                else if (drawn.Value == CardValue.GoldenJack) Jack.Instance.MarkVoStart(CardValue.GoldenJack);
            }

            currentPowerOwnerGlobalSeat = playerIndex;
            currentPowerValue = drawn.Value;

            var targets = GetAllHumanClientIds();
            if (targets.Count > 0)
                BeginPowerAvatarClientRpc(
                    currentPowerOwnerGlobalSeat,
                    (int)currentPowerValue,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
                );

            StartCoroutine(SafetyEndPowerAvatarAfter(
                currentPowerOwnerGlobalSeat,
                float.PositiveInfinity,
                60f
            ));
        }

        DiscardPeekedCardWithVisualClientRpc(playerIndex, drawn, cards.ToArray(), wasteCards.ToArray());

        if (discardValue == CardValue.Jack)
        {
            ShowPowerMessageClientRpc(playerIndex, "Jack's Power", 1.5f);
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);

            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();
            Debug.Log($"[RequestDiscardPeekedCardServerRpc] senderClientId={senderClientId} IsBot={IsBotClientId(senderClientId)} IsHost={IsHost}");

            if (IsBotClientId(senderClientId))
            {
                if (IsHost)
                    StartCoroutine(Jack.Instance.SimulateBotJackReveal(senderClientId));
            }
            else
            {
                Jack.Instance.StartJackRevealLocalOnlyClientRpc(
                    senderClientId,
                    new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                    }
                );
                return;
            }
            return;
        }

        else if (discardValue == CardValue.Queen)
        {
            ShowPowerMessageClientRpc(playerIndex, "Queen's Power", 1.5f);
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);

            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();

            StartQueenSwapLocalOnlyClientRpc(
                senderClientId,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                }
            );
            if (IsHost && IsBotClientId(senderClientId))
            {
                Queen.Instance.StartBotQueenSwapPhase(senderClientId);
            }
            return;
        }

        else if (discardValue == CardValue.King)
        {
            ShowPowerMessageClientRpc(playerIndex, "King's Power", 1.5f);
            if (turnTimeoutCoroutine != null)
            {
                StopCoroutine(turnTimeoutCoroutine);
                turnTimeoutCoroutine = null;
            }
            FreezeTimerUI();

            if (cards.Count == 0)
            {
                GamePlayManager.instance.EndCurrentPowerAvatarFromServer();
                // Powerless King: just a normal discard, no timer reset
                _pendingAdvanceCo = StartCoroutine(DelayedNextPlayerTurn(1.0f, _turnSerial));
                return;
            }

            // Otherwise, do King phase as usual (reset timer, etc)
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);
            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();

            StartKingGlowLocalOnlyClientRpc(
                senderClientId,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                }
            );
            if (IsHost && IsBotClientId(senderClientId))
            {
                King.Instance.StartBotKingPhase(senderClientId);
            }
            return;

        }

        else if (discardValue == CardValue.Fiend)
        {
            ShowPowerMessageClientRpc(playerIndex, "Fiend's Power", 1.5f);
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);
            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();

            StartFiendPopupLocalOnlyClientRpc(
                senderClientId,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                }
            );
            if (IsHost && IsBotClientId(senderClientId))
            {
                Fiend.Instance.StartBotFiendJumble(senderClientId);
            }
            return;
        }

        else if (discardValue == CardValue.GoldenJack)
        {
            ShowPowerMessageClientRpc(playerIndex, "Golden Jack's Power", 1.5f);
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);

            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();

            if (IsBotClientId(senderClientId))
            {
                if (IsHost)
                    StartCoroutine(Jack.Instance.SimulateBotGoldenJackReveal(senderClientId));
            }
            else
            {
                Jack.Instance.StartGoldenJackRevealLocalOnlyClientRpc(
                    senderClientId,
                    new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                    }
                );
                return;
            }
            return;
        }
        else if (discardValue == CardValue.Nemesis)
        {
            ShowPowerMessageClientRpc(playerIndex, "Nemesis's Power", 1.5f);
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);

            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();

            StartNemesisLocalOnlyClientRpc(
                senderClientId,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                }
            );

            // Bot support
            if (IsHost && IsBotClientId(senderClientId))
                Nemesis.Instance.StartBotNemesisCurse(senderClientId);

            return;
        }
        else if (discardValue == CardValue.Skip)
        {
            ShowPowerMessageClientRpc(playerIndex, "Skip Activated");
            if (IsHost && Skip.Instance != null)
                Skip.Instance.TriggerSkip();

            return;
        }

        if (IsHost)
        {
            _pendingAdvanceCo = StartCoroutine(DelayedNextPlayerTurn(1f, _turnSerial));
        }
        else
            EndTurnForAllClientRpc();
    }

    public bool IsBotClientId(ulong clientId)
    {
        return clientId >= 9000;
    }

    [ClientRpc]
    void PlaySpecialCardVoiceClientRpc(int cardValue)
    {
        if (!CardGameManager.IsSound) return;
        if (_audioSource == null) return;
        AudioClip clip = null;
        switch ((CardValue)cardValue)
        {
            case CardValue.Jack: clip = jackVoiceClip; break;
            case CardValue.Queen: clip = queenVoiceClip; break;
            case CardValue.King: clip = kingVoiceClip; break;
            case CardValue.Fiend: clip = fiendVoiceClip; break;
            case CardValue.GoldenJack: clip = goldenJackVoiceClip; break;
            case CardValue.Nemesis : clip = nemesisVoiceClip; break;
        }
        if (clip != null)
        {
            _audioSource.volume = 1f;
            _audioSource.PlayOneShot(clip);
        }
    }

    [ClientRpc]
    void StartNemesisLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        Nemesis.Instance.StartNemesisPhase();
        ShowTooltipOverlay("Nemesis: Pick a card to Curse!");
    }

    [ClientRpc]
    void StartFiendPopupLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        Fiend.Instance.ShowFiendPopup();
        ShowTooltipOverlay("Fiend: Pick a player to Jumble his cards!");
    }

    [ClientRpc]
    void StartQueenSwapLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        Queen.Instance.StartQueenSwap();
        ShowTooltipOverlay("Queen Power: Pick 2 cards to Swop!");
    }

    [ClientRpc]
    void StartKingGlowLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        King.Instance.StartKingPhase();
        ShowTooltipOverlay("King Power: Pick a card to Kill!");
    }

    [ClientRpc]
    void DiscardPeekedCardWithVisualClientRpc(int playerIndex, SerializableCard discardedCard, SerializableCard[] deck, SerializableCard[] wastePile)
    {
        HideNonTopWasteParticles();
        if (peekedCard != null)
        {
            Destroy(peekedCard.gameObject);
            peekedCard = null;
            hasPeekedCard = false;
        }


        var wasteObj = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, cardWastePile.transform.parent);
        wasteObj.gameObject.AddComponent<WastePile>().Initialize(wasteObj);
        wasteObj.Type = discardedCard.Type;
        wasteObj.Value = discardedCard.Value;
        wasteObj.IsOpen = true;
        wasteObj.SetCursed(discardedCard.IsCursed);
        wasteObj.CalcPoint();
        wasteObj.name = $"{wasteObj.Type}_{wasteObj.Value}";

        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(wasteObj, cardDeckTransform.position, cardWastePile.transform.position, 0.3f, randomRot));
        UpdateDeckVisualLocal(deck);
        OnUnoClick(discardedCard.Value);
    }

    public IEnumerator AnimateCardMove(Card card, Vector3 from, Vector3 to, float duration, float? targetZRot = null)
    {
        if (card == null) yield break;

        float elapsed = 0;
        Quaternion startRot = card.transform.rotation;
        Quaternion endRot = targetZRot.HasValue
            ? Quaternion.Euler(0, 0, targetZRot.Value)
            : card.transform.rotation;

        while (elapsed < duration)
        {
            if (card == null) yield break;

            card.transform.position = Vector3.Lerp(from, to, elapsed / duration);
            card.transform.rotation = Quaternion.Slerp(startRot, endRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (card == null) yield break;
        card.transform.position = to;
        card.transform.rotation = endRot;

        if (card != null && cardWastePile != null)
        {
            card.transform.SetParent(cardWastePile.transform, true); // keep exact world pos
            card.transform.SetAsLastSibling();
            if (card.cursedOutline != null && card.IsCursed)
            {
                card.cursedOutline.SetActive(true);
                if (card.cursedOutline.transform.childCount > 0)
                    card.cursedOutline.transform.GetChild(0).gameObject.SetActive(true);
            }

            RefreshWasteInteractivity();
            if (IsServer) PlayDrawCardSoundClientRpc();
        }
        if (IsHost && cards.Count == 0)
        {
            unoBtn.SetActive(false);
            arrowObject.SetActive(false);
            arrowObject2.SetActive(false);
        }
    }

    public void OnHandCardReplaceRequested(int handIndex)
    {
        if (isKingRefillPhase) return;
        if (!hasPeekedCard || deckInteractionLocked) return;
        deckInteractionLocked = true;
        RequestReplaceHandCardServerRpc(handIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestReplaceHandCardServerRpc(int handIndex, ServerRpcParams rpcParams = default)
    {
        if (isKingRefillPhase) return;
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!TryResolveTurnOnce(senderClientId)) return;
        peekedCardsByClientId.Remove(senderClientId);
        int playerIndex = GetPlayerIndexFromClientId(senderClientId);
        if (playerIndex < 0 || playerIndex >= players.Count) return;
        if (cards.Count == 0) return;

        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);
        UpdateRemainingCardsCounter();

        Player2 p = players[playerIndex];
        Card replacedCard = p.cardsPanel.cards[handIndex];
        SerializableCard replacedSer = new SerializableCard(replacedCard.Type, replacedCard.Value, replacedCard.IsCursed);

        wasteCards.Add(replacedSer);

        turnEndedByTimeout = false;
        ReplaceHandCardClientRpc(playerIndex, handIndex, drawn, replacedSer, cards.ToArray(), wasteCards.ToArray(), false);

    }

    public IEnumerator DelayedNextPlayerTurn(float delay, int expectedSerial)
    {
        yield return new WaitForSeconds(delay);
        if (!IsHost || expectedSerial != _turnSerial) yield break;
        if (isTurnEnding) yield break;
        EndTurnAndGoNextPlayer();
    }

    private IEnumerator WaitThenAdvanceBotTurn(int expectedSerial)
    {
        yield return new WaitForSeconds(2.5f);
        if (!IsHost || expectedSerial != _turnSerial) yield break;
        EndTurnAndGoNextPlayer();
    }


    [ClientRpc]
    public void ReplaceHandCardClientRpc(
    int playerIndex,
    int handIndex,
    SerializableCard newCard,
    SerializableCard waste,
    SerializableCard[] deck,
    SerializableCard[] wastePile,
    bool fromWastePile)
    {
        if (isKingRefillPhase) return;

        HideNonTopWasteParticles();

        int localSeat = GetLocalIndexFromGlobal(playerIndex);
        var p = players[localSeat];
        if (p == null) return;

        Card oldCard = p.cardsPanel.cards[handIndex];
        if (oldCard == null) return;

        Vector3 slotLocalPos = oldCard.transform.localPosition;
        Quaternion slotLocalRot = oldCard.transform.localRotation;
        Vector3 slotLocalScale = oldCard.transform.localScale;

        Vector3 slotWorldPos = oldCard.transform.position;
        Vector3 fromWorld = (fromWastePile ? cardWastePile.transform.position : cardDeckTransform.position);

        oldCard.IsClickable = false;
        oldCard.onClick = null;
        oldCard.IsOpen = true;
        oldCard.transform.SetParent(AnimRoot, true);

        StartCoroutine(_FlyOldToWaste(oldCard, slotWorldPos));

        Card incoming = Instantiate(_cardPrefab, AnimRoot).GetComponent<Card>();
        incoming.Type = newCard.Type;
        incoming.Value = newCard.Value;
        incoming.IsOpen = fromWastePile ? true : p.isUserPlayer;
        incoming.CalcPoint();
        incoming.IsClickable = false;
        incoming.SetCursed(newCard.IsCursed);
        incoming.onClick = null;

        UpdateDeckVisualLocal(deck);

        float dur = fromWastePile ? 0.8f : 0.5f;

        p.cardsPanel.cards[handIndex] = null;

        StartCoroutine(FlyAndAdoptToSlot(
            incoming,
            fromWorld,
            p, handIndex,
            slotLocalPos, slotLocalRot, slotLocalScale,
            dur
        ));

        ShowReplacedMessageClientRpc(playerIndex);
        DisableUnoBtn();

        // Capture & clear the flag once
        bool timedOut = turnEndedByTimeout;
        turnEndedByTimeout = false;

        // One sound, once
        CardGameManager.PlaySound(normal_click);

        // Deck empty UI guard
        if (IsHost && cards.Count == 0)
        {
            unoBtn.SetActive(false);
            arrowObject.SetActive(false);
            arrowObject2.SetActive(false);
        }

        // If the timer expired during the replace animation, force-advance now.
        if (timedOut)
        {
            if (IsHost)
            {
                if (_pendingAdvanceCo != null) { StopCoroutine(_pendingAdvanceCo); _pendingAdvanceCo = null; }
                _pendingAdvanceCo = StartCoroutine(DelayedNextPlayerTurn(0.2f, _turnSerial));
            }
            return;
        }

        // Normal path → host schedules next turn
        if (IsHost)
        {
            var playerList = MultiplayerManager.Instance.playerDataNetworkList;
            bool isBot = (playerIndex >= 0 && playerIndex < playerList.Count && playerList[playerIndex].clientId >= 9000);
            if (!isBot)
                _pendingAdvanceCo = StartCoroutine(DelayedNextPlayerTurn(2.5f, _turnSerial));
        }

    }

    private IEnumerator _FlyOldToWaste(Card oldCard, Vector3 fromWorld)
    {
        if (oldCard == null) yield break;

        Vector3 wasteWorld = cardWastePile.transform.position;
        float wasteDur = 0.5f;
        float randomRot = Random.Range(-50f, 50f);
        yield return StartCoroutine(FlyWorld(oldCard.transform, fromWorld, wasteWorld, wasteDur, Quaternion.Euler(0, 0, randomRot)));

        if (oldCard == null) yield break;

        oldCard.transform.SetParent(cardWastePile.transform, true);
        oldCard.transform.SetAsLastSibling();
        if (oldCard.cursedOutline != null && oldCard.IsCursed)
        {
            oldCard.cursedOutline.SetActive(true);
            if (oldCard.cursedOutline.transform.childCount > 0)
                oldCard.cursedOutline.transform.GetChild(0).gameObject.SetActive(true);
        }

        var wp = oldCard.GetComponent<WastePile>();
        if (wp == null) wp = oldCard.gameObject.AddComponent<WastePile>();
        wp.Initialize(oldCard);

        RefreshWasteInteractivity();
        if (IsServer) PlayDrawCardSoundClientRpc();
    }

    void DisableAllHandCardGlow()
    {
        foreach (var card in players[0].cardsPanel.cards)
        {
            if (card == null) continue;
            card.ShowGlow(false);
            card.IsClickable = false;
            card.onClick = null;
        }
    }

    [ClientRpc]
    void ShowWastePileCardClientRpc(int cardType, int cardValue, bool isCursed)
    {
        HideNonTopWasteParticles();

        var discard = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, cardWastePile.transform);
        discard.Type = (CardType)cardType;
        discard.Value = (CardValue)cardValue;
        discard.IsOpen = true;
        discard.CalcPoint();
        discard.name = $"{discard.Type}_{discard.Value}";
        discard.IsClickable = false;
        discard.SetCursed(isCursed);

        // turn on cursed outline/particles for the new TOP
        if (discard.cursedOutline != null)
        {
            discard.cursedOutline.SetActive(isCursed);
            if (isCursed && discard.cursedOutline.transform.childCount > 0)
                discard.cursedOutline.transform.GetChild(0).gameObject.SetActive(true);
        }

        var wp = discard.gameObject.AddComponent<WastePile>();
        wp.Initialize(discard);

        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(discard, cardDeckTransform.position, cardWastePile.transform.position, 0.3f, randomRot));
        OnUnoClick((CardValue)cardValue);
    }

    public void PutCardToWastePile(Card c, Player2 p = null)
    {
        if (p != null)
        {
            c.ShowGlow(false);
            p.RemoveCard(c);
            if (p.cardsPanel.cards.Count == 1 && !p.unoClicked)
                CardGameManager.PlaySound(draw_card_clip);
        }

        CurrentType = c.Type;
        CurrentValue = c.Value;

        SerializableCard serializableDiscard = new SerializableCard(c.Type, c.Value, c.IsCursed);
        wasteCards.Add(serializableDiscard);

        if (p != null)
        {
            if (p.cardsPanel.cards.Count == 0)
            {
                Invoke("SetupGameOver", 2f);
                return;
            }
        }
    }

    public void SetupGameOver()
    {
        SetGameHasEndedClientRpc();
        for (int i = players.Count - 1; i >= 0; i--)
        {
            if (!players[i].isInRoom)
                players.RemoveAt(i);
        }

        // Sort and build result array
        List<Player2> sorted = new List<Player2>(players);
        sorted.Sort((a, b) => a.GetTotalPoints().CompareTo(b.GetTotalPoints()));

        WinnerResultData[] results = new WinnerResultData[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            results[i] = new WinnerResultData
            {
                playerName = sorted[i].playerName,
                avatarIndex = sorted[i].avatarIndex,
                totalPoints = sorted[i].GetTotalPoints(),
                isUserPlayer = sorted[i].isUserPlayer,
                cards = sorted[i].cardsPanel.cards
                    .Where(c => c != null)
                    .Select(c => new SerializableCard(c.Type, c.Value, c.IsCursed))
                    .ToArray()
            };
        }

        int userIndex = -1;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].isUserPlayer)
            {
                userIndex = i;
                break;
            }
        }
        for (int localSeat = 0; localSeat < players.Count; localSeat++)
        {
            int globalSeat = GetGlobalIndexFromLocal(localSeat);
            ulong clientId = GetClientIdFromGlobalSeat(globalSeat);
            if (clientId == ulong.MaxValue) continue; // seat has no valid client

            int sortedIndex = sorted.IndexOf(players[localSeat]);
            bool didWin = (sortedIndex == 0);

            PlayGameOverSoundClientRpc(didWin,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } }
            );
        }

        ShowGameOverClientRpc();
        ShowWinnerResultDataClientRpc(results);

        if (winnerUI != null)
            winnerUI.ShowWinnersFromNetwork(results);
    }

    [ClientRpc]
    private void SetGameHasEndedClientRpc()
    {
        GameHasEnded = true;
    }


    [ClientRpc]
    void ShowGameOverClientRpc()
    {
        if (screenCanvas != null)
            screenCanvas.SetActive(false);
        if (gameOverPopup != null)
            gameOverPopup.SetActive(true);
        if (cardWastePile != null)
        {
            cardWastePile.gameObject.SetActive(false);
        }
        if (playerCardsPanel != null)
            playerCardsPanel.SetActive(false);

    }

    [ClientRpc]
    public void ShowWinnerResultDataClientRpc(WinnerResultData[] data)
    {
        if (winnerUI != null)
            winnerUI.ShowWinnersFromNetwork(data);
    }

    private int Mod(int x, int m)
    {
        return (x % m + m) % m;
    }

    public void DisableUnoBtn()
    {
        unoBtn.GetComponent<Button>().interactable = false;
    }

    public void OnUnoClick(CardValue discardedValue)
    {
        DisableUnoBtn();

        if (CurrentPlayer.wasTimeout)
        {
            CurrentPlayer.wasTimeout = false;
            return;
        }

        bool isSpecial = IsSpecialCard(discardedValue);

        CurrentPlayer.ShowMessage("Discarded", false);
        CurrentPlayer.unoClicked = true;

        // Sound: special vs normal
        CardGameManager.PlaySound(isSpecial ? special_click : normal_click);
    }


    public void FreezeTimerUI()
    {
        BroadcastTurnTimerClientRpc(turnTimerLeft, turnTimerDuration);
    }

    private bool IsSpecialCard(CardValue value)
    {
        return value == CardValue.King
            || value == CardValue.Queen
            || value == CardValue.Jack
            || value == CardValue.Fiend
            || value == CardValue.GoldenJack
            || value == CardValue.Skip
            || value == CardValue.Nemesis;
    }


    private bool ShouldShowWasteArrow()
    {
        Card top = GetTopWasteCard();
        if (top == null)
            return false;
        if (top.killedOutline != null && top.killedOutline.activeSelf)
            return false;
        return true;
    }

    private void UpdateRemainingCardsCounter()
    {
        if (remainingCardsCounterText != null)
            remainingCardsCounterText.text = cards != null ? cards.Count.ToString() : "0";
    }

    [ClientRpc]
    public void ShowTimeoutMessageClientRpc(int globalPlayerIndex)
    {
        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex >= 0 && localIndex < players.Count)
        {
            players[localIndex].ShowMessage("Timeout", false, 1f);
            players[localIndex].wasTimeout = true;
        }
    }


    [ClientRpc]
    public void ShowPowerMessageClientRpc(int globalPlayerIndex, string text, float duration = 1.5f)
    {
        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex >= 0 && localIndex < players.Count)
        {
            players[localIndex].ShowMessage(text, true, duration);
        }
    }

    [ClientRpc]
    public void ShowReplacedMessageClientRpc(int globalPlayerIndex)
    {
        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex >= 0 && localIndex < players.Count)
            players[localIndex].ShowMessage("Replaced", false, 1.1f);
    }

    [ClientRpc]
    public void PlayGameOverSoundClientRpc(bool didWin, ClientRpcParams rpcParams = default)
    {
        if (_audioSource != null)
        {
            if (didWin && music_win_clip != null)
                _audioSource.PlayOneShot(music_win_clip);
            else if (!didWin && music_loss_clip != null)
                _audioSource.PlayOneShot(music_loss_clip);
        }
    }

    public void Cleanup()
    {
        players?.Clear();
        cards?.Clear();
        wasteCards?.Clear();
        peekedCardsByClientId?.Clear();
        if (_audioSource != null)
            _audioSource.Stop();
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        if (turnTimerCoroutine != null)
        {
            StopCoroutine(turnTimerCoroutine);
            turnTimerCoroutine = null;
        }
        if (hostHeartbeatCo != null) { StopCoroutine(hostHeartbeatCo); hostHeartbeatCo = null; }
        if (clientWatchdogCo != null) { StopCoroutine(clientWatchdogCo); clientWatchdogCo = null; }
        HideLoadingUI();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    [ClientRpc]
    public void ShowDisconnectUIClientRpc()
    {
        if (GameHasEnded) return;
        var ui = FindObjectOfType<DisconnectUI>(true);
        if (ui != null) ui.Show();
    }

    public Sprite GetAvatarSpriteForGlobalSeatSafe(int globalSeat)
    {
        int localSeat = GetLocalIndexFromGlobal(globalSeat);
        if (localSeat >= 0 && localSeat < players.Count)
        {
            var p2 = players[localSeat];
            if (p2 != null && p2.avatarImage != null && p2.avatarImage.sprite != null)
                return p2.avatarImage.sprite;
        }

        if (baseAvatarSprites != null &&
            localSeat >= 0 &&
            localSeat < baseAvatarSprites.Count &&
            baseAvatarSprites[localSeat] != null)
        {
            return baseAvatarSprites[localSeat];
        }

        // Nothing available
        return null;
    }

    [ClientRpc]
    void PlayWinnerConfettiClientRpc(ulong winnerClientId, ClientRpcParams rpcParams = default)
    {
        var myId = NetworkManager.Singleton.LocalClientId;
        Debug.Log($"[Confetti] RPC on client {myId}, winner={winnerClientId}");
        if (myId != winnerClientId) return;  // local guard

        if (wheelUI != null)
            wheelUI.PlayLocalWinFX();
    }

    [ClientRpc]
    private void HardCancelInteractivityClientRpc(ClientRpcParams rpcParams = default)
    {
        // clear card highlight, clickability, peek states, discard/activate buttons, power phases
        DisableAllHandCardGlowAllPlayers();

        DisableDeckClickability();

        if (unoBtn != null)
        {
            unoBtn.SetActive(false);
            var b = unoBtn.GetComponent<UnityEngine.UI.Button>();
            if (b != null) b.interactable = false;
        }

        if (Jack.Instance != null)
        {
            Jack.Instance.isJackRevealPhase = false;
            Jack.Instance.isGoldenJackRevealPhase = false;
        }

        if (King.Instance != null)
        {
            King.Instance.isKingPhase = false;
        }

        if (Queen.Instance != null)
        {
            Queen.Instance.isQueenSwapPhase = false;
        }

        if (peekedCard != null && cardDeckTransform != null &&
        peekedCard.transform.IsChildOf(cardDeckTransform))
        {
            peekedCard.IsOpen = false;
        }
        peekedCard = null;
        hasPeekedCard = false;

        RefreshWasteInteractivity();
    }

    [ClientRpc]
    void EndAllPowerAvatarsClientRpc(ClientRpcParams rpcParams = default)
    {
        if (players == null || baseAvatarSprites == null) return;

        for (int localSeat = 0; localSeat < players.Count; localSeat++)
        {
            var p = players[localSeat];
            if (p?.avatarImage == null) continue;

            var baseSprite = (localSeat < baseAvatarSprites.Count) ? baseAvatarSprites[localSeat] : null;
            if (baseSprite != null)
                p.avatarImage.sprite = baseSprite;

            if (avatarRevertBySeat.TryGetValue(localSeat, out var running) && running != null)
                StopCoroutine(running);
            avatarRevertBySeat[localSeat] = null;
        }
    }


    private int CardScore(CardValue v)
    {
        switch (v)
        {
            case CardValue.Zero: return 0;
            case CardValue.One: return 1;
            case CardValue.Two: return 2;
            case CardValue.Three: return 3;
            case CardValue.Four: return 4;
            case CardValue.Five: return 5;
            case CardValue.Six: return 6;
            case CardValue.Seven: return 7;
            case CardValue.Eight: return 8;
            case CardValue.Nine: return 9;
            default: return 10;
        }
    }

    private int FindIndexOfHighestHandCardGreaterThanScore(List<Card> hand, int incomingScore)
    {
        int bestIdx = -1;
        int bestScore = int.MinValue;

        for (int i = 0; i < hand.Count; i++)
        {
            var c = hand[i];
            if (c == null) continue;

            int s = CardScore(c.Value);
            if (s > incomingScore && s > bestScore)
            {
                bestScore = s;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlayEmojiServerRpc(int globalSeat, int emojiIndex, ServerRpcParams rpcParams = default)
    {
        PlayEmojiClientRpc(globalSeat, emojiIndex);
    }

    [ClientRpc]
    public void PlayEmojiClientRpc(int globalSeat, int emojiIndex, ClientRpcParams rpcParams = default)
    {
        int localSeat = GetLocalIndexFromGlobal(globalSeat);
        if (localSeat < 0 || localSeat >= players.Count) return;

        var p2 = players[localSeat];
        if (p2 == null) return;

        p2.SpawnEmojiLocal(emojiIndex);

        if (CardGameManager.IsSound && _audioSource != null &&
            p2.emojiSfx != null && emojiIndex >= 0 && emojiIndex < p2.emojiSfx.Length)
        {
            var clip = p2.emojiSfx[emojiIndex];
            if (clip != null)
                _audioSource.PlayOneShot(clip, p2.emojiSfxVolume);
        }
    }

    private IEnumerator FlyWorld(Transform t, Vector3 from, Vector3 to, float duration, Quaternion? endRot = null)
    {
        if (t == null) yield break;
        float elapsed = 0f;
        Quaternion r0 = t.rotation;
        Quaternion r1 = endRot ?? t.rotation;

        while (elapsed < duration)
        {
            if (t == null) yield break;
            float k = elapsed / duration;
            t.position = Vector3.Lerp(from, to, k);
            t.rotation = Quaternion.Slerp(r0, r1, k);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (t == null) yield break;
        t.position = to;
        t.rotation = r1;
    }

    private IEnumerator FlyAndAdoptToSlot(
        Card incoming,
        Vector3 fromWorld,
        Player2 p, int handIndex,
        Vector3 slotLocalPos, Quaternion slotLocalRot, Vector3 slotLocalScale,
        float duration)
    {
        if (incoming == null || p == null) yield break;

        // fly in neutral space
        var originalParent = incoming.transform.parent;
        incoming.transform.SetParent(AnimRoot, true);
        incoming.transform.position = fromWorld;

        // where to land (world)
        Vector3 toWorld = p.cardsPanel.transform.TransformPoint(slotLocalPos);

        yield return StartCoroutine(FlyWorld(incoming.transform, fromWorld, toWorld, duration));

        incoming.transform.SetParent(p.cardsPanel.transform, false);
        incoming.transform.localPosition = slotLocalPos;
        incoming.transform.localRotation = slotLocalRot;
        incoming.transform.localScale = slotLocalScale;

        if (handIndex >= 0 && handIndex < p.cardsPanel.cards.Count)
            p.cardsPanel.cards[handIndex] = incoming;

        incoming.FlashMarkedOutline();
        if (IsServer) PlayDrawCardSoundClientRpc();

        yield return new WaitForSeconds(2f);
        incoming.IsOpen = false;
    }

    public void ShowTooltipOverlay(string text)
    {
        if (!CardGameManager.ShowTooltips) return;
        if (tooltipParent == null || tooltipText == null) return;

        tooltipText.text = text;
        tooltipParent.SetActive(true);
        tooltipParent.transform.SetAsLastSibling();

        if (tooltipCanvasGroup == null)
            tooltipCanvasGroup = tooltipParent.GetComponent<CanvasGroup>();

        if (tooltipFadeRoutine != null) StopCoroutine(tooltipFadeRoutine);
        tooltipFadeRoutine = StartCoroutine(FadeTooltip(1f));
    }

    public void HideTooltipOverlay()
    {
        if (tooltipParent == null) return;

        if (tooltipCanvasGroup == null)
            tooltipCanvasGroup = tooltipParent.GetComponent<CanvasGroup>();

        if (tooltipFadeRoutine != null) StopCoroutine(tooltipFadeRoutine);
        tooltipFadeRoutine = StartCoroutine(FadeTooltip(0f, () =>
        {
            tooltipParent.SetActive(false);
        }));
    }

    private IEnumerator FadeTooltip(float targetAlpha, System.Action onComplete = null)
    {
        float startAlpha = tooltipCanvasGroup.alpha;
        float time = 0f;

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            tooltipCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / fadeDuration);
            yield return null;
        }

        tooltipCanvasGroup.alpha = targetAlpha;
        onComplete?.Invoke();
    }

    private void WireTooltipClickToClose()
    {
        if (tooltipParent == null) return;


        // Hook: click anywhere on dimmed bg to close
        if (tooltipDimmedButton != null)
        {
            tooltipDimmedButton.transition = Selectable.Transition.None;
            tooltipDimmedButton.onClick.RemoveAllListeners();
            tooltipDimmedButton.onClick.AddListener(HideTooltipOverlay);
        }

        // Hook: clicking the popup itself also closes
        if (tooltipPopupButton != null)
        {
            tooltipPopupButton.transition = Selectable.Transition.None;
            tooltipPopupButton.onClick.RemoveAllListeners();
            tooltipPopupButton.onClick.AddListener(HideTooltipOverlay);
        }

        if (tooltipDimmedButton == null && tooltipPopupButton == null)
            Debug.LogWarning("[Tooltip] No click targets assigned/found. Assign Dimed/Popup Buttons in the Inspector.");
    }

    private void HideNonTopWasteParticles()
    {
        if (cardWastePile == null || cardWastePile.transform.childCount == 0) return;

        Transform prevTop = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1);
        var prevCard = prevTop ? prevTop.GetComponent<Card>() : null;
        if (prevCard == null) return;

        // Special outline particle
        if (prevCard.specialOutline != null && prevCard.specialOutline.transform.childCount > 0)
            prevCard.specialOutline.transform.GetChild(0).gameObject.SetActive(false);

        // Cursed outline particle
        if (prevCard.cursedOutline != null && prevCard.cursedOutline.transform.childCount > 0)
            prevCard.cursedOutline.transform.GetChild(0).gameObject.SetActive(false);
    }

}

[System.Serializable]
public struct SerializableCard : INetworkSerializable
{
    public CardType Type;
    public CardValue Value;
    public bool IsCursed;

    public SerializableCard(CardType t, CardValue v, bool cursed = false)
    { Type = t; Value = v; IsCursed = cursed; }

    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
    {
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref Value);
        serializer.SerializeValue(ref IsCursed);
    }
}

[System.Serializable]
public class AvatarProfile
{
    public int avatarIndex;
    public string avatarName;
}

public class AvatarProfiles
{
    public List<AvatarProfile> profiles;
}
