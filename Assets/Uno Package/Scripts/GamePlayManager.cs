using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
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

    [Header("Gameplay")]
    [Range(0, 100)]
    public int LeftRoomProbability = 10;
    [Range(0, 100)]
    public int UnoProbability = 70;
    [Range(0, 100)]
    public int LowercaseNameProbability = 30;

    public float cardDealTime = 3f;
    public Card _cardPrefab;
    public Transform cardDeckTransform;
    public Image cardWastePile;
    public GameObject arrowObject, unoBtn, cardDeckBtn;
    public Popup colorChoose, playerChoose, noNetwork;
    public GameObject loadingView, rayCastBlocker;
    public Animator cardEffectAnimator;
    public ParticleSystem wildCardParticle;
    public GameObject menuButton;

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

    private List<Card> cards;
    private List<Card> wasteCards;

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
        instance = this;
        Input.multiTouchEnabled = false;
    }


    void CreateDeck()
    {
        cards = new List<Card>();
        wasteCards = new List<Card>();
        for (int j = 1; j <= 4; j++)
        {
            cards.Add(CreateCardOnDeck(CardType.Other, CardValue.Wild));
            cards.Add(CreateCardOnDeck(CardType.Other, CardValue.DrawFour));
        }
        for (int i = 0; i <= 12; i++)
        {
            for (int j = 1; j <= 4; j++)
            {
                cards.Add(CreateCardOnDeck((CardType)j, (CardValue)i));
                cards.Add(CreateCardOnDeck((CardType)j, (CardValue)i));
            }
        }
    }

    Card CreateCardOnDeck(CardType t, CardValue v)
    {
        Card temp = Instantiate(_cardPrefab, cardDeckTransform);


        temp.Type = t;
        temp.Value = v;
        temp.IsOpen = false;
        temp.CalcPoint();
        temp.name = t.ToString() + "_" + v.ToString();


        return temp;
    }

    [ClientRpc]
    void DealCardsClientRpc(SerializableCard[] allCardsFlat, int cardsPerPlayer, int playerCount)
    {
        // Determine my global seat index
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;

        // Unflatten to per-hand but remap order to local player list
        List<List<SerializableCard>> allCards = new List<List<SerializableCard>>();
        for (int localSeat = 0; localSeat < playerCount; localSeat++)
        {
            int globalSeat = (myGlobalSeat + localSeat) % playerCount;
            var hand = new List<SerializableCard>();
            for (int c = 0; c < cardsPerPlayer; c++)
                hand.Add(allCardsFlat[globalSeat * cardsPerPlayer + c]);
            allCards.Add(hand);
        }
        StartCoroutine(DealCardsAnimated(allCards, cardsPerPlayer, playerCount));
    }


    IEnumerator DealCardsAnimated(List<List<SerializableCard>> allCards, int cardsPerPlayer, int playerCount)
    {
        for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
        {
            for (int seat = 0; seat < playerCount; seat++)
            {
                var player = players[seat];
                var sc = allCards[seat][cardSlot];
                var card = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, player.cardsPanel.transform);
                card.Type = sc.Type;
                card.Value = sc.Value;
                card.IsOpen = (seat == 0);
                card.CalcPoint();
                card.name = $"{sc.Type}_{sc.Value}";
                player.AddCard(card);
                card.transform.SetSiblingIndex(cardSlot);
                card.localSeat = seat;
                card.cardIndex = cardSlot;
                card.IsClickable = true;
                card.onClick = OnAnyCardClicked;

                CardGameManager.PlaySound(throw_card_clip);
            }
            yield return new WaitForSeconds(cardDealTime);
        }

        for (int seat = 0; seat < playerCount; seat++)
        {
            players[seat].cardsPanel.UpdatePos();
        }
        Canvas.ForceUpdateCanvases();
    }



    IEnumerator DealCardsToPlayer(Player2 p, int NoOfCard = 1, float delay = 0f)
    {
        yield return new WaitForSeconds(delay);
        for (int t = 0; t < NoOfCard; t++)
        {
            PickCardFromDeck(p, true);
            yield return new WaitForSeconds(cardDealTime);
        }
    }

    public Card PickCardFromDeck(Player2 p, bool updatePos = false)
    {
        if (cards.Count == 0)
        {
            print("Card Over");
            while (wasteCards.Count > 5)
            {
                cards.Add(wasteCards[0]);
                wasteCards[0].transform.SetParent(cardDeckTransform);
                wasteCards[0].transform.localPosition = Vector3.zero;
                wasteCards[0].transform.localRotation = Quaternion.Euler(Vector3.zero);
                wasteCards[0].IsOpen = false;
                wasteCards.RemoveAt(0);
            }
        }
        Card temp = cards[0];
        p.AddCard(cards[0]);
        cards[0].IsOpen = p.isUserPlayer;
        if (updatePos)
            p.cardsPanel.UpdatePos();
        else
            cards[0].SetTargetPosAndRot(Vector3.zero, 0f);
        cards.RemoveAt(0);
        CardGameManager.PlaySound(throw_card_clip);
        return temp;

    }

    public void PutCardToWastePile(Card c, Player2 p = null)
    {
        if (p != null)
        {
            p.RemoveCard(c);
            if (p.cardsPanel.cards.Count == 1 && !p.unoClicked)
            {
                ApplyUnoCharge(CurrentPlayer);
            }
            CardGameManager.PlaySound(draw_card_clip);
        }

        CurrentType = c.Type;
        CurrentValue = c.Value;
        wasteCards.Add(c);
        c.IsOpen = true;
        c.transform.SetParent(cardWastePile.transform, true);
        c.SetTargetPosAndRot(new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 1), c.transform.localRotation.eulerAngles.z + Random.Range(-15f, 15f));

        if (p != null)
        {
            if (p.cardsPanel.cards.Count == 0)
            {
                Invoke("SetupGameOver", 2f);
                return;
            }
            if (c.Type == CardType.Other)
            {
                CurrentPlayer.Timer = true;
                CurrentPlayer.choosingColor = true;
                if (CurrentPlayer.isUserPlayer)
                {
                    colorChoose.ShowPopup();
                }
                else
                {
                    Invoke("ChooseColorforAI", Random.Range(3f, 9f));
                }
            }
            else
            {
                if (c.Value == CardValue.Reverse)
                {
                    clockwiseTurn = !clockwiseTurn;
                    cardEffectAnimator.Play(clockwiseTurn ? "ClockWiseAnim" : "AntiClockWiseAnim");
                    Invoke("NextPlayerTurn", 1.5f);
                }
                else if (c.Value == CardValue.Skip)
                {
                    NextPlayerIndex();
                    CurrentPlayer.ShowMessage("Turn Skipped!");
                    Invoke("NextPlayerTurn", 1.5f);
                }
                else if (c.Value == CardValue.DrawTwo)
                {
                    NextPlayerIndex();
                    CurrentPlayer.ShowMessage("+2");
                    wildCardParticle.Emit(30);
                    StartCoroutine(DealCardsToPlayer(CurrentPlayer, 2, .5f));
                    Invoke("NextPlayerTurn", 1.5f);
                }
                else
                {
                    NextPlayerTurn();
                }
            }
        }
    }

    private IEnumerator RemovePlayerFromRoom(float time)
    {
        yield return new WaitForSeconds(time);

        if (gameOver) yield break;

        List<int> indexes = new List<int>();
        for (int i = 1; i < players.Count; i++)
        {
            indexes.Add(i);
        }
        indexes.Shuffle();

        int index = -1;
        foreach (var i in indexes)
        {
            if (!players[i].Timer)
            {
                index = i;
                break;
            }
        }

        var player = players[index];
        player.isInRoom = false;

        Toast.instance.ShowMessage(player.playerName + " left the room", 2.5f);

        yield return new WaitForSeconds(2f);

        player.gameObject.SetActive(false);
        foreach (var item in player.cardsPanel.cards)
        {
            item.gameObject.SetActive(false);
        }
    }

    void ChooseColorforAI()
    {
        CurrentPlayer.ChooseBestColor();
    }

    public void NextPlayerIndex()
    {
        int step = clockwiseTurn ? 1 : -1;
        do
        {
            currentPlayerIndex = Mod(currentPlayerIndex + step, players.Count);
        } while (!players[currentPlayerIndex].isInRoom);
    }

    private int Mod(int x, int m)
    {
        return (x % m + m) % m;
    }

    public void NextPlayerTurn()
    {
        NextPlayerIndex();
        CurrentPlayer.OnTurn();
    }

    public void OnColorSelect(int i)
    {
        if (!colorChoose.isOpen) return;
        colorChoose.HidePopup();

        SelectColor(i);
    }

    public void SelectColor(int i)
    {
        CurrentPlayer.Timer = false;
        CurrentPlayer.choosingColor = false;

        CurrentType = (CardType)i;
        cardEffectAnimator.Play("DrawFourAnim");
        if (CurrentValue == CardValue.Wild)
        {
            wildCardParticle.gameObject.SetActive(true);
            wildCardParticle.Emit(30);
            Invoke("NextPlayerTurn", 1.5f);
            CardGameManager.PlaySound(choose_color_clip);
        }
        else
        {
            NextPlayerIndex();
            CurrentPlayer.ShowMessage("+4");
            StartCoroutine(DealCardsToPlayer(CurrentPlayer, 4, .5f));
            Invoke("NextPlayerTurn", 2f);
            CardGameManager.PlaySound(choose_color_clip);
        }
    }

    public void EnableDeckClick()
    {
        arrowObject.SetActive(true);
    }

    public void OnDeckClick()
    {
        if (!setup) return;

        if (arrowObject.activeInHierarchy)
        {

            arrowObject.SetActive(false);
            CurrentPlayer.pickFromDeck = true;
            PickCardFromDeck(CurrentPlayer, true);
            if (CurrentPlayer.cardsPanel.AllowedCard.Count == 0 || (!CurrentPlayer.Timer && CurrentPlayer.isUserPlayer))
            {
                CurrentPlayer.OnTurnEnd();
                NextPlayerTurn();
            }
            else
            {
                CurrentPlayer.UpdateCardColor();
            }
        }
        else if (!CurrentPlayer.pickFromDeck && CurrentPlayer.isUserPlayer)
        {
            PickCardFromDeck(CurrentPlayer, true);
            CurrentPlayer.pickFromDeck = true;
            CurrentPlayer.UpdateCardColor();
        }
    }

    public void EnableUnoBtn()
    {
        unoBtn.GetComponent<Button>().interactable = true;
    }

    public void DisableUnoBtn()
    {
        unoBtn.GetComponent<Button>().interactable = false;
    }

    public void OnUnoClick()
    {
        DisableUnoBtn();
        CurrentPlayer.ShowMessage("Uno!", true);
        CurrentPlayer.unoClicked = true;
        CardGameManager.PlaySound(uno_btn_clip);
    }

    public void ApplyUnoCharge(Player2 p)
    {
        DisableUnoBtn();
        CurrentPlayer.ShowMessage("Uno Charges");
        StartCoroutine(DealCardsToPlayer(p, 2, .3f));
    }

    public void SetupGameOver()
    {
        gameOver = true;
        for (int i = players.Count - 1; i >= 0; i--)
        {
            if (!players[i].isInRoom)
            {
                players.RemoveAt(i);
            }
        }

        if (players.Count == 2)
        {
            playerObject[0].SetActive(true);
            playerObject[2].SetActive(true);
            playerObject[2].transform.GetChild(2).GetComponent<Text>().text = "2nd Place";
            playerObject.RemoveAt(3);
            playerObject.RemoveAt(1);

        }
        else if (players.Count == 3)
        {
            playerObject.RemoveAt(2);
            for (int i = 0; i < 3; i++)
            {
                playerObject[i].SetActive(true);
            }
            playerObject[2].transform.GetChild(2).GetComponent<Text>().text = "3rd Place";

        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                playerObject[i].SetActive(true);
            }
        }

        players.Sort((x, y) => x.GetTotalPoints().CompareTo(y.GetTotalPoints()));
        var winner = players[0];

        starParticle.gameObject.SetActive(winner.isUserPlayer);
        playerObject[0].GetComponentsInChildren<Image>()[1].sprite = winner.avatarImage.sprite;

        for (int i = 1; i < playerObject.Count; i++)
        {
            var playerNameText = playerObject[i].GetComponentInChildren<Text>();
            playerNameText.text = players[i].playerName;
            playerNameText.GetComponent<EllipsisText>().UpdateText();
            playerObject[i].GetComponentsInChildren<Image>()[1].sprite = players[i].avatarImage.sprite;
        }

        CardGameManager.PlaySound(winner.isUserPlayer ? music_win_clip : music_loss_clip);
        gameOverPopup.SetActive(true);
        screenCanvas.SetActive(false);

        for (int i = 1; i < players.Count; i++)
        {
            if (players[i].isUserPlayer)
            {
                loseTimerAnimation.SetActive(true);
                loseTimerAnimation.transform.position = playerObject[i].transform.position;
                break;
            }
        }

        gameOverPopup.GetComponentInChildren<Text>().text = winner.isUserPlayer ? "You win Game." : "You Lost Game ...   Try Again.";
        fastForwardTime = 0;
    }

    IEnumerator CheckNetwork()
    {
        while (true)
        {
            WWW www = new WWW("https://www.google.com");
            yield return www;
            if (string.IsNullOrEmpty(www.error))
            {
                if (noNetwork.isOpen)
                {
                    noNetwork.HidePopup();

                    Time.timeScale = 1;
                    OnApplicationPause(false);
                }
            }
            else
            {
                if (Time.timeScale == 1)
                {
                    noNetwork.ShowPopup();

                    Time.timeScale = 0;
                    pauseTime = System.DateTime.Now;
                }
            }

            yield return new WaitForSecondsRealtime(1f);
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            pauseTime = System.DateTime.Now;
        }
        else
        {
            if (CardGameManager.currentGameMode == GameMode.MultiPlayer && multiplayerLoaded && !gameOver)
            {
                fastForwardTime += Mathf.Clamp((int)(System.DateTime.Now - pauseTime).TotalSeconds, 0, 3600);
                if (Time.timeScale == 1f)
                {
                    StartCoroutine(DoFastForward());
                }
            }
        }
    }

    IEnumerator DoFastForward()
    {
        Time.timeScale = 10f;
        rayCastBlocker.SetActive(true);
        while (fastForwardTime > 0)
        {
            yield return new WaitForSeconds(1f);
            fastForwardTime--;
        }
        Time.timeScale = 1f;
        rayCastBlocker.SetActive(false);

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
            p2.isUserPlayer = (seat == 0);
        }
    }


    public void StartMultiplayerGame()
    {
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
        SerializableCard[] allCards = new SerializableCard[playerCount * cardsPerPlayer];
        for (int playerIdx = 0; playerIdx < playerCount; playerIdx++)
            for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
            {
                allCards[playerIdx * cardsPerPlayer + cardSlot] = new SerializableCard(cards[0].Type, cards[0].Value);
                cards.RemoveAt(0);
            }
        DealCardsClientRpc(allCards, cardsPerPlayer, playerCount);

        cards.RemoveRange(0, cardsPerPlayer * players.Count);
        SerializableCard[] deckCards = new SerializableCard[cards.Count];
        for (int i = 0; i < cards.Count; i++)
            deckCards[i] = new SerializableCard(cards[i].Type, cards[i].Value);

        UpdateDeckVisualClientRpc(deckCards);

        int a = 0;
        while (cards[a].Type == CardType.Other) a++;
        var waste = new SerializableCard(cards[a].Type, cards[a].Value);
        SetWastePileClientRpc(waste);
    }

    [ClientRpc]
    void GiveAllHandsClientRpc(SerializableCard[] allCards, int cardsPerPlayer, int playerCount)
    {
        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        int myGlobalSeat = 0;
        for (int i = 0; i < playerList.Count; i++)
            if (playerList[i].clientId == myClientId)
                myGlobalSeat = i;

        for (int localSeat = 0; localSeat < playerCount; localSeat++)
        {
            int globalSeat = (myGlobalSeat + localSeat) % playerCount;

            var player = players[localSeat];
            ClearHandPanel(player.cardsPanel.transform);
            player.cardsPanel.cards.Clear();

            for (int cardSlot = 0; cardSlot < cardsPerPlayer; cardSlot++)
            {
                int idx = globalSeat * cardsPerPlayer + cardSlot;
                var sc = allCards[idx];

                var card = Instantiate(_cardPrefab, cardDeckTransform.position, Quaternion.identity, player.cardsPanel.transform);
                card.Type = sc.Type;
                card.Value = sc.Value;
                card.IsOpen = (localSeat == 0);
                card.CalcPoint();
                card.name = $"{sc.Type}_{sc.Value}";
                player.AddCard(card);

                card.transform.SetSiblingIndex(cardSlot);

                card.localSeat = localSeat;
                card.cardIndex = cardSlot;
                card.IsClickable = true;
                card.onClick = OnAnyCardClicked;
            }

            player.cardsPanel.UpdatePos();
        }
    }


    public void OnAnyCardClicked(Card card)
    {
        // FOR DEBUG
        card.IsOpen = true;

    }

    [ClientRpc]
    void SetWastePileClientRpc(SerializableCard wasteCard)
    {
        var card = Instantiate(_cardPrefab, cardWastePile.transform);
        card.transform.localPosition = Vector3.zero;
        card.Type = wasteCard.Type;
        card.Value = wasteCard.Value;
        card.IsOpen = true;
        card.CalcPoint();
        card.name = wasteCard.Type.ToString() + "_" + wasteCard.Value.ToString();


        card.transform.position = cardWastePile.transform.position;
        card.SetTargetPosAndRot(new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 1), card.transform.localRotation.eulerAngles.z + Random.Range(-15f, 15f));
    }

    void ClearHandPanel(Transform panel)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
            GameObject.Destroy(panel.GetChild(i).gameObject);
    }

    [ClientRpc]
    void UpdateDeckVisualClientRpc(SerializableCard[] deckCards)
    {
        foreach (Transform t in cardDeckTransform)
            Destroy(t.gameObject);

        for (int i = 0; i < deckCards.Length; i++)
        {
            var sc = deckCards[i];
            var card = Instantiate(_cardPrefab, cardDeckTransform);
            card.IsOpen = false;
            card.Type = sc.Type;
            card.Value = sc.Value;
            card.CalcPoint();
            card.name = $"{sc.Type}_{sc.Value}";
            card.GetComponent<Button>()?.gameObject.SetActive(false);

            card.transform.localPosition += new Vector3(
                Random.Range(-2f, 2f),
                0,
                -i * 0.75f
            );
        }
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