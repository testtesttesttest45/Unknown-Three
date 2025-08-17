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
            if (cid == myClientId) continue; // skip myself

            GameObject row = Instantiate(playerRowPrefab, avatarPanel);

            int localSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
            Player2 p = (localSeat >= 0 && localSeat < gpm.players.Count) ? gpm.players[localSeat] : null;

            var nameText = row.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            if (nameText != null)
            {
                string label = p != null && !string.IsNullOrEmpty(p.playerName)
                    ? p.playerName
                    : TryGetNameFromPlayerData(cid) ?? $"Player {globalSeat + 1}";
                if (!IsClientIdPresentInPlayerData(cid)) label += " (disconnected)";
                nameText.text = label;
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

    public void HideFiendPopup()
    {
        if (fiendPopup != null) fiendPopup.SetActive(false);
    }

    private void OnOpponentSelected(int targetGlobalSeat)
    {
        HideFiendPopup();

        if (jumbleToggle != null && jumbleToggle.isOn)
            RequestVisualJumbleHandServerRpc(targetGlobalSeat);
        else
            RequestJumbleHandServerRpc(targetGlobalSeat);
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

    // Server: authoritative jumble
    [ServerRpc(RequireOwnership = false)]
    public void RequestJumbleHandServerRpc(int targetGlobalSeat, ServerRpcParams rpcParams = default)
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
        if (localSeat < 0 || localSeat >= gpm.players.Count) { FinishPowerAndAdvanceTurn(); return; }

        var hand = gpm.players[localSeat].cardsPanel?.cards;
        if (hand == null || hand.Count <= 1) { FinishPowerAndAdvanceTurn(); return; }

        int cardCount = hand.Count;
        List<int> indices = BuildRandomPermutation(cardCount);

        // Broadcast authoritative order to everyone
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
            if (IsHost) FinishPowerAndAdvanceTurn();
            return;
        }

        // Animate on a visual copy; commit using snapshot + newOrder
        player.StartCoroutine(JumbleHandAnimation(
            player, hand, newOrder, 3.0f, true,
            () =>
            {
                gpm.EndCurrentPowerAvatarFromServer();
                if (IsHost) gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f, gpm.CurrentTurnSerial));
            }
        ));
    }

    // Server: visual-only jumble
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

        player.StartCoroutine(JumbleHandAnimation(
            player, hand, newOrder, 3.0f, false,
            () =>
            {
                // snap back to original layout
                player.cardsPanel.UpdatePos();
                if (IsHost)
                {
                    gpm.EndCurrentPowerAvatarFromServer();
                    gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f, gpm.CurrentTurnSerial));
                }
            }
        ));
    }

    // synced Animation
    public IEnumerator JumbleHandAnimation(
        Player2 player,
        List<Card> hand,
        int[] finalOrder,
        float durationSeconds,
        bool updateCardData,
        System.Action onComplete)
    {
        if (player == null || player.cardsPanel == null || hand == null || hand.Count == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        var handSnapshot = new List<Card>(hand);
        int cardCount = handSnapshot.Count;

        Vector3[] slotPositions = new Vector3[cardCount];
        for (int i = 0; i < cardCount; i++)
            slotPositions[i] = handSnapshot[i] != null
                ? handSnapshot[i].transform.localPosition
                : Vector3.zero;

        var visualOrder = new List<Card>(handSnapshot);

        float timer = 0f;
        const float swapAnim = 0.18f;
        while (timer < durationSeconds && cardCount > 1)
        {
            if (player.cardsPanel == null || player.cardsPanel.cards == null) break;

            int a = Random.Range(0, cardCount);
            int b; do { b = Random.Range(0, cardCount); } while (b == a);

            var ca = visualOrder[a];
            var cb = visualOrder[b];
            if (ca == null || cb == null) { timer += swapAnim; yield return null; continue; }

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

            var tmp = visualOrder[a];
            visualOrder[a] = visualOrder[b];
            visualOrder[b] = tmp;

            TryPlaySwapSfx();
            timer += swapAnim;
        }

        yield return new WaitForSeconds(0.2f);

        if (finalOrder == null || finalOrder.Length != cardCount)
        {
            for (int i = 0; i < cardCount; i++)
            {
                var c = handSnapshot[i];
                if (c != null) c.transform.localPosition = slotPositions[i];
            }
            player.cardsPanel.UpdatePos();
            onComplete?.Invoke();
            yield break;
        }

        var committed = new List<Card>(cardCount);
        for (int i = 0; i < finalOrder.Length; i++)
        {
            int src = Mathf.Clamp(finalOrder[i], 0, handSnapshot.Count - 1);
            committed.Add(handSnapshot[src]);
        }

        if (updateCardData)
        {
            player.cardsPanel.cards.Clear();
            player.cardsPanel.cards.AddRange(committed);

            for (int i = 0; i < player.cardsPanel.cards.Count; i++)
            {
                var c = player.cardsPanel.cards[i];
                if (c != null)
                {
                    c.cardIndex = i;
                    c.transform.localPosition = slotPositions[i];
                }
            }
        }
        else
        {
            for (int i = 0; i < cardCount; i++)
            {
                var c = handSnapshot[i];
                if (c != null) c.transform.localPosition = slotPositions[i];
            }
        }

        player.cardsPanel.UpdatePos();
        onComplete?.Invoke();
    }

    private void TryPlaySwapSfx()
    {
        if (swapCardClip == null) return;
        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            if (_audioSource == null) return;
        }
        _audioSource.PlayOneShot(swapCardClip);
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
        if (IsHost) gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f, gpm.CurrentTurnSerial));
    }
}
