using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#pragma warning disable 0618

public class GamePlayManager : NetworkBehaviour
{
    [Header("Sound")]
    public AudioClip music_win_clip;
    public AudioClip music_loss_clip;
    public AudioClip draw_card_clip;
    public AudioClip throw_card_clip;
    public AudioClip uno_btn_clip;
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
    private bool isJackRevealPhase = false;
    private bool isPeekingPhase = false;
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
    public float turnTimerDuration = 30f;
    private float turnTimerLeft = 0f;
    private bool isTurnEnding = false;
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
    public AudioClip fiendRevengeVoiceClip;
    public AudioClip jackSpecialVoiceClip;

    public AudioSource _audioSource;

    private HashSet<ulong> readyClientIds = new HashSet<ulong>();

    public WinnerUI winnerUI;
    public TMPro.TextMeshProUGUI remainingCardsCounterText;
    private bool turnEndedByTimeout = false;
    private bool lastTurnEndedByTimeout = false;


    [Header("Bot AI Tuning")]
    [Tooltip("Bot will only consider waste pile if card value is <= this value.")]
    public int botWastePileMinValue = 3;

    [Tooltip("Chance (percent) for bot to draw a minimum card from waste pile (0-100).")]
    [Range(0, 100)]
    public int botWastePileDrawChance = 20;

    [Tooltip("Chance (percent) for bot to do a replace after deck draw (0-100).")]
    [Range(0, 100)]
    public int botDeckReplaceChance = 20;

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

    public bool IsDeckArrow
    {
        get { return arrowObject.activeSelf; }
    }
    public static GamePlayManager instance;

    System.DateTime pauseTime;
    int fastForwardTime = 0;
    bool setup = false, multiplayerLoaded = false, gameOver = false;

    void Start()
    {
        Application.targetFrameRate = 120;
        instance = this;
        Input.multiTouchEnabled = false;
        previousPlayerIndex = -1;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < playerList.Count; i++)
            Debug.Log($"[SeatSetup] Global seat {i}: clientId={playerList[i].clientId} (isMine={NetworkManager.Singleton.LocalClientId == playerList[i].clientId})");
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log("[GamePlayManager] OnNetworkSpawn! Safe to set up player panels.");
        SetupAllPlayerPanels();

        if (!IsHost)
            NotifyReadyServerRpc();
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

    public void StartMultiplayerGame()
    {
        Debug.Log($"[StartMultiplayerGame] Deck has {cards.Count} cards before dealing.");
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

        StartCoroutine(StartPeekingPhase());
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
                card.IsOpen = true;
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

    int MapClientIdToLocalSeat(ulong clientId)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;
        int playerCount = playerList.Count;
        for (int localSeat = 0; localSeat < playerCount; localSeat++)
            if (playerList[(myGlobalSeat + localSeat) % playerCount].clientId == clientId)
                return localSeat;
        return -1; // Not found
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

    }
    public void CreateDeck()
    {
        cards = new List<SerializableCard>();
        wasteCards = new List<SerializableCard>();

        List<CardValue> values = new List<CardValue>
{
     
     CardValue.Jack,CardValue.King, CardValue.Fiend,
};

        for (int j = 0; j < 4; j++)
        {
            foreach (var val in values)
            {
                var card = new SerializableCard((CardType)j, val);
                cards.Add(card);
            }
        }
        // add 4 more king and 4 more fiend
        for (int j = 0; j < 4; j++)
        {
            cards.Add(new SerializableCard(CardType.Other, CardValue.King));
            cards.Add(new SerializableCard(CardType.Other, CardValue.Fiend));
        }
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        cards.Add(new SerializableCard(CardType.Other, CardValue.Zero));
        Debug.Log($"[CreateDeck] Deck created with {cards.Count} cards.");
        UpdateRemainingCardsCounter();
    }

    private IEnumerator StartPeekingPhase()
    {
        isPeekingPhase = true;

        float peekTime = 4f;
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


        if (IsHost)
        {
            currentPlayerIndex = 0;
            StartPlayerTurnForAllClientRpc(currentPlayerIndex);
        }
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
    public int GetGlobalIndexFromLocal(int localIndex)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;
        int playerCount = playerList.Count;
        return (myGlobalSeat + localIndex) % playerCount;
    }

