using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Jack : NetworkBehaviour
{
    public static Jack Instance;

    public bool isJackRevealPhase = false;
    public bool isGoldenJackRevealPhase = false;

    private bool botGoldenJackRevealActive = false;
    private bool botJackRevealActive = false;

    private const float JACK_VO_LEN = 2f;
    private const float GOLDEN_JACK_VO_LEN = 4f;
    private const float REVEAL_LEN = 2f;
    private const float EXPOSE_VO_LEN = 2f;

    private float jackVoStartServerTime = -1f;
    private float goldenVoStartServerTime = -1f;

    private Coroutine _exposeGateCo;
    private int _powerEpoch = 0;
    private ulong _powerUserClientId = ulong.MaxValue;
    private AudioSource _voChannel;
    private const float JACK_EXPOSE_EXTRA = 2f;
    private const float GOLDEN_JACK_EXPOSE_EXTRA = 2f;
    private readonly Dictionary<Player2, int> _timerExposeEpoch = new Dictionary<Player2, int>();

    private GamePlayManager gpm => GamePlayManager.instance;

    void Awake()
    {
        Instance = this;
        if (_voChannel == null)
        {
            _voChannel = gameObject.AddComponent<AudioSource>();
            _voChannel.playOnAwake = false;
            _voChannel.loop = false;
        }
    }

    public void MarkVoStart(CardValue v)
    {
        float t = Time.unscaledTime;
        if (v == CardValue.Jack) jackVoStartServerTime = t;
        else if (v == CardValue.GoldenJack) goldenVoStartServerTime = t;
    }

    private void CancelExposeGate()
    {
        if (_exposeGateCo != null)
        {
            StopCoroutine(_exposeGateCo);
            _exposeGateCo = null;
        }
    }

    private void StartExposeGate(ulong userId, float voLen, float voStartServerTime)
    {
        CancelExposeGate();
        _powerUserClientId = userId;
        int epoch = ++_powerEpoch;
        _exposeGateCo = StartCoroutine(ExposeZeroAfterBothCoroutine(userId, voLen, voStartServerTime, epoch));
    }

    private IEnumerator StartExposeGateBlocking(ulong userId, float voLen, float voStartServerTime)
    {
        CancelExposeGate();
        _powerUserClientId = userId;
        int epoch = ++_powerEpoch;
        _exposeGateCo = StartCoroutine(ExposeZeroAfterBothCoroutine(userId, voLen, voStartServerTime, epoch));
        while (_exposeGateCo != null && epoch == _powerEpoch)
            yield return null;
    }

    void OnJackCardDiscardedByMe()
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;
        if (!gpm.IsMyTurn() || !gpm.players[0].isUserPlayer) return;
        gpm.ShowTooltipOverlay("Jack Power: Pick a card to Reveal!");
        isJackRevealPhase = true;

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p = gpm.players[seat];
            if (p == null || p.cardsPanel == null || p.cardsPanel.cards == null) continue;

            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
            {
                var handCard = p.cardsPanel.cards[i];
                if (handCard == null) continue;

                handCard.ShowGlow(true);
                handCard.IsClickable = true;
                int s = seat, idx = i;
                handCard.onClick = null;
                handCard.onClick = _ =>
                {
                    if (!isJackRevealPhase) return;
                    isJackRevealPhase = false;

                    gpm.DisableAllHandCardGlowAllPlayers();
                    int globalPlayerIndex = gpm.GetGlobalIndexFromLocal(s);
                    RequestRevealHandCardServerRpc(globalPlayerIndex, idx);
                    gpm.UpdateDeckClickability();
                };
            }
        }

        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    [ClientRpc]
    public void StartJackRevealLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        OnJackCardDiscardedByMe();
    }

    IEnumerator HideCardAfterDelay(Card card, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (card != null) card.IsOpen = false;
    }

    [ClientRpc]
    void RevealHandCardClientRpc(
        int playerIndex, int cardIndex, CardType type, CardValue value, ulong jackUserClientId,
        ClientRpcParams rpcParams = default)
    {
        // Only reveal to the jack user (and only if human)
        if (gpm.IsBotClientId(jackUserClientId)) return;
        if (gpm.IsBotClientId(NetworkManager.Singleton.LocalClientId)) return;
        if (NetworkManager.Singleton.LocalClientId != jackUserClientId) return;

        int local = gpm.GetLocalIndexFromGlobal(playerIndex);
        if (local < 0 || local >= gpm.players.Count) return;

        var panel = gpm.players[local]?.cardsPanel;
        if (panel == null || panel.cards == null) return;
        if (cardIndex < 0 || cardIndex >= panel.cards.Count) return;

        var card = panel.cards[cardIndex];
        if (card == null) return;

        card.Type = type;
        card.Value = value;
        card.IsOpen = true;
        card.FlashMarkedOutline();
        StartCoroutine(HideCardAfterDelay(card, REVEAL_LEN));
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealHandCardServerRpc(int playerIndex, int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        // Freeze timer immediately
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        int localIdx = gpm.GetLocalIndexFromGlobal(playerIndex);
        if (localIdx < 0 || localIdx >= gpm.players.Count) { OnJackRevealDoneServerRpc(); return; }

        var panel = gpm.players[localIdx]?.cardsPanel;
        if (panel == null || panel.cards == null) { OnJackRevealDoneServerRpc(); return; }
        if (cardIndex < 0 || cardIndex >= panel.cards.Count) { OnJackRevealDoneServerRpc(); return; }

        var handCard = panel.cards[cardIndex];
        if (handCard == null) { OnJackRevealDoneServerRpc(); return; }

        ulong jackUserClientId = serverRpcParams.Receive.SenderClientId;
        if (gpm.IsBotClientId(jackUserClientId))
        {
            FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, true);
            StartExposeGate(jackUserClientId, JACK_VO_LEN, jackVoStartServerTime);
            return;
        }

        RevealHandCardClientRpc(
            playerIndex, cardIndex, handCard.Type, handCard.Value, jackUserClientId,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { jackUserClientId } }
            });

        FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, false);

        StartExposeGate(jackUserClientId, JACK_VO_LEN, jackVoStartServerTime);
    }

    private IEnumerator ExposeZeroAfterBothCoroutine(ulong powerUserClientId, float voLen, float voStartServerTime, int epoch)
    {
        float voRemaining = (voStartServerTime < 0f)
            ? voLen
            : Mathf.Max(0f, (voStartServerTime + voLen) - Time.unscaledTime);

        float wait = Mathf.Max(voRemaining, REVEAL_LEN);
        if (wait > 0f) yield return new WaitForSeconds(wait);

        if (epoch != _powerEpoch) { _exposeGateCo = null; yield break; }

        // Zero check (exclude power user)
        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(pi);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue || pClientId == powerUserClientId) continue;

            var panel = gpm.players[pi]?.cardsPanel;
            if (panel == null || panel.cards == null) continue;

            for (int ci = 0; ci < panel.cards.Count; ci++)
            {
                var c = panel.cards[ci];
                if (c != null && c.Value == CardValue.Zero) { zeroHolderPlayerIndex = pi; break; }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            if (epoch != _powerEpoch) { _exposeGateCo = null; yield break; } // recheck before VO

            var allRealClientIds = gpm.GetAllHumanClientIds();
            if (allRealClientIds.Count > 0)
            {
                float extra = 0f;
                if (Mathf.Approximately(voLen, JACK_VO_LEN))
                    extra = JACK_EXPOSE_EXTRA;
                else if (Mathf.Approximately(voLen, GOLDEN_JACK_VO_LEN))
                    extra = GOLDEN_JACK_EXPOSE_EXTRA;

                float exposeSeconds = EXPOSE_VO_LEN + extra;

                StartExposeEffectClientRpc(
                    gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex),
                    voLen,
                    exposeSeconds,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = allRealClientIds } }
                );

                yield return new WaitForSeconds(exposeSeconds);
            }

            if (epoch != _powerEpoch) { _exposeGateCo = null; yield break; }
        }

        _exposeGateCo = null;
        OnJackRevealDoneServerRpc();
    }

    private IEnumerator RestoreTimerEffectAfterDelay(Player2 player, float delay, int epoch)
    {
        yield return new WaitForSeconds(delay);

        // If a newer expose started for this player, abort this older restore
        if (_timerExposeEpoch.TryGetValue(player, out var currentEpoch) && currentEpoch != epoch)
            yield break;

        if (player?.timerOjbect != null)
        {
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(0.572f, 0.463f, 0.125f, 1f); // #927620
            player.timerOjbect.SetActive(false);
        }

        // clean up
        _timerExposeEpoch.Remove(player);
    }

    [ClientRpc]
    void FlashMarkedOutlineClientRpc(int playerIndex, int cardIndex, ulong jackUserClientId, bool jackUserIsBot, ClientRpcParams rpcParams = default)
    {
        if (!jackUserIsBot && NetworkManager.Singleton.LocalClientId == jackUserClientId)
            return;

        int local = gpm.GetLocalIndexFromGlobal(playerIndex);
        if (local < 0 || local >= gpm.players.Count) return;

        var panel = gpm.players[local]?.cardsPanel;
        if (panel == null || panel.cards == null) return;
        if (cardIndex < 0 || cardIndex >= panel.cards.Count) return;

        var card = panel.cards[cardIndex];
        if (card == null) return;

        card.IsOpen = false;
        card.FlashEyeOutline();
    }

    [ServerRpc(RequireOwnership = false)]
    void OnJackRevealDoneServerRpc(ServerRpcParams rpcParams = default)
    {
        CancelExposeGate();
        jackVoStartServerTime = -1f;
        goldenVoStartServerTime = -1f;

        isJackRevealPhase = false;
        isGoldenJackRevealPhase = false;

        if (gpm.currentPowerOwnerGlobalSeat >= 0)
        {
            var targets = gpm.GetAllHumanClientIds();
            if (targets.Count > 0)
            {
                gpm.EndPowerAvatarClientRpc(
                    gpm.currentPowerOwnerGlobalSeat,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
                );
            }
        }
        gpm.currentPowerOwnerGlobalSeat = -1;

        if (IsHost)
            StartCoroutine(gpm.DelayedNextPlayerTurn(1.0f));
    }

    [ClientRpc]
    public void StartGoldenJackRevealLocalOnlyClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        OnGoldenJackCardDiscardedByMe();
    }

    void OnGoldenJackCardDiscardedByMe()
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;
        if (!gpm.IsMyTurn() || !gpm.players[0].isUserPlayer) return;

        gpm.ShowTooltipOverlay("Golden Jack Power: Pick a card to Reveal!");
        isGoldenJackRevealPhase = true;

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p = gpm.players[seat];
            if (p == null || p.cardsPanel == null || p.cardsPanel.cards == null) continue;

            for (int i = 0; i < p.cardsPanel.cards.Count; i++)
            {
                var handCard = p.cardsPanel.cards[i];
                if (handCard == null) continue;

                handCard.ShowGlow(true);
                handCard.IsClickable = true;
                int s = seat, idx = i;
                handCard.onClick = null;
                handCard.onClick = _ =>
                {
                    if (!isGoldenJackRevealPhase) return;
                    isGoldenJackRevealPhase = false;

                    gpm.DisableAllHandCardGlowAllPlayers();
                    int globalPlayerIndex = gpm.GetGlobalIndexFromLocal(s);
                    RequestRevealAllHandCardsServerRpc(globalPlayerIndex);
                    gpm.UpdateDeckClickability();
                };
            }
        }

        if (gpm.players[0].isUserPlayer)
        {
            gpm.players[0].SetTimerVisible(true);
            gpm.players[0].UpdateTurnTimerUI(gpm.turnTimerDuration, gpm.turnTimerDuration);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealAllHandCardsServerRpc(int playerIndex, ServerRpcParams serverRpcParams = default)
    {
        // Freeze timer immediately
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        ulong goldenJackUserClientId = serverRpcParams.Receive.SenderClientId;

        int localIdx = gpm.GetLocalIndexFromGlobal(playerIndex);
        if (localIdx < 0 || localIdx >= gpm.players.Count) { OnJackRevealDoneServerRpc(); return; }

        var targetPlayer = gpm.players[localIdx];
        if (targetPlayer?.cardsPanel == null || targetPlayer.cardsPanel.cards == null)
        {
            OnJackRevealDoneServerRpc();
            return;
        }

        var revealInfos = new List<CardRevealInfo>();
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            var handCard = targetPlayer.cardsPanel.cards[i];
            if (handCard == null) continue;
            revealInfos.Add(new CardRevealInfo { cardIndex = i, type = handCard.Type, value = handCard.Value });
        }

        RevealAllHandCardsClientRpc(
            playerIndex,
            revealInfos.ToArray(),
            goldenJackUserClientId,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { goldenJackUserClientId } } }
        );

        // Others see flash for all cards
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
            FlashMarkedOutlineClientRpc(playerIndex, i, goldenJackUserClientId, false);

        // Gate expose golden jack VO (4s) AND reveals (2s)
        StartExposeGate(goldenJackUserClientId, GOLDEN_JACK_VO_LEN, goldenVoStartServerTime);
    }

    [ClientRpc]
    void RevealAllHandCardsClientRpc(int playerIndex, CardRevealInfo[] infos, ulong goldenJackUserClientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId != goldenJackUserClientId) return;

        int local = gpm.GetLocalIndexFromGlobal(playerIndex);
        if (local < 0 || local >= gpm.players.Count) return;

        var panel = gpm.players[local]?.cardsPanel;
        if (panel == null || panel.cards == null) return;

        foreach (var info in infos)
        {
            if (info.cardIndex < 0 || info.cardIndex >= panel.cards.Count) continue;
            var card = panel.cards[info.cardIndex];
            if (card == null) continue;

            card.Type = info.type;
            card.Value = info.value;
            card.IsOpen = true;
            card.FlashMarkedOutline();
            StartCoroutine(HideCardAfterDelay(card, REVEAL_LEN));
        }
    }

    // bot handling
    public IEnumerator SimulateBotJackReveal(ulong botClientId)
    {
        if (botJackRevealActive) yield break;
        botJackRevealActive = true;
        isJackRevealPhase = true;

        yield return new WaitForSeconds(Random.Range(1f, 2f));

        // pick target & flash one card ...
        var validTargets = new List<int>();
        for (int i = 0; i < gpm.players.Count; i++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(i);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue || pClientId == botClientId) continue;
            var p = gpm.players[i];
            if (p != null && p.isInRoom && p.cardsPanel?.cards != null && p.cardsPanel.cards.Count > 0)
                validTargets.Add(i);
        }
        if (validTargets.Count == 0) { botJackRevealActive = false; isJackRevealPhase = false; yield break; }

        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];
        var tgtPanel = gpm.players[targetPlayerIndex]?.cardsPanel;
        if (tgtPanel == null || tgtPanel.cards == null || tgtPanel.cards.Count == 0)
        { botJackRevealActive = false; isJackRevealPhase = false; yield break; }

        int targetCardIndex = Random.Range(0, tgtPanel.cards.Count);
        var realClientIds = gpm.GetAllHumanClientIds();
        FlashMarkedOutlineClientRpc(
            gpm.GetGlobalIndexFromLocal(targetPlayerIndex),
            targetCardIndex,
            botClientId,
            true, // bot
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = realClientIds } }
        );

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        yield return StartExposeGateBlocking(botClientId, JACK_VO_LEN, jackVoStartServerTime);

        botJackRevealActive = false;
        isJackRevealPhase = false;
    }

    public IEnumerator SimulateBotGoldenJackReveal(ulong botClientId)
    {
        if (botGoldenJackRevealActive) yield break;
        botGoldenJackRevealActive = true;
        isGoldenJackRevealPhase = true;

        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var validTargets = new List<int>();
        for (int i = 0; i < gpm.players.Count; i++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(i);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue || pClientId == botClientId) continue;
            var p = gpm.players[i];
            if (p != null && p.isInRoom && p.cardsPanel?.cards != null && p.cardsPanel.cards.Count > 0)
                validTargets.Add(i);
        }
        if (validTargets.Count == 0) { botGoldenJackRevealActive = false; isGoldenJackRevealPhase = false; yield break; }

        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];
        var targetPlayer = gpm.players[targetPlayerIndex];

        var realClientIds = gpm.GetAllHumanClientIds();
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            FlashMarkedOutlineClientRpc(
                gpm.GetGlobalIndexFromLocal(targetPlayerIndex),
                i,
                botClientId,
                true,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = realClientIds } }
            );
        }

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        yield return StartExposeGateBlocking(botClientId, GOLDEN_JACK_VO_LEN, goldenVoStartServerTime);

        botGoldenJackRevealActive = false;
        isGoldenJackRevealPhase = false;
    }

    [ClientRpc]
    void StartExposeEffectClientRpc(int globalZeroHolderIndex, float baseVoLen, float exposeSeconds, ClientRpcParams rpcParams = default)
    {
        StartCoroutine(StartExposeEffectLocal(globalZeroHolderIndex, baseVoLen, exposeSeconds));
    }

    private IEnumerator StartExposeEffectLocal(int globalZeroHolderIndex, float baseVoLen, float exposeSeconds)
    {
        float waited = 0f;
        float killAfter = Mathf.Max(0.1f, baseVoLen + 0.2f);
        if (gpm._audioSource != null)
        {
            while (gpm._audioSource.isPlaying && waited < killAfter)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (gpm._audioSource.isPlaying)
                gpm._audioSource.Stop();
        }

        int localIndex = gpm.GetLocalIndexFromGlobal(globalZeroHolderIndex);
        if (localIndex >= 0 && localIndex < gpm.players.Count)
        {
            var player = gpm.players[localIndex];
            if (player?.timerOjbect != null)
            {
                player.timerOjbect.SetActive(true);
                var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.color = Color.red;

                // NEW: bump epoch and pass it
                int epoch = (_timerExposeEpoch.TryGetValue(player, out var e) ? e : 0) + 1;
                _timerExposeEpoch[player] = epoch;
                StartCoroutine(RestoreTimerEffectAfterDelay(player, exposeSeconds, epoch));

                player.ShowMessage("Zero detected", true, Mathf.Min(exposeSeconds, 1.5f));
                if (gpm.exposed != null) gpm._audioSource.PlayOneShot(gpm.exposed, 0.2f);
            }
        }

        if (_voChannel == null)
        {
            _voChannel = gameObject.AddComponent<AudioSource>();
            _voChannel.playOnAwake = false;
            _voChannel.loop = false;
        }
        _voChannel.Stop();
        if (gpm.jackSpecialVoiceClip != null)
            _voChannel.PlayOneShot(gpm.jackSpecialVoiceClip);
    }

    public bool IsPowerFlowActive
    {
        get
        {
            return isJackRevealPhase || isGoldenJackRevealPhase || _exposeGateCo != null;
        }
    }

}
