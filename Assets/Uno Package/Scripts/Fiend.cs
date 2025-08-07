using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class Fiend : NetworkBehaviour
{
    public static Fiend Instance;

    [Header("Assign in Inspector")]
    public GameObject fiendPopup;
    public Transform avatarPanel;
    public GameObject playerRowPrefab;
    [Header("Audio")]
    private AudioSource _audioSource;
    public AudioClip swapCardClip;
    [SerializeField] Toggle jumbleToggle;

    void Awake()
    {
        Instance = this;
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
    }

    public void ShowFiendPopup()
    {
        fiendPopup.SetActive(true);
        if (jumbleToggle != null)
            jumbleToggle.isOn = false;

        foreach (Transform child in avatarPanel)
            Destroy(child.gameObject);

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;

        for (int globalSeat = 0; globalSeat < playerList.Count; globalSeat++)
        {
            var pd = playerList[globalSeat];
            if (pd.clientId == myClientId)
                continue; // skip self

            int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(globalSeat);
            var p = GamePlayManager.instance.players[localSeat];

            int targetGlobalSeat = globalSeat;

            GameObject avatarEntry = Instantiate(playerRowPrefab, avatarPanel);

            var avatarImg = avatarEntry.transform.Find("AvatarImage")?.GetComponent<Image>();
            if (avatarImg != null && p.avatarImage != null)
                avatarImg.sprite = p.avatarImage.sprite;

            var nameText = avatarEntry.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            if (nameText != null)
                nameText.text = p.playerName;

            Button btn = avatarEntry.GetComponent<Button>() ?? avatarEntry.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnOpponentSelected(targetGlobalSeat));
        }
    }

    private void OnOpponentSelected(int targetSeat)
    {
        fiendPopup.SetActive(false);

        if (jumbleToggle != null && jumbleToggle.isOn)
        {
            // FAKE jumble for all clients (no real data change)
            RequestVisualJumbleHandServerRpc(targetSeat);
        }
        else
        {
            // REAL jumble, notify server to perform and sync
            RequestJumbleHandServerRpc(targetSeat);
        }

    }


    [ServerRpc(RequireOwnership = false)]
    private void RequestJumbleHandServerRpc(int seatIndex, ServerRpcParams rpcParams = default)
    {
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();

        // Build a random target hand order (shuffle result)
        var hand = GamePlayManager.instance.players[seatIndex].cardsPanel.cards;
        int cardCount = hand.Count;
        List<int> indices = new List<int>();
        for (int i = 0; i < cardCount; i++) indices.Add(i);
        for (int i = cardCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = indices[i]; indices[i] = indices[j]; indices[j] = temp;
        }

        // Send to all clients for animated jumble
        JumbleHandClientRpc(seatIndex, indices.ToArray());
    }

    [ClientRpc]
    private void JumbleHandClientRpc(int seatIndex, int[] newOrder)
    {
        GamePlayManager.instance.FreezeTimerUI();

        int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(seatIndex);
        var player = GamePlayManager.instance.players[localSeat];
        var hand = player.cardsPanel.cards;

        player.StartCoroutine(JumbleHandAnimation(player, hand, newOrder, 3.0f, true, () =>
        {
            if (IsHost && NetworkManager.Singleton.IsServer)
                GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));
        }));
    }

    public IEnumerator JumbleHandAnimation(Player2 player, List<Card> hand, int[] finalOrder, float duration, bool updateCardData, System.Action onComplete)
    {
        float timer = 0f;
        int cardCount = hand.Count;

        Vector3[] slotPositions = new Vector3[cardCount];
        for (int i = 0; i < cardCount; i++)
            slotPositions[i] = hand[i].transform.localPosition;

        List<Card> workingHand = updateCardData ? hand : new List<Card>(hand);

        while (timer < duration)
        {
            int a = Random.Range(0, cardCount);
            int b;
            do { b = Random.Range(0, cardCount); } while (b == a);

            Vector3 posA = workingHand[a].transform.localPosition;
            Vector3 posB = workingHand[b].transform.localPosition;

            float elapsed = 0f;
            float swapAnim = 0.18f;
            while (elapsed < swapAnim)
            {
                float t = elapsed / swapAnim;
                workingHand[a].transform.localPosition = Vector3.Lerp(posA, posB, t);
                workingHand[b].transform.localPosition = Vector3.Lerp(posB, posA, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            workingHand[a].transform.localPosition = posB;
            workingHand[b].transform.localPosition = posA;

            var temp = workingHand[a];
            workingHand[a] = workingHand[b];
            workingHand[b] = temp;

            if (swapCardClip != null && _audioSource != null)
                _audioSource.PlayOneShot(swapCardClip);
            timer += swapAnim;
        }

        yield return new WaitForSeconds(0.2f);

        // Snap to final order and update positions
        List<Card> oldHand = new List<Card>(workingHand);
        List<Card> newHand = new List<Card>();
        for (int i = 0; i < finalOrder.Length; i++)
        {
            Card c = oldHand[finalOrder[i]];
            newHand.Add(c);
            c.transform.localPosition = slotPositions[i];
        }
        if (updateCardData)
        {
            player.cardsPanel.cards.Clear();
            player.cardsPanel.cards.AddRange(newHand);
            for (int i = 0; i < newHand.Count; i++)
                newHand[i].cardIndex = i;
        }
        player.cardsPanel.UpdatePos();

        onComplete?.Invoke();
    }


    public void HideFiendPopup()
    {
        fiendPopup.SetActive(false);
    }

    public void StartBotFiendJumble(ulong botClientId)
    {
        StartCoroutine(BotFiendJumbleRoutine(botClientId));
    }

    private IEnumerator BotFiendJumbleRoutine(ulong botClientId)
    {
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var gpm = GamePlayManager.instance;
        var playerList = MultiplayerManager.Instance.playerDataNetworkList;

        // Find all opponents (not self/bot)
        List<int> targetSeats = new List<int>();
        for (int globalSeat = 0; globalSeat < playerList.Count; globalSeat++)
        {
            if (playerList[globalSeat].clientId != botClientId)
                targetSeats.Add(globalSeat);
        }

        if (targetSeats.Count == 0) yield break;

        int randomTarget = targetSeats[Random.Range(0, targetSeats.Count)];
        RequestJumbleHandServerRpc(randomTarget, new ServerRpcParams
        {
            Receive = new ServerRpcReceiveParams { SenderClientId = botClientId }
        });
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestVisualJumbleHandServerRpc(int seatIndex, ServerRpcParams rpcParams = default)
    {
        if (GamePlayManager.instance.turnTimeoutCoroutine != null)
        {
            GamePlayManager.instance.StopCoroutine(GamePlayManager.instance.turnTimeoutCoroutine);
            GamePlayManager.instance.turnTimeoutCoroutine = null;
        }
        GamePlayManager.instance.FreezeTimerUI();
        // Build a random visual shuffle order
        var hand = GamePlayManager.instance.players[seatIndex].cardsPanel.cards;
        int cardCount = hand.Count;
        List<int> indices = new List<int>();
        for (int i = 0; i < cardCount; i++) indices.Add(i);
        for (int i = cardCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = indices[i]; indices[i] = indices[j]; indices[j] = temp;
        }
        // Send to all clients for animated jumble (visual only)
        VisualJumbleHandClientRpc(seatIndex, indices.ToArray());
    }

    [ClientRpc]
    private void VisualJumbleHandClientRpc(int seatIndex, int[] newOrder)
    {
        GamePlayManager.instance.FreezeTimerUI();

        int localSeat = GamePlayManager.instance.GetLocalIndexFromGlobal(seatIndex);
        var player = GamePlayManager.instance.players[localSeat];
        var hand = player.cardsPanel.cards;

        player.StartCoroutine(JumbleHandAnimation(player, hand, newOrder, 3.0f, false, () =>
        {
            player.cardsPanel.UpdatePos();
            if (NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsServer)
            {
                GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));
            }
        }));
    }




}