    public int GetLocalIndexFromGlobal(int globalIndex)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;
        int playerCount = playerList.Count;
        for (int localIndex = 0; localIndex < playerCount; localIndex++)
            if ((myGlobalSeat + localIndex) % playerCount == globalIndex)
                return localIndex;
        return 0;
    }
    public void NextPlayerIndex()
    {
        int step = clockwiseTurn ? 1 : -1;
        do
        {
            currentPlayerIndex = Mod(currentPlayerIndex + step, players.Count);
        } while (!players[currentPlayerIndex].isInRoom);
    }

    public bool IsMyTurn()
    {
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalIndex = -1;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalIndex = i;
        return (myGlobalIndex == GetGlobalIndexFromLocal(currentPlayerIndex));
    }

    [ClientRpc]
    public void StartPlayerTurnForAllClientRpc(int globalPlayerIndex)
    {
        if (Fiend.Instance != null)
            Fiend.Instance.HideFiendPopup();
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
        isJackRevealPhase = false;
        ulong curClientId = MultiplayerManager.Instance.playerDataNetworkList[GetGlobalIndexFromLocal(currentPlayerIndex)].clientId;
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
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalIndex = -1;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalIndex = i;

        if (myGlobalIndex == globalPlayerIndex && players[0].isUserPlayer)
        {
            deckInteractionLocked = false;
            EnableDeckClick();
            UpdateDeckClickability();

            if (ShouldShowWasteArrow())
                arrowObject2.SetActive(true);
            else
                arrowObject2.SetActive(false);
        }
        else
        {
            arrowObject.SetActive(false);
            arrowObject2.SetActive(false);
            UpdateDeckClickability();
        }

        RefreshWasteInteractivity();

        if (IsHost)
        {
            if (turnTimeoutCoroutine != null)
            {
                StopCoroutine(turnTimeoutCoroutine);
                turnTimeoutCoroutine = null;
            }
            turnTimeoutCoroutine = StartCoroutine(HostTurnTimeoutRoutine());
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
        if (_audioSource != null && draw_card_clip != null)
            _audioSource.PlayOneShot(draw_card_clip);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestWasteCardSwapServerRpc(int handIndex, SerializableCard newCard, SerializableCard replacedCard, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
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

        ReplaceHandCardClientRpc(playerIndex, handIndex, newCard, replacedCard,
            cards.ToArray(), wasteCards.ToArray(), true);

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

    void OnJackCardDiscardedByMe()
    {
        if (!IsMyTurn() || !players[0].isUserPlayer)
        {
            Debug.LogWarning("OnJackCardDiscardedByMe called but not my turn or not user player!");
            return;
        }
        isJackRevealPhase = true;
        for (int seat = 0; seat < players.Count; seat++)
        {
            var p = players[seat];
            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
            {
                var handCard = p.cardsPanel.cards[i];
                handCard.ShowGlow(true);
                handCard.IsClickable = true;
                int s = seat, idx = i;
                handCard.onClick = null;
                handCard.onClick = (clickedCard) =>
                {
                    if (!isJackRevealPhase) return;
                    isJackRevealPhase = false;
                    DisableAllHandCardGlowAllPlayers();
                    int globalPlayerIndex = GetGlobalIndexFromLocal(s);
                    RequestRevealHandCardServerRpc(globalPlayerIndex, idx);
                    UpdateDeckClickability();
                };

            }
        }
        if (players[0].isUserPlayer)
        {
            players[0].SetTimerVisible(true);
            players[0].UpdateTurnTimerUI(turnTimerDuration, turnTimerDuration);
        }
    }

    IEnumerator HideCardAfterDelay(Card card, float delay)
    {
        yield return new WaitForSeconds(delay);
        card.IsOpen = false;
    }

    [ClientRpc]
    void RevealHandCardClientRpc(
     int playerIndex, int cardIndex, CardType type, CardValue value, ulong jackUserClientId,
     ClientRpcParams rpcParams = default)
    {
        if (IsBotClientId(jackUserClientId))
        {
            Debug.LogError("BUG: Bot Jack reveal triggered RevealHandCardClientRpc!");
            return;
        }
        Debug.Log($"RevealHandCardClientRpc: local={NetworkManager.Singleton.LocalClientId} jack={jackUserClientId} botJack={IsBotClientId(jackUserClientId)}");

        if (IsBotClientId(NetworkManager.Singleton.LocalClientId))
            return;

        if (NetworkManager.Singleton.LocalClientId != jackUserClientId)
            return;
        if (IsBotClientId(jackUserClientId))
            return;

        var p = players[GetLocalIndexFromGlobal(playerIndex)];
        var card = p.cardsPanel.cards[cardIndex];
        card.Type = type;
        card.Value = value;
        card.IsOpen = true;
        StartCoroutine(HideCardAfterDelay(card, 1f));
    }



    private ulong currentJackUserClientId = ulong.MaxValue;
    private bool currentJackUserIsBot = false;

    [ClientRpc]
    void PlayJackZeroBonusSoundClientRpc(ClientRpcParams rpcParams = default)
    {
        if (_audioSource != null && jackSpecialVoiceClip != null)
            _audioSource.PlayOneShot(jackSpecialVoiceClip);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealHandCardServerRpc(int playerIndex, int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();

        var handCard = players[playerIndex].cardsPanel.cards[cardIndex];
        ulong jackUserClientId = serverRpcParams.Receive.SenderClientId;
        if (IsBotClientId(jackUserClientId))
        {
            Debug.LogWarning("RequestRevealHandCardServerRpc called for bot—this should never happen! Please check SimulateBotJackReveal.");
            FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, true);
            OnJackRevealDoneServerRpc();
            return;
        }

        // Reveal for Jack user only
        RevealHandCardClientRpc(
            playerIndex, cardIndex, handCard.Type, handCard.Value, jackUserClientId,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { jackUserClientId }
                }
            }
        );
        // Reveal for others: flash
        FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, false);

        // Now delay before exposing the Zero
        StartCoroutine(ExposeZeroAfterDelayCoroutine(jackUserClientId));
    }


    private IEnumerator ExposeZeroAfterDelayCoroutine(ulong jackUserClientId)
    {
        yield return new WaitForSeconds(1.0f);

        // JACK SPECIAL BONUS: Find the player (not Jack user) who has the Zero
        int zeroHolderPlayerIndex = -1;
        int zeroHolderCardIndex = -1;

        for (int pi = 0; pi < players.Count; pi++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[GetGlobalIndexFromLocal(pi)].clientId;
            if (pClientId == jackUserClientId) continue; // skip Jack user

            for (int ci = 0; ci < players[pi].cardsPanel.cards.Count; ci++)
            {
                var card = players[pi].cardsPanel.cards[ci];
                if (card == null) continue;
                if (card.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    zeroHolderCardIndex = ci;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            // Play sound for all real clients (not bots), including Jack user
            var allRealClientIds = new List<ulong>();
            foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
            {
                if (!IsBotClientId(pd.clientId)) // Only real clients
                    allRealClientIds.Add(pd.clientId);
            }

            PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = allRealClientIds }
            });

            ShowZeroHolderTimerEffectClientRpc(GetGlobalIndexFromLocal(zeroHolderPlayerIndex));

            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            OnJackRevealDoneServerRpc();
        }
    }


    [ClientRpc]
    void ShowZeroHolderTimerEffectClientRpc(int globalPlayerIndex)
    {
        // bounds check
        int localIndex = GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex < 0 || localIndex >= players.Count) return;
        var player = players[localIndex];

        if (player.timerOjbect != null)
        {
            player.timerOjbect.SetActive(true);
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = Color.red;

            StartCoroutine(RestoreTimerEffectAfterDelay(player, 3.0f));
        }
    }

    private IEnumerator RestoreTimerEffectAfterDelay(Player2 player, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (player.timerOjbect != null)
        {
            // Restore color 
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = new Color(0.572f, 0.463f, 0.125f, 1f);

            player.timerOjbect.SetActive(false);
        }
    }

    private IEnumerator JackZeroBonusEndTurnCoroutine()
    {
        yield return new WaitForSeconds(3.0f);
        OnJackRevealDoneServerRpc();
    }

    [ClientRpc]
    void FlashMarkedOutlineClientRpc(int playerIndex, int cardIndex, ulong jackUserClientId, bool jackUserIsBot, ClientRpcParams rpcParams = default)
    {
        // If Jack user is a bot, everyone sees only the flash.
        if (!jackUserIsBot && NetworkManager.Singleton.LocalClientId == jackUserClientId)
            return; // Jack user skips flash—they see the value already

        var p = players[GetLocalIndexFromGlobal(playerIndex)];
        var card = p.cardsPanel.cards[cardIndex];

        card.IsOpen = false;
        card.FlashEyeOutline();
    }

    [ServerRpc(RequireOwnership = false)]
    void OnJackRevealDoneServerRpc(ServerRpcParams rpcParams = default)
    {
        isJackRevealPhase = false;

        if (IsHost)
            StartCoroutine(DelayedNextPlayerTurn(1.0f));

        currentJackUserClientId = ulong.MaxValue;
        currentJackUserIsBot = false;
    }

    private void ResetAndRestartTurnTimerCoroutine()
    {
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        turnTimerLeft = turnTimerDuration;
        turnTimeoutCoroutine = StartCoroutine(HostTurnTimeoutRoutine());
    }

    void DisableAllHandCardGlowAllPlayers()
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
    void StartJackRevealLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[JackReveal] I am {NetworkManager.Singleton.LocalClientId} (IsHost={IsHost}), Jack user is {clientId}");

        if (NetworkManager.Singleton.LocalClientId != clientId)
            return;

        Debug.Log("Running OnJackCardDiscardedByMe!");
        OnJackCardDiscardedByMe();
    }


    [ClientRpc]
    public void EndTurnForAllClientRpc()
    {
        foreach (var p in players)
            p.OnTurnEnd();
    }

    public void NextPlayerTurn()
    {
        if (!IsHost) return;
        NextPlayerIndex();
        int globalIndex = GetGlobalIndexFromLocal(currentPlayerIndex);
        StartPlayerTurnForAllClientRpc(globalIndex);

        if (IsCurrentPlayerBot())
        {
            StartCoroutine(RunBotTurn(globalIndex));
        }
    }

    public IEnumerator RunBotTurn(int botGlobalIndex)
    {
        if (Fiend.Instance != null)
            Fiend.Instance.HideFiendPopup();
        int localBotIndex = GetLocalIndexFromGlobal(botGlobalIndex);
        ulong botClientId = MultiplayerManager.Instance.playerDataNetworkList[botGlobalIndex].clientId;

        float waitBefore = Random.Range(1f, 2f);
        yield return new WaitForSeconds(waitBefore);

        if (cards.Count == 0)
            yield break;

        // Try Waste Pile Steal
        Card wasteCard = GetTopWasteCard();
        bool canTakeWaste = false;
        SerializableCard wasteSer = default;

        if (wasteCard != null && wasteCard.killedOutline != null && !wasteCard.killedOutline.activeSelf)
        {
            // Only if top waste card value is <= botWastePileMinValue, and within chance
            if ((int)wasteCard.Value <= botWastePileMinValue && Random.Range(0, 100) < botWastePileDrawChance)
            {
                canTakeWaste = true;
                wasteSer = new SerializableCard(wasteCard.Type, wasteCard.Value);
            }
        }

        if (canTakeWaste)
        {
            float delay = Random.Range(1f, 2f);
            yield return new WaitForSeconds(delay);

            var botHand = players[localBotIndex].cardsPanel.cards;
            int swapHandIdx = Random.Range(0, botHand.Count);

            peekedCardsByClientId[botClientId] = wasteSer;

            if (NetworkManager.Singleton.IsHost)
            {
                SerializableCard replacedCard = new SerializableCard(botHand[swapHandIdx].Type, botHand[swapHandIdx].Value);
                DoWasteCardSwapForBot(localBotIndex, swapHandIdx, wasteSer, replacedCard);
            }

            yield break;
        }

        float roll = Random.Range(0, 100);
        bool doReplace = roll < botDeckReplaceChance;

        if (cards.Count == 0) yield break;
        SerializableCard topDeckCard = cards[cards.Count - 1];
        peekedCardsByClientId[botClientId] = topDeckCard;

        float delay2 = Random.Range(1f, 2f);
        yield return new WaitForSeconds(delay2);

        if (doReplace)
        {
            var botHand = players[localBotIndex].cardsPanel.cards;
            int replaceIdx = Random.Range(0, botHand.Count);

            if (NetworkManager.Singleton.IsHost)
            {
                DoDeckReplaceForBot(localBotIndex, replaceIdx);
            }
        }
        else
        {
            RequestDiscardPeekedCardServerRpc(topDeckCard.Value, botClientId);
        }
    }


    // Host-only bot deck replace
    private void DoDeckReplaceForBot(int botLocalIndex, int handIndex)
    {
        int playerIndex = botLocalIndex;
        if (cards.Count == 0) return;

        // Draw the top card
        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);
        UpdateRemainingCardsCounter();

        Player2 p = players[playerIndex];
        Card replacedCard = p.cardsPanel.cards[handIndex];
        SerializableCard replacedSer = new SerializableCard(replacedCard.Type, replacedCard.Value);

        wasteCards.Add(replacedSer);

        ReplaceHandCardClientRpc(
            playerIndex, handIndex, drawn, replacedSer,
            cards.ToArray(), wasteCards.ToArray(), false
        );
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();
        StartCoroutine(WaitThenAdvanceBotTurn());
    }

    private IEnumerator WaitThenAdvanceBotTurn()
    {
        yield return new WaitForSeconds(2.0f);
        EndTurnAndGoNextPlayer();
    }


    // host-only, no ServerRpc
    private void DoWasteCardSwapForBot(int botLocalIndex, int handIndex, SerializableCard newCard, SerializableCard replacedCard)
    {
        int playerIndex = botLocalIndex;

        wasteCards.Add(replacedCard);

        ReplaceHandCardClientRpc(
            playerIndex,
            handIndex,
            newCard,
            replacedCard,
            cards.ToArray(),
            wasteCards.ToArray(),
            true
        );

        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }
        FreezeTimerUI();
        StartCoroutine(WaitThenAdvanceBotTurn());

        RemoveTopWasteCardClientRpc();

        ulong botClientId = MultiplayerManager.Instance.playerDataNetworkList[playerIndex].clientId;
        peekedCardsByClientId.Remove(botClientId);
        hasPeekedCard = false;
        peekedCard = null;
        wasteInteractionStarted = false;
    }

    public bool IsCurrentPlayerBot()
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int globalIndex = GetGlobalIndexFromLocal(currentPlayerIndex);
        if (globalIndex < 0 || globalIndex >= playerList.Count) return false;
        return playerList[globalIndex].clientId >= 9000;
    }

    public int GetPlayerIndexFromClientId(ulong clientId)
    {
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == clientId)
                return i;
        Debug.LogError($"[GetPlayerIndexFromClientId] Could not find clientId={clientId} in playerList!");
        return -1;
    }

    public void EnableDeckClick()
    {
        arrowObject.SetActive(true);
    }

    [ClientRpc]
    void BroadcastTurnTimerClientRpc(float secondsLeft, float totalSeconds)
    {
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
        for (int i = 0; i < players.Count; i++)
        {
            players[i].SetTimerVisible(i == playerIndex);
            players[i].UpdateTurnTimerUI(totalSeconds, totalSeconds);
        }
    }

    private IEnumerator HostTurnTimeoutRoutine()
    {
        turnTimerLeft = turnTimerDuration;
        while (turnTimerLeft > 0f)
        {
            BroadcastTurnTimerClientRpc(turnTimerLeft, turnTimerDuration);
            yield return new WaitForSeconds(0.1f);
            turnTimerLeft -= 0.1f;
        }
        BroadcastTurnTimerClientRpc(0f, turnTimerDuration);

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        ulong currentTurnClientId = playerList[GetGlobalIndexFromLocal(currentPlayerIndex)].clientId;

        turnEndedByTimeout = true;
        ShowTimeoutMessageClientRpc(currentPlayerIndex);

        if (peekedCardsByClientId.ContainsKey(currentTurnClientId))
        {
            var serCard = peekedCardsByClientId[currentTurnClientId];
            DiscardPeekedCardServerRpc((int)serCard.Type, (int)serCard.Value);
            yield return new WaitForSeconds(0.3f);
        }

        if (isTurnEnding) yield break;

        yield return new WaitForSeconds(1f);
        EndTurnAndGoNextPlayer();

    }


    public void UpdateDeckClickability()
    {
        if (isJackRevealPhase || isPeekingPhase)
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

    [ClientRpc]
    public void UpdateDeckVisualClientRpc(SerializableCard[] deckCards)
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
            card.name = $"{sc.Type}_{sc.Value}";

            card.transform.localPosition = new Vector3(
                Random.Range(-2f, 2f),
                0,
                -i * 1.15f
            );
        }

        UpdateDeckClickability();
        UpdateRemainingCardsCounter();
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

        var peekedSerCard = new SerializableCard(card.Type, card.Value);
        peekedCardsByClientId[myClientId] = peekedSerCard;

        if (!IsHost || (IsHost && !NetworkManager.Singleton.IsServer))
            NotifyHostPeekedCardServerRpc((int)card.Type, (int)card.Value);
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

        ShowWastePileCardClientRpc(cardType, cardValue);

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
        UpdateDeckVisualClientRpc(deckCards);
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

        if (drawn.Value == CardValue.Jack || drawn.Value == CardValue.Queen || drawn.Value == CardValue.King || drawn.Value == CardValue.Fiend)
        {
            PlaySpecialCardVoiceClientRpc((int)drawn.Value);
        }

        DiscardPeekedCardWithVisualClientRpc(playerIndex, drawn, cards.ToArray(), wasteCards.ToArray());

        if (discardValue == CardValue.Jack)
        {
            ShowPowerMessageClientRpc(playerIndex, "Jack's Power", 1.5f);
            ResetTurnTimerClientRpc(playerIndex, turnTimerDuration);

            if (IsHost)
                ResetAndRestartTurnTimerCoroutine();
            Debug.Log($"[RequestDiscardPeekedCardServerRpc] senderClientId={senderClientId} IsBot={IsBotClientId(senderClientId)} IsHost={IsHost}");
            // For bots, force host to run SimulateBotJackReveal directly, skip ClientRpc entirely

            if (IsBotClientId(senderClientId))
            {
                if (IsHost)
                    StartCoroutine(SimulateBotJackReveal(senderClientId));
            }
            else
            {
                // For humans, call the ClientRpc as before (target just the clientId)
                StartJackRevealLocalOnlyClientRpc(
                    senderClientId,
                    new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { senderClientId } }
                    }
                );
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
                // just a normal discard, no timer reset
                StartCoroutine(DelayedNextPlayerTurn(1.0f));
                return;
            }

            // normal king phase
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

        else if (discardValue == CardValue.Skip)
        {
            if (IsHost && Skip.Instance != null)
                Skip.Instance.TriggerSkip();

            return;
        }

        if (IsHost)
        {
            float delay = 1f;
            StartCoroutine(DelayedNextPlayerTurn(delay));
        }
        else
            EndTurnForAllClientRpc();

    }

    private bool IsBotClientId(ulong clientId)
    {
        return clientId >= 9000;
    }

    [ClientRpc]
    void PlaySpecialCardVoiceClientRpc(int cardValue)
    {
        if (_audioSource == null) return;
        AudioClip clip = null;
        switch ((CardValue)cardValue)
        {
            case CardValue.Jack: clip = jackVoiceClip; break;
            case CardValue.Queen: clip = queenVoiceClip; break;
            case CardValue.King: clip = kingVoiceClip; break;
            case CardValue.Fiend: clip = fiendVoiceClip; break;
        }
        if (clip != null)
        {
            _audioSource.volume = 0.4f;
            _audioSource.PlayOneShot(clip);
        }
    }

    private bool botJackRevealActive = false;

    private IEnumerator SimulateBotJackReveal(ulong botClientId)
    {
        if (botJackRevealActive)
        {
            Debug.LogWarning("SimulateBotJackReveal: Already active, ignoring duplicate call.");
            yield break;
        }
        botJackRevealActive = true;

        Debug.Log($"[SimulateBotJackReveal] botClientId={botClientId}");
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        // Pick random target (any player except the bot itself)
        List<int> validTargets = new List<int>();
        for (int i = 0; i < players.Count; i++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[GetGlobalIndexFromLocal(i)].clientId;
            if (players[i].isInRoom && players[i].cardsPanel.cards.Count > 0 && pClientId != botClientId)
                validTargets.Add(i);
        }
        if (validTargets.Count == 0)
        {
            botJackRevealActive = false;
            yield break;
        }
        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];
        int targetCardIndex = Random.Range(0, players[targetPlayerIndex].cardsPanel.cards.Count);


        // Flash eye outline to ALL real players except the bot (the one being Jack).
        List<ulong> realClientIds = new List<ulong>();
        foreach (var pd in MultiplayerManager.Instance.playerDataNetworkList)
        {
            if (!IsBotClientId(pd.clientId))
                realClientIds.Add(pd.clientId);
        }
        // Send FlashMarkedOutlineClientRpc to all real players
        FlashMarkedOutlineClientRpc(targetPlayerIndex, targetCardIndex, botClientId, true,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = realClientIds }
            }
        );

        FreezeTimerUI();
        if (turnTimeoutCoroutine != null)
        {
            StopCoroutine(turnTimeoutCoroutine);
            turnTimeoutCoroutine = null;
        }

        yield return new WaitForSeconds(1.0f);

        // JACK SPECIAL BONUS: Find any player (except the bot himself) holding a Zero card.
        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < players.Count; pi++)
        {
            ulong pClientId = MultiplayerManager.Instance.playerDataNetworkList[GetGlobalIndexFromLocal(pi)].clientId;
            if (pClientId == botClientId) continue; // skip Jack user

            for (int ci = 0; ci < players[pi].cardsPanel.cards.Count; ci++)
            {
                var card = players[pi].cardsPanel.cards[ci];
                if (card == null) continue;
                if (card.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        // 3. If found, show timer effect and play sound for all real players (including the one who holds Zero)
        if (zeroHolderPlayerIndex != -1)
        {
            PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = realClientIds }
            });

            ShowZeroHolderTimerEffectClientRpc(GetGlobalIndexFromLocal(zeroHolderPlayerIndex));
            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
            OnJackRevealDoneServerRpc();
        }

        botJackRevealActive = false;
    }

    [ClientRpc]
    void StartFiendPopupLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        Fiend.Instance.ShowFiendPopup();
    }

    [ClientRpc]
    void StartQueenSwapLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        Queen.Instance.StartQueenSwap();
    }

    [ClientRpc]
    void StartKingGlowLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        King.Instance.StartKingPhase();
    }


    [ClientRpc]
    void DiscardPeekedCardWithVisualClientRpc(int playerIndex, SerializableCard discardedCard, SerializableCard[] deck, SerializableCard[] wastePile)
    {
        if (cardWastePile.transform.childCount > 0)
        {
            Transform prevTop = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1);
            Card prevCard = prevTop.GetComponent<Card>();
            if (prevCard != null && prevCard.specialOutline != null)
                prevCard.specialOutline.transform.GetChild(0).gameObject.SetActive(false);
        }
        if (peekedCard != null)
        {
            Destroy(peekedCard.gameObject);
            peekedCard = null;
            hasPeekedCard = false;
        }

        var wasteObj = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, cardWastePile.transform);
        wasteObj.gameObject.AddComponent<WastePile>().Initialize(wasteObj);
        wasteObj.Type = discardedCard.Type;
        wasteObj.Value = discardedCard.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();
        wasteObj.name = $"{wasteObj.Type}_{wasteObj.Value}";

        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(wasteObj, cardDeckTransform.position, cardWastePile.transform.position, 0.3f, randomRot));
        UpdateDeckVisualClientRpc(deck);
        OnUnoClick();
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
            card.transform.SetParent(cardWastePile.transform, true);
            card.transform.SetAsLastSibling();
            card.transform.localPosition = Vector3.zero;
            RefreshWasteInteractivity();

            if (IsServer)
            {
                PlayDrawCardSoundClientRpc();
            }
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
        int playerIndex = GetPlayerIndexFromClientId(senderClientId);
        if (playerIndex < 0 || playerIndex >= players.Count) return;
        if (cards.Count == 0) return;

        SerializableCard drawn = cards[cards.Count - 1];
        cards.RemoveAt(cards.Count - 1);
        UpdateRemainingCardsCounter();

        Player2 p = players[playerIndex];
        Card replacedCard = p.cardsPanel.cards[handIndex];
        SerializableCard replacedSer = new SerializableCard(replacedCard.Type, replacedCard.Value);

        wasteCards.Add(replacedSer);

        ReplaceHandCardClientRpc(playerIndex, handIndex, drawn, replacedSer, cards.ToArray(), wasteCards.ToArray(), false);
    }

    public IEnumerator DelayedNextPlayerTurn(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (isTurnEnding) yield break;
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

        if (cardWastePile.transform.childCount > 0)
        {
            Transform prevTop = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1);
            Card prevCard = prevTop.GetComponent<Card>();
            if (prevCard != null && prevCard.specialOutline != null)
                prevCard.specialOutline.transform.GetChild(0).gameObject.SetActive(false);
        }
        int localSeat = GetLocalIndexFromGlobal(playerIndex);
        var p = players[localSeat];

        Vector3 handSlotPos = p.cardsPanel.cards[handIndex].transform.position;
        Card cardToRemove = p.cardsPanel.cards[handIndex];
        p.cardsPanel.cards[handIndex] = null;
        Destroy(cardToRemove.gameObject);

        // Animate waste card flying to waste pile
        var wasteObj = Instantiate(_cardPrefab, handSlotPos, Quaternion.identity, cardWastePile.transform.parent);
        wasteObj.Type = waste.Type;
        wasteObj.Value = waste.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();

        var wp = wasteObj.gameObject.AddComponent<WastePile>();
        wp.Initialize(wasteObj);
        RefreshWasteInteractivity();
        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(wasteObj, handSlotPos, cardWastePile.transform.position, 0.5f, randomRot));

        Vector3 fromPos = fromWastePile ? cardWastePile.transform.position : cardDeckTransform.position;
        GameObject newCardGO = Instantiate(_cardPrefab.gameObject, fromPos, Quaternion.identity, p.cardsPanel.transform.parent);
        Card newCardVisual = newCardGO.GetComponent<Card>();
        newCardVisual.Type = newCard.Type;
        newCardVisual.Value = newCard.Value;

        if (fromWastePile) newCardVisual.IsOpen = true;
        else newCardVisual.IsOpen = p.isUserPlayer;
        newCardVisual.CalcPoint();

        UpdateDeckVisualClientRpc(deck);

        float animDuration = fromWastePile ? 0.8f : 0.5f;

        StartCoroutine(AnimateHandCardReplace(
            p, handIndex, newCardVisual, newCardGO, fromPos, handSlotPos, animDuration, playerIndex, fromWastePile
        ));


        ShowReplacedMessageClientRpc(playerIndex);
        DisableUnoBtn();
        if (turnEndedByTimeout)
        {
            turnEndedByTimeout = false;
            return;
        }
        CardGameManager.PlaySound(uno_btn_clip);
    }

    IEnumerator AnimateHandCardReplace(Player2 p, int handIndex, Card newCardVisual, GameObject newCardGO,
    Vector3 fromPos, Vector3 handSlotPos, float animDuration, int playerIndex, bool fromWastePile)
    {
        yield return StartCoroutine(AnimateCardMove(newCardVisual, fromPos, handSlotPos, animDuration));
        Destroy(newCardGO, 0.01f);

        p.AddSerializableCard(new SerializableCard(newCardVisual.Type, newCardVisual.Value), handIndex);

        yield return null;

        if (IsServer)
            PlayDrawCardSoundClientRpc();

        Card c = p.cardsPanel.cards[handIndex];
        if (c != null)
            c.FlashMarkedOutline();

        float flashDuration = 2f;
        yield return new WaitForSeconds(flashDuration);

        if (c != null)
            c.IsOpen = false;

        if (IsHost && cards.Count == 0)
        {
            unoBtn.SetActive(false);
            arrowObject.SetActive(false);
            arrowObject2.SetActive(false);
        }

        if (IsHost)
        {
            // Only advance here if not a bot
            var playerList = MultiplayerManager.Instance.playerDataNetworkList;
            int globalIndex = playerIndex;
            bool isBot = (globalIndex >= 0 && globalIndex < playerList.Count && playerList[globalIndex].clientId >= 9000);

            if (!isBot)
            {
                StartCoroutine(DelayedNextPlayerTurn(0f));
            }

        }

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
    void ShowWastePileCardClientRpc(int cardType, int cardValue)
    {
        if (cardWastePile.transform.childCount > 0)
        {
            Transform prevTop = cardWastePile.transform.GetChild(cardWastePile.transform.childCount - 1);
            Card prevCard = prevTop.GetComponent<Card>();
            if (prevCard != null && prevCard.specialOutline != null)
                prevCard.specialOutline.transform.GetChild(0).gameObject.SetActive(false);
        }
        var discard = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, cardWastePile.transform);

        discard.Type = (CardType)cardType;
        discard.Value = (CardValue)cardValue;
        discard.IsOpen = true;
        discard.CalcPoint();
        discard.name = $"{discard.Type}_{discard.Value}";
        discard.IsClickable = false;

        var wp = discard.gameObject.AddComponent<WastePile>();
        wp.Initialize(discard);



        float randomRot = Random.Range(-50, 50f);
        StartCoroutine(AnimateCardMove(discard, cardDeckTransform.position, cardWastePile.transform.position, 0.3f, randomRot));
        OnUnoClick();
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

        SerializableCard serializableDiscard = new SerializableCard(c.Type, c.Value);
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
        gameOver = true;
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
                    .Select(c => new SerializableCard(c.Type, c.Value))
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
        for (int seat = 0; seat < players.Count; seat++)
        {
            ulong clientId = MultiplayerManager.Instance.playerDataNetworkList[seat].clientId;
            int sortedIndex = sorted.IndexOf(players[seat]);
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

    public void OnUnoClick()
    {
        DisableUnoBtn();
        if (CurrentPlayer.wasTimeout)
        { 
            CurrentPlayer.wasTimeout = false;
            return;
        }
        CurrentPlayer.ShowMessage("Discarded", true);
        CurrentPlayer.unoClicked = true;
        CardGameManager.PlaySound(uno_btn_clip);
    }


    public void FreezeTimerUI()
    {
        BroadcastTurnTimerClientRpc(turnTimerLeft, turnTimerDuration);
    }

    private bool IsSpecialCard(CardValue value)
    {
        return value == CardValue.King || value == CardValue.Queen || value == CardValue.Jack || value == CardValue.Fiend;
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
            players[localIndex].ShowMessage("Timeout", true, 1f);
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
            players[localIndex].ShowMessage("Replaced", true, 1.1f);
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
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

}

[System.Serializable]
public struct SerializableCard : INetworkSerializable
{
    public CardType Type;
    public CardValue Value;
    public SerializableCard(CardType t, CardValue v) { Type = t; Value = v; }

    public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
    {
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref Value);
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