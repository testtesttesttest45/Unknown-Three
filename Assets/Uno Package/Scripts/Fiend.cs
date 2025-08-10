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

    private GamePlayManager gpm => GamePlayManager.instance;

    void Awake()
    {
        Instance = this;
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
    }

    // normal flow of Fiend
    public void ShowFiendPopup()
    {
        if (fiendPopup == null || avatarPanel == null || playerRowPrefab == null) return;

        fiendPopup.SetActive(true);
        if (jumbleToggle != null) jumbleToggle.isOn = false;

        foreach (Transform child in avatarPanel) Destroy(child.gameObject);
        if (gpm == null || gpm.seatOrderGlobal == null || gpm.seatOrderGlobal.Count == 0) return;

        ulong myClientId = NetworkManager.Singleton.LocalClientId;

        for (int globalSeat = 0; globalSeat < gpm.seatOrderGlobal.Count; globalSeat++)
        {
            ulong cid = gpm.seatOrderGlobal[globalSeat];

            // Skip myself only
            if (cid == myClientId) continue;

            GameObject row = Instantiate(playerRowPrefab, avatarPanel);

            int localSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
            Player2 p = (localSeat >= 0 && localSeat < gpm.players.Count) ? gpm.players[localSeat] : null;

            var nameText = row.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            if (nameText != null)
            {
                string label = p != null && !string.IsNullOrEmpty(p.playerName)
                    ? p.playerName
                    : TryGetNameFromPlayerData(cid) ?? $"Player {globalSeat + 1}";
                nameText.text = label;

                // append "disconnected" to the name
                if (!IsClientIdPresentInPlayerData(cid))
                    nameText.text += " (disconnected)";
            }

            var avatarImg = row.transform.Find("AvatarImage")?.GetComponent<Image>();
            if (avatarImg != null)
            {
                Sprite s = gpm.GetAvatarSpriteForGlobalSeatSafe(globalSeat);
                if (s != null) avatarImg.sprite = s;
            }

            int capturedGlobalSeat = globalSeat;
            var btn = row.GetComponent<Button>() ?? row.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnOpponentSelected(capturedGlobalSeat));
        }

    }

    private bool IsClientIdPresentInPlayerData(ulong clientId)
    {
        var list = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < list.Count; i++)
            if (list[i].clientId == clientId) return true;
        return false;
    }

    private string TryGetNameFromPlayerData(ulong clientId)
    {
        var list = MultiplayerManager.Instance.playerDataNetworkList;
        for (int i = 0; i < list.Count; i++)
            if (list[i].clientId == clientId)
                return list[i].playerName.ToString();
        return null;
    }


    public void HideFiendPopup()
    {
        if (fiendPopup != null) fiendPopup.SetActive(false);
    }

    private void OnOpponentSelected(int targetGlobalSeat)
    {
        HideFiendPopup();

        if (jumbleToggle != null && jumbleToggle.isOn)
        {
            // Visual-only jumble (no data change)
            RequestVisualJumbleHandServerRpc(targetGlobalSeat);
        }
        else
        {
            // Real jumble (reorders hand)
            RequestJumbleHandServerRpc(targetGlobalSeat);
        }
    }

    // authoritative true jumble
    [ServerRpc(RequireOwnership = false)]
    private void RequestJumbleHandServerRpc(int targetGlobalSeat, ServerRpcParams rpcParams = default)
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;

        // Pause/Freeze timer
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        int localSeat = gpm.GetLocalIndexFromGlobal(targetGlobalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var hand = gpm.players[localSeat].cardsPanel?.cards;
        if (hand == null || hand.Count <= 1) 
        {
            FinishPowerAndAdvanceTurn();
            return;
        }

        int cardCount = hand.Count;
        List<int> indices = BuildRandomPermutation(cardCount);

        // Broadcast to everybody to animate & apply final order
        JumbleHandClientRpc(targetGlobalSeat, indices.ToArray());
    }

    [ClientRpc]
    private void JumbleHandClientRpc(int targetGlobalSeat, int[] newOrder)
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;

        gpm.FreezeTimerUI();

        int localSeat = gpm.GetLocalIndexFromGlobal(targetGlobalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var player = gpm.players[localSeat];
        var hand = player.cardsPanel?.cards;
        if (hand == null || hand.Count == 0 || newOrder == null || newOrder.Length != hand.Count)
        {
            // Fail-safe finish
            if (IsHost) FinishPowerAndAdvanceTurn();
            return;
        }

        // Animate and then commit new order (updateCardData=true)
        player.StartCoroutine(JumbleHandAnimation(
            player, hand, newOrder, 3.0f, true,
            () =>
            {
                gpm.EndCurrentPowerAvatarFromServer();
                if (IsHost) gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
            }
        ));
    }

    // fake jumble, visual only
    [ServerRpc(RequireOwnership = false)]
    private void RequestVisualJumbleHandServerRpc(int targetGlobalSeat, ServerRpcParams rpcParams = default)
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;

        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        int localSeat = gpm.GetLocalIndexFromGlobal(targetGlobalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) { FinishPowerAndAdvanceTurn(); return; }

        var hand = gpm.players[localSeat].cardsPanel?.cards;
        if (hand == null || hand.Count <= 1) { FinishPowerAndAdvanceTurn(); return; }

        int cardCount = hand.Count;
        List<int> indices = BuildRandomPermutation(cardCount);

        VisualJumbleHandClientRpc(targetGlobalSeat, indices.ToArray());
    }

    [ClientRpc]
    private void VisualJumbleHandClientRpc(int targetGlobalSeat, int[] newOrder)
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;

        gpm.FreezeTimerUI();

        int localSeat = gpm.GetLocalIndexFromGlobal(targetGlobalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var player = gpm.players[localSeat];
        var hand = player.cardsPanel?.cards;
        if (hand == null || hand.Count == 0 || newOrder == null || newOrder.Length != hand.Count)
        {
            if (IsHost) FinishPowerAndAdvanceTurn();
            return;
        }

        // Animate only (updateCardData=false), then snap back to original layout
        player.StartCoroutine(JumbleHandAnimation(
            player, hand, newOrder, 3.0f, false,
            () =>
            {
                player.cardsPanel.UpdatePos();
                if (IsHost)
                {
                    gpm.EndCurrentPowerAvatarFromServer();
                    gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
                }
            }
        ));
    }

    // helpers
    public IEnumerator JumbleHandAnimation(
        Player2 player,
        List<Card> hand,
        int[] finalOrder,
        float durationSeconds,
        bool updateCardData,
        System.Action onComplete)
    {
        if (player == null || player.cardsPanel == null || hand == null || hand.Count == 0) yield break;

        float timer = 0f;
        int cardCount = hand.Count;

        Vector3[] slotPositions = new Vector3[cardCount];
        for (int i = 0; i < cardCount; i++)
            slotPositions[i] = hand[i].transform.localPosition;

        List<Card> workingHand = updateCardData ? hand : new List<Card>(hand);

        const float swapAnim = 0.18f;
        while (timer < durationSeconds && cardCount > 1)
        {
            int a = Random.Range(0, cardCount);
            int b; do { b = Random.Range(0, cardCount); } while (b == a);

            var ca = workingHand[a];
            var cb = workingHand[b];
            if (ca == null || cb == null) { timer += swapAnim; continue; }

            Vector3 posA = ca.transform.localPosition;
            Vector3 posB = cb.transform.localPosition;

            float elapsed = 0f;
            while (elapsed < swapAnim)
            {
                float t = elapsed / swapAnim;
                if (ca != null) ca.transform.localPosition = Vector3.Lerp(posA, posB, t);
                if (cb != null) cb.transform.localPosition = Vector3.Lerp(posB, posA, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (ca != null) ca.transform.localPosition = posB;
            if (cb != null) cb.transform.localPosition = posA;

            var tmp = workingHand[a];
            workingHand[a] = workingHand[b];
            workingHand[b] = tmp;

            if (swapCardClip != null && _audioSource != null)
                _audioSource.PlayOneShot(swapCardClip);

            timer += swapAnim;
        }

        yield return new WaitForSeconds(0.2f);

        if (finalOrder == null || finalOrder.Length != cardCount)
        {
            for (int i = 0; i < cardCount; i++)
                if (workingHand[i] != null)
                    workingHand[i].transform.localPosition = slotPositions[i];

            onComplete?.Invoke();
            yield break;
        }

        var oldWorking = new List<Card>(workingHand);
        var newHand = new List<Card>(cardCount);
        for (int i = 0; i < finalOrder.Length; i++)
        {
            int src = Mathf.Clamp(finalOrder[i], 0, oldWorking.Count - 1);
            Card c = oldWorking[src];
            newHand.Add(c);
            if (c != null) c.transform.localPosition = slotPositions[i];
        }

        if (updateCardData)
        {
            player.cardsPanel.cards.Clear();
            player.cardsPanel.cards.AddRange(newHand);
            for (int i = 0; i < player.cardsPanel.cards.Count; i++)
                if (player.cardsPanel.cards[i] != null)
                    player.cardsPanel.cards[i].cardIndex = i;
        }

        player.cardsPanel.UpdatePos();
        onComplete?.Invoke();
    }

    // bot handling
    public void StartBotFiendJumble(ulong botClientId)
    {
        StartCoroutine(BotFiendJumbleRoutine(botClientId));
    }

    private IEnumerator BotFiendJumbleRoutine(ulong botClientId)
    {
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        if (gpm == null) yield break;

        var playerList = MultiplayerManager.Instance.playerDataNetworkList;
        var targets = new List<int>();

        for (int globalSeat = 0; globalSeat < playerList.Count; globalSeat++)
        {
            if (playerList[globalSeat].clientId != botClientId)
                targets.Add(globalSeat);
        }
        if (targets.Count == 0) yield break;

        int targetGlobalSeat = targets[Random.Range(0, targets.Count)];

        RequestJumbleHandServerRpc(
            targetGlobalSeat,
            new ServerRpcParams { Receive = new ServerRpcReceiveParams { SenderClientId = botClientId } }
        );
    }

    // helpers
    private static List<int> BuildRandomPermutation(int n)
    {
        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }
        return idx;
    }

    private void FinishPowerAndAdvanceTurn()
    {
        gpm.EndCurrentPowerAvatarFromServer();
        if (IsHost) gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
    }
}
