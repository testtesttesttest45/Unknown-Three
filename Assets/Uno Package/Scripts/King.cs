using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class King : NetworkBehaviour
{
    public static King Instance;

    public bool isKingPhase = false;
    private Card selectedCard = null;
    private int selectedLocalSeat = -1;
    private int selectedCardIndex = -1;

    private Vector3 lastKilledCardPos;
    private float lastKilledCardZRot;
    private List<bool> _overlayPanelActiveCache;
    private readonly List<ParticleSystem> _pausedParticlesDuringSpotlight = new List<ParticleSystem>();

    private static readonly float[] kSpotlightAngles = { 130f, 65f, -40f, -110f };

    private void RotateSpotlightToSeat(int localSeat)
    {
        if (!gpm || !gpm.spotlight) return;

        float z = 0f;
        if (localSeat >= 0 && localSeat < kSpotlightAngles.Length)
            z = kSpotlightAngles[localSeat];

        var pivot = gpm.spotlight.transform;

        // snap rotation
        var e = pivot.localEulerAngles;
        e.z = z + gpm.spotlightZArtOffset;
        pivot.localEulerAngles = e;
    }

    private GamePlayManager gpm => GamePlayManager.instance;

    void Awake() => Instance = this;

    public void StartKingPhase()
    {
        if (gpm == null || gpm.cards == null || gpm.cards.Count == 0) return;

        isKingPhase = true;
        selectedCard = null;
        selectedLocalSeat = -1;
        selectedCardIndex = -1;

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var player = gpm.players[seat];
            for (int i = 0; i < player.cardsPanel.cards.Count; i++)
            {
                var handCard = player.cardsPanel.cards[i];
                if (handCard == null) continue;

                handCard.ShowGlow(true);
                handCard.IsClickable = true;

                int capturedSeat = seat;
                int capturedCardIndex = i;
                handCard.onClick = (clickedCard) =>
                {
                    OnHandCardSelected(clickedCard, capturedSeat, capturedCardIndex);
                };
            }
        }

        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    private void OnHandCardSelected(Card card, int localSeat, int cardIndex)
    {
        if (!isKingPhase || card == null) return;
        isKingPhase = false;

        selectedCard = card;
        selectedLocalSeat = localSeat;
        selectedCardIndex = cardIndex;

        EndKingPhase();

        // Freeze timer
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        int globalSeat = gpm.GetGlobalIndexFromLocal(localSeat);
        KingKillCardServerRpc(globalSeat, cardIndex, card.transform.position, card.transform.rotation.eulerAngles.z, -1);
    }

    public void EndKingPhase()
    {
        isKingPhase = false;
        foreach (var p in gpm.players)
            foreach (var c in p.cardsPanel.cards)
            {
                if (c == null) continue;
                c.ShowGlow(false);
                c.IsClickable = false;
                c.onClick = null;
            }
    }

    [ServerRpc(RequireOwnership = false)]
    private void KingKillCardServerRpc(
        int globalSeat,
        int cardIndex,
        Vector3 pos,
        float zRot,
        int actingBotLocalSeat,
        ServerRpcParams rpcParams = default)
    {
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        int killerGlobalSeat;
        ulong killerClientId;

        if (actingBotLocalSeat >= 0 && actingBotLocalSeat < gpm.players.Count)
        {
            killerGlobalSeat = gpm.GetGlobalIndexFromLocal(actingBotLocalSeat);
            killerClientId = gpm.GetClientIdFromGlobalSeat(killerGlobalSeat);
        }
        else
        {
            killerClientId = rpcParams.Receive.SenderClientId;
            killerGlobalSeat = gpm.GetPlayerIndexFromClientId(killerClientId);

            if (killerGlobalSeat < 0)
            {
                var plist = MultiplayerManager.Instance.playerDataNetworkList;
                for (int i = 0; i < plist.Count; i++)
                {
                    if (plist[i].clientId == killerClientId)
                    {
                        killerGlobalSeat = gpm.GetPlayerIndexFromClientId(plist[i].clientId);
                        break;
                    }
                }
            }
        }


        bool isOpponent = (killerGlobalSeat != globalSeat);

        int targetLocalSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
        if (targetLocalSeat < 0 || targetLocalSeat >= gpm.players.Count) return;

        var targetPlayer = gpm.players[targetLocalSeat];
        if (cardIndex < 0 || cardIndex >= targetPlayer.cardsPanel.cards.Count) return;

        var killedCard = targetPlayer.cardsPanel.cards[cardIndex];
        bool killedWasFiend = (killedCard != null && killedCard.Value == CardValue.Fiend);
        bool killedWasGoldenJack = (killedCard != null && killedCard.Value == CardValue.GoldenJack);
        bool killedWasZero = (killedCard != null && killedCard.Value == CardValue.Zero);

        KingKillCardClientRpc(globalSeat, cardIndex, killerGlobalSeat);

        KingRefillHandAfterDelay(
            globalSeat, cardIndex, pos, zRot,
            killerGlobalSeat, isOpponent, killedWasFiend, killedWasGoldenJack, actingBotLocalSeat, killerClientId,
            killedWasZero
        );
    }

    private async void KingRefillHandAfterDelay(
        int globalSeat, int cardIndex, Vector3 toPos, float toZRot,
        int killerGlobalSeat, bool isOpponent, bool killedWasFiend, bool killedWasGoldenJack,
        int actingBotLocalSeat = -1, ulong killerClientId = 0, bool killedWasZero = false)
    {
        gpm.isKingRefillPhase = true;
        await System.Threading.Tasks.Task.Delay(1000);

        // Draw top card if deck not empty
        if (gpm.cards == null || gpm.cards.Count == 0)
        {
            gpm.isKingRefillPhase = false;
            gpm.EndCurrentPowerAvatarFromServer();
            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
            return;
        }

        SerializableCard topCard = gpm.cards[gpm.cards.Count - 1];
        gpm.cards.RemoveAt(gpm.cards.Count - 1);

        KingRefillHandClientRpc(globalSeat, cardIndex, topCard, gpm.cards.ToArray());

        gpm.isKingRefillPhase = false;

        if (killedWasZero && isOpponent)
        {
            var targets = gpm.GetAllHumanClientIds();
            foreach (var cid in targets)
            {
                int killerLocalForThatClient = LocalSeatForClient(cid, killerGlobalSeat);
                ShowZeroKillCelebrationLocalSeatClientRpc(
                    killerLocalForThatClient,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { cid } } }
                );
            }

            await System.Threading.Tasks.Task.Delay(4000);
        }

        // Revenge effects
        if (killedWasFiend && isOpponent)
        {
            float jumbleSeconds = 3.5f;

            gpm.BeginTemporaryAvatarFromServer(globalSeat, CardValue.Fiend, jumbleSeconds);
            ShowFiendRevengeClientRpc(globalSeat);
            await System.Threading.Tasks.Task.Delay(400);

            int killerLocalSeat = gpm.GetLocalIndexFromGlobal(killerGlobalSeat);
            if (killerGlobalSeat >= 0 && killerGlobalSeat < MultiplayerManager.Instance.playerDataNetworkList.Count)
                killerClientId = MultiplayerManager.Instance.playerDataNetworkList[killerGlobalSeat].clientId;

            if (killerGlobalSeat >= 0 && Fiend.Instance != null)
            {
                Fiend.Instance.RequestJumbleHandServerRpc(killerGlobalSeat);
            }
        }
        else if (killedWasGoldenJack && isOpponent)
        {
            float revealSeconds = 3.0f;

            gpm.BeginTemporaryAvatarFromServer(globalSeat, CardValue.GoldenJack, revealSeconds);
            ShowGoldenJackRevengeClientRpc(killerGlobalSeat, globalSeat, killerClientId);

            await System.Threading.Tasks.Task.Delay((int)(revealSeconds * 1000));

            if (NetworkManager.Singleton.IsHost)
                gpm.EndAvatarForSeatFromServer(killerGlobalSeat);

            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
        }
        else
        {
            gpm.EndCurrentPowerAvatarFromServer();
            if (NetworkManager.Singleton.IsHost)
                gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
        }
    }

    [ClientRpc]
    private void ShowZeroKillCelebrationLocalSeatClientRpc(int killerLocalSeat, ClientRpcParams rpcParams = default)
    {
        if (gpm._audioSource != null)
        {
            if (gpm.goodKill != null) gpm._audioSource.PlayOneShot(gpm.goodKill, 0.95f);
            if (gpm.goodKillEnhanced != null) gpm._audioSource.PlayOneShot(gpm.goodKillEnhanced, 0.95f);
        }
        if (gpm.spotlightPanel) gpm.spotlightPanel.SetActive(true);
        if (gpm.spotlightConfetti)
        {
            gpm.spotlightConfetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gpm.spotlightConfetti.Play(true);
        }

        PauseNonSpotlightParticles();

        if (gpm.spotlight) gpm.spotlight.SetActive(true);
        RotateSpotlightToSeat(killerLocalSeat);
        MaskOnlyKillerBG_NoLayoutShift(killerLocalSeat);
        gpm.StartCoroutine(HideZeroSpotlightAfterDelay());
    }

    [ClientRpc]
    private void KingKillCardClientRpc(int globalSeat, int cardIndex, int killerGlobalSeat)
    {
        int localSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var player = gpm.players[localSeat];
        if (cardIndex < 0 || cardIndex >= player.cardsPanel.cards.Count) return;

        Card killed = player.cardsPanel.cards[cardIndex];
        if (killed == null) return;

        if (killed.Value == CardValue.One && killerGlobalSeat != globalSeat && gpm._audioSource != null)
        {
            if (gpm.goodKill != null) gpm._audioSource.PlayOneShot(gpm.goodKill, 0.95f);
            if (gpm.special_click != null) gpm._audioSource.PlayOneShot(gpm.special_click, 1.0f);
        }

        lastKilledCardPos = killed.transform.position;
        lastKilledCardZRot = killed.transform.rotation.eulerAngles.z;

        var wasteObj = Object.Instantiate(
            gpm._cardPrefab,
            lastKilledCardPos,
            Quaternion.Euler(0, 0, lastKilledCardZRot),
            gpm.cardWastePile.transform.parent
        );

        wasteObj.Type = killed.Type;
        wasteObj.Value = killed.Value;
        wasteObj.IsOpen = true;
        wasteObj.CalcPoint();
        wasteObj.gameObject.AddComponent<WastePile>().Initialize(wasteObj);

        if (killed.Value != CardValue.GoldenJack)
        {
            wasteObj.ShowKilledOutline(true);
            wasteObj.IsClickable = false;
            wasteObj.onClick = null;
        }
        else
        {
            wasteObj.ShowKilledOutline(false);
        }

        float randomRot = Random.Range(-50, 50f);
        gpm.StartCoroutine(gpm.AnimateCardMove(
            wasteObj, lastKilledCardPos, gpm.cardWastePile.transform.position, 0.3f, randomRot
        ));

        player.RemoveCard(killed, false);
    }


    [ClientRpc]
    private void KingRefillHandClientRpc(int globalSeat, int cardIndex, SerializableCard refillCard, SerializableCard[] newDeck)
    {
        int localSeat = gpm.GetLocalIndexFromGlobal(globalSeat);
        if (localSeat < 0 || localSeat >= gpm.players.Count) return;

        var player = gpm.players[localSeat];

        var newCardObj = Instantiate(
            gpm._cardPrefab,
            gpm.cardDeckTransform.position,
            Quaternion.identity,
            player.cardsPanel.transform.parent
        );

        Card newCard = newCardObj.GetComponent<Card>();
        newCard.Type = refillCard.Type;
        newCard.Value = refillCard.Value;
        newCard.IsOpen = false;
        newCard.CalcPoint();

        gpm.StartCoroutine(KingAnimateDeckToHandSlotAndInsert(
            player, cardIndex, newCard, lastKilledCardPos, lastKilledCardZRot, 0.3f
        ));

        if (gpm.draw_card_clip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.draw_card_clip, 0.9f);

        gpm.UpdateDeckVisualLocal(newDeck);
    }

    private static IEnumerator KingAnimateDeckToHandSlotAndInsert(
        Player2 player, int handIndex, Card card, Vector3 toPos, float toZRot, float duration)
    {
        Vector3 from = card.transform.position;
        Quaternion fromRot = card.transform.rotation;
        Quaternion toRot = Quaternion.Euler(0, 0, toZRot);

        float elapsed = 0;
        while (elapsed < duration)
        {
            if (card == null) yield break;
            card.transform.position = Vector3.Lerp(from, toPos, elapsed / duration);
            card.transform.rotation = Quaternion.Slerp(fromRot, toRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (card == null) yield break;

        card.transform.position = toPos;
        card.transform.rotation = toRot;

        player.AddCard(card, handIndex);
        player.cardsPanel.UpdatePos();

        Card justInserted = player.cardsPanel.cards[handIndex];
        if (justInserted != null) justInserted.FlashMarkedOutline();

        yield return new WaitForSeconds(2.0f);

        if (GamePlayManager.instance.IsHost && GamePlayManager.instance.cards.Count == 0)
        {
            GamePlayManager.instance.unoBtn.SetActive(false);
            GamePlayManager.instance.arrowObject.SetActive(false);
        }
    }

    [ClientRpc]
    private void ShowFiendRevengeClientRpc(int victimGlobalSeat)
    {
        if (gpm.fiendRevengeVoiceClip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.fiendRevengeVoiceClip, 0.95f);

        int victimLocalSeat = gpm.GetLocalIndexFromGlobal(victimGlobalSeat);
        if (victimLocalSeat < 0 || victimLocalSeat >= gpm.players.Count) return;

        var victimPlayer = gpm.players[victimLocalSeat];
        victimPlayer.ShowMessage("Fiend's Revenge", true, 2f);
    }

    [ClientRpc]
    private void ShowGoldenJackRevengeClientRpc(int kingUserGlobalSeat, int goldenJackVictimGlobalSeat, ulong killerClientId)
    {
        if (gpm.goldenJackRevengeVoiceClip != null && gpm._audioSource != null)
            gpm._audioSource.PlayOneShot(gpm.goldenJackRevengeVoiceClip, 0.95f);

        ulong myClientId = NetworkManager.Singleton.LocalClientId;
        int kingUserLocalSeat = gpm.GetLocalIndexFromGlobal(kingUserGlobalSeat);
        int victimLocalSeat = gpm.GetLocalIndexFromGlobal(goldenJackVictimGlobalSeat);

        if (victimLocalSeat >= 0 && victimLocalSeat < gpm.players.Count)
            gpm.players[victimLocalSeat].ShowMessage("Golden Jack's Revenge!", true, 2.5f);

        if (kingUserLocalSeat < 0 || kingUserLocalSeat >= gpm.players.Count) return;
        var kingPlayer = gpm.players[kingUserLocalSeat];

        bool isKiller = (myClientId == killerClientId);

        for (int i = 0; i < kingPlayer.cardsPanel.cards.Count; i++)
        {
            var c = kingPlayer.cardsPanel.cards[i];
            if (c == null) continue;

            if (isKiller)
            {
                // Killer sees only a “peeked” flash (no reveal)
                c.FlashEyeOutline();
            }
            else
            {
                // Others see reveal + flash
                c.IsOpen = true;
                c.FlashMarkedOutline();
                kingPlayer.StartCoroutine(HideCardAfterDelay(c, 3.0f));
            }
        }
    }

    private IEnumerator HideCardAfterDelay(Card card, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (card != null) card.IsOpen = false;
    }

    private bool IsBotClientId(ulong clientId) => clientId >= 9000;

    [ClientRpc]
    public void JumbleOwnHandClientRpc(int killerGlobalSeat)
    {
        int killerLocalSeat = gpm.GetLocalIndexFromGlobal(killerGlobalSeat);
        if (killerLocalSeat < 0 || killerLocalSeat >= gpm.players.Count) return;

        var player = gpm.players[killerLocalSeat];
        var hand = player.cardsPanel.cards;

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        int cardCount = hand.Count;
        if (cardCount <= 1) { if (IsHost) gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f)); return; }

        var indices = BuildRandomPermutation(cardCount);

        player.StartCoroutine(
            (Fiend.Instance != null ? Fiend.Instance.JumbleHandAnimation(
                player, hand, indices.ToArray(), 3.0f, true, () =>
                {
                    if (NetworkManager.Singleton.IsHost)
                        gpm.EndAvatarForSeatFromServer(killerGlobalSeat);

                    if (gpm.IsHost && NetworkManager.Singleton.IsServer)
                        NotifyJumbleFinishedServerRpc();
                })
            : FallbackJumble(player, hand, indices))
        );
    }

    private IEnumerator FallbackJumble(Player2 player, List<Card> hand, List<int> indices)
    {
        // instant reorder as a failsafe if Fiend is missing
        var panel = player.cardsPanel;
        var copy = new List<Card>(hand);
        panel.cards.Clear();
        for (int i = 0; i < indices.Count; i++)
            panel.cards.Add(copy[indices[i]]);
        panel.UpdatePos();

        if (NetworkManager.Singleton.IsHost)
            GamePlayManager.instance.StartCoroutine(GamePlayManager.instance.DelayedNextPlayerTurn(0.5f));

        yield break;
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyJumbleFinishedServerRpc(ServerRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.IsServer)
            gpm.StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f));
    }

    public void StartBotKingPhase(ulong botClientId)
    {
        StartCoroutine(BotKingPhaseRoutine(botClientId));
    }

    private IEnumerator BotKingPhaseRoutine(ulong botClientId)
    {
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var candidates = new List<(int seat, int cardIndex)>();
        int botLocalSeat = -1;

        // find bot's local seat
        for (int i = 0; i < gpm.players.Count; i++)
        {
            var pd = MultiplayerManager.Instance.playerDataNetworkList[gpm.GetGlobalIndexFromLocal(i)];
            if (pd.clientId == botClientId) { botLocalSeat = i; break; }
        }

        // choose any opponent card
        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            if (seat == botLocalSeat) continue;
            var player = gpm.players[seat];
            for (int ci = 0; ci < player.cardsPanel.cards.Count; ci++)
                if (player.cardsPanel.cards[ci] != null)
                    candidates.Add((seat, ci));
        }

        if (candidates.Count == 0) yield break;

        var choice = candidates[Random.Range(0, candidates.Count)];
        var card = gpm.players[choice.seat].cardsPanel.cards[choice.cardIndex];
        if (card == null) yield break;

        Vector3 pos = card.transform.position;
        float zRot = card.transform.rotation.eulerAngles.z;

        KingKillCardServerRpc(
            gpm.GetGlobalIndexFromLocal(choice.seat),
            choice.cardIndex,
            pos,
            zRot,
            botLocalSeat,
            new ServerRpcParams { Receive = new ServerRpcReceiveParams { SenderClientId = botClientId } }
        );
    }

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

    private struct BGMaskState { public float a; public bool blocks; public bool interact; }
    private List<BGMaskState> _bgMaskCache;

    private static CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private void MaskOnlyKillerBG_NoLayoutShift(int killerLocalSeat)
    {
        var targets = gpm.spotlightTargets;
        if (targets == null || targets.Length == 0) return;

        if (_bgMaskCache == null || _bgMaskCache.Count != targets.Length)
            _bgMaskCache = new List<BGMaskState>(new BGMaskState[targets.Length]);

        for (int i = 0; i < targets.Length; i++)
        {
            var bg = GetBGObjectForSeat(i);
            if (bg == null) continue;

            var cg = EnsureCanvasGroup(bg);
            if (_bgMaskCache[i].a == 0f && _bgMaskCache[i].blocks == false && _bgMaskCache[i].interact == false)
            {
                _bgMaskCache[i] = new BGMaskState { a = cg.alpha, blocks = cg.blocksRaycasts, interact = cg.interactable };
            }

            bool isKiller = (i == killerLocalSeat);
            cg.alpha = isKiller ? 1f : 0f;
            cg.blocksRaycasts = isKiller;
            cg.interactable = isKiller;
        }
    }

    private void RestoreBGs()
    {
        var targets = gpm.spotlightTargets;
        if (targets == null || _bgMaskCache == null) return;

        int n = Mathf.Min(targets.Length, _bgMaskCache.Count);
        for (int i = 0; i < n; i++)
        {
            var bg = GetBGObjectForSeat(i);
            if (bg == null) continue;
            var cg = EnsureCanvasGroup(bg);
            var st = _bgMaskCache[i];
            cg.alpha = st.a == 0 && !st.blocks && !st.interact ? 1f : st.a;
            cg.blocksRaycasts = st.blocks;
            cg.interactable = st.interact;
        }
    }

    private IEnumerator HideZeroSpotlightAfterDelay()
    {
        yield return new WaitForSeconds(4f);

        if (gpm.spotlightPanel != null) gpm.spotlightPanel.SetActive(false);
        if (gpm.spotlight != null) gpm.spotlight.SetActive(false);

        ResumePausedParticles();
        RestoreBGs();
    }

    private Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == name) return c;
            var r = FindChildRecursive(c, name);
            if (r != null) return r;
        }
        return null;
    }

    private GameObject GetBGObjectForSeat(int localSeat)
    {
        if (gpm.spotlightTargets == null ||
            localSeat < 0 || localSeat >= gpm.spotlightTargets.Length) return null;

        var seat = gpm.spotlightTargets[localSeat];
        if (!seat) return null;

        var bg = FindChildRecursive(seat, "BG");
        return bg ? bg.gameObject : null;
    }

    private int LocalSeatForClient(ulong receiverClientId, int killerGlobalSeat)
    {
        var order = gpm.seatOrderGlobal;
        int n = (order != null) ? order.Count : 0;
        if (n == 0) return 0;

        int recvGlobalIdx = order.IndexOf(receiverClientId);
        if (recvGlobalIdx < 0) return 0;

        int local = killerGlobalSeat - recvGlobalIdx;
        local %= n;
        if (local < 0) local += n;
        return local;
    }

    private readonly List<GameObject> _disabledParticleGOsDuringSpotlight = new List<GameObject>();
    private void PauseNonSpotlightParticles()
    {
        _disabledParticleGOsDuringSpotlight.Clear();

        Transform spotlightRoot = (gpm != null && gpm.spotlightPanel != null)
            ? gpm.spotlightPanel.transform
            : null;

        var all = GameObject.FindObjectsOfType<ParticleSystem>(true);
        foreach (var ps in all)
        {
            if (!ps) continue;

            if (spotlightRoot && ps.transform.IsChildOf(spotlightRoot)) continue;

            var go = ps.gameObject;
            if (go.activeInHierarchy)
            {
                go.SetActive(false);
                _disabledParticleGOsDuringSpotlight.Add(go);
            }
        }
    }

    private void ResumePausedParticles()
    {
        for (int i = 0; i < _disabledParticleGOsDuringSpotlight.Count; i++)
        {
            var go = _disabledParticleGOsDuringSpotlight[i];
            if (go) go.SetActive(true);
        }
        _disabledParticleGOsDuringSpotlight.Clear();
    }


}
