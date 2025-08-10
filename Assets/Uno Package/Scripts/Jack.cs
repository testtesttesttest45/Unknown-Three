using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Jack : NetworkBehaviour
{
    public static Jack Instance;

    public bool isJackRevealPhase = false;
    public bool isGoldenJackRevealPhase = false;

    private ulong currentJackUserClientId = ulong.MaxValue;
    private bool currentJackUserIsBot = false;
    private bool botGoldenJackRevealActive = false;
    private bool botJackRevealActive = false;

    private GamePlayManager gpm => GamePlayManager.instance;

    void Awake() => Instance = this;

    // reveal one hand card

    void OnJackCardDiscardedByMe()
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;
        if (!gpm.IsMyTurn() || !gpm.players[0].isUserPlayer) return;

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
        StartCoroutine(HideCardAfterDelay(card, 2f));
    }

    [ClientRpc]
    void PlayJackZeroBonusSoundClientRpc(ClientRpcParams rpcParams = default)
    {
        if (gpm._audioSource != null && gpm.jackSpecialVoiceClip != null)
            gpm._audioSource.PlayOneShot(gpm.jackSpecialVoiceClip);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestRevealHandCardServerRpc(int playerIndex, int cardIndex, ServerRpcParams serverRpcParams = default)
    {
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
            Debug.LogWarning("RequestRevealHandCardServerRpc called for bot—should be simulated path.");
            FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, true);
            OnJackRevealDoneServerRpc();
            return;
        }

        // Reveal for Jack user only
        RevealHandCardClientRpc(
            playerIndex, cardIndex, handCard.Type, handCard.Value, jackUserClientId,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { jackUserClientId } }
            });

        // Others see a flash outline
        FlashMarkedOutlineClientRpc(playerIndex, cardIndex, jackUserClientId, false);

        StartCoroutine(ExposeZeroAfterDelayCoroutine(jackUserClientId));
    }

    private IEnumerator ExposeZeroAfterDelayCoroutine(ulong jackUserClientId)
    {
        yield return new WaitForSeconds(1.0f);

        // Find the player (not Jack user) who has the Zero
        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(pi);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue) continue;         // empty / disconnected seat
            if (pClientId == jackUserClientId) continue;       // skip Jack user

            var panel = gpm.players[pi]?.cardsPanel;
            if (panel == null || panel.cards == null) continue;

            for (int ci = 0; ci < panel.cards.Count; ci++)
            {
                var c = panel.cards[ci];
                if (c == null) continue;
                if (c.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            var allRealClientIds = gpm.GetAllHumanClientIds();

            if (allRealClientIds.Count > 0)
            {
                PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = allRealClientIds }
                });
            }

            ShowZeroHolderTimerEffectClientRpc(gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex));
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
        int localIndex = gpm.GetLocalIndexFromGlobal(globalPlayerIndex);
        if (localIndex < 0 || localIndex >= gpm.players.Count) return;

        var player = gpm.players[localIndex];
        if (player?.timerOjbect != null)
        {
            player.timerOjbect.SetActive(true);
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = Color.red;
            StartCoroutine(RestoreTimerEffectAfterDelay(player, 3.0f));
        }
    }

    private IEnumerator RestoreTimerEffectAfterDelay(Player2 player, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (player?.timerOjbect != null)
        {
            var img = player.timerOjbect.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(0.572f, 0.463f, 0.125f, 1f); // #927620
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
        // If Jack user is a human and this client IS the Jack user, skip the flash (they already saw the reveal)
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
        isJackRevealPhase = false;

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

        currentJackUserClientId = ulong.MaxValue;
        currentJackUserIsBot = false;
    }

    // reveal all (GOlden Jack)

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

        // Reveal to the Golden Jack user only
        RevealAllHandCardsClientRpc(
            playerIndex,
            revealInfos.ToArray(),
            goldenJackUserClientId,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { goldenJackUserClientId } } }
        );

        // Others see flash
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            FlashMarkedOutlineClientRpc(playerIndex, i, goldenJackUserClientId, false);
        }

        StartCoroutine(ExposeZeroAfterDelayCoroutine(goldenJackUserClientId));
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
            StartCoroutine(HideCardAfterDelay(card, 2f));
        }
    }

    // bot handling

    public IEnumerator SimulateBotGoldenJackReveal(ulong botClientId)
    {
        if (botGoldenJackRevealActive) yield break;
        botGoldenJackRevealActive = true;

        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var validTargets = new List<int>();
        for (int i = 0; i < gpm.players.Count; i++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(i);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue) continue; // empty / disconnected
            if (pClientId == botClientId) continue;    // skip self
            var p = gpm.players[i];
            if (p != null && p.isInRoom && p.cardsPanel?.cards != null && p.cardsPanel.cards.Count > 0)
                validTargets.Add(i);
        }

        if (validTargets.Count == 0)
        {
            Debug.LogWarning("[SimulateBotGoldenJackReveal] No valid target players!");
            botGoldenJackRevealActive = false;
            yield break;
        }

        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];
        var targetPlayer = gpm.players[targetPlayerIndex];

        var realClientIds = gpm.GetAllHumanClientIds();
        for (int i = 0; i < targetPlayer.cardsPanel.cards.Count; i++)
        {
            FlashMarkedOutlineClientRpc(
                gpm.GetGlobalIndexFromLocal(targetPlayerIndex),
                i,
                botClientId,
                false,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = realClientIds } }
            );
        }

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        yield return new WaitForSeconds(1.0f);

        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(pi);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue) continue;
            if (pClientId == botClientId) continue;

            var panel = gpm.players[pi]?.cardsPanel;
            if (panel == null || panel.cards == null) continue;

            for (int ci = 0; ci < panel.cards.Count; ci++)
            {
                var c = panel.cards[ci];
                if (c == null) continue;
                if (c.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            var allRealClientIds = gpm.GetAllHumanClientIds();
            if (allRealClientIds.Count > 0)
            {
                PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = allRealClientIds }
                });
            }

            ShowZeroHolderTimerEffectClientRpc(gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex));
            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
            OnJackRevealDoneServerRpc();
        }

        botGoldenJackRevealActive = false;
    }

    public IEnumerator SimulateBotJackReveal(ulong botClientId)
    {
        if (botJackRevealActive)
        {
            Debug.LogWarning("SimulateBotJackReveal: Already active, ignoring duplicate call.");
            yield break;
        }
        botJackRevealActive = true;

        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var validTargets = new List<int>();
        for (int i = 0; i < gpm.players.Count; i++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(i);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue) continue; // empty / disconnected
            if (pClientId == botClientId) continue;    // skip self

            var p = gpm.players[i];
            if (p != null && p.isInRoom && p.cardsPanel?.cards != null && p.cardsPanel.cards.Count > 0)
                validTargets.Add(i);
        }

        if (validTargets.Count == 0)
        {
            Debug.LogWarning("[SimulateBotJackReveal] No valid target players!");
            botJackRevealActive = false;
            yield break;
        }

        int targetPlayerIndex = validTargets[Random.Range(0, validTargets.Count)];
        var tgtPanel = gpm.players[targetPlayerIndex]?.cardsPanel;
        if (tgtPanel == null || tgtPanel.cards == null || tgtPanel.cards.Count == 0)
        {
            botJackRevealActive = false;
            yield break;
        }
        int targetCardIndex = Random.Range(0, tgtPanel.cards.Count);

        var realClientIds = gpm.GetAllHumanClientIds();
        FlashMarkedOutlineClientRpc(
            gpm.GetGlobalIndexFromLocal(targetPlayerIndex),
            targetCardIndex,
            botClientId,
            true,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = realClientIds } }
        );

        gpm.FreezeTimerUI();
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }

        yield return new WaitForSeconds(1.0f);

        int zeroHolderPlayerIndex = -1;
        for (int pi = 0; pi < gpm.players.Count; pi++)
        {
            int globalSeat = gpm.GetGlobalIndexFromLocal(pi);
            ulong pClientId = gpm.GetClientIdFromGlobalSeat(globalSeat);
            if (pClientId == ulong.MaxValue) continue;
            if (pClientId == botClientId) continue;

            var panel = gpm.players[pi]?.cardsPanel;
            if (panel == null || panel.cards == null) continue;

            for (int ci = 0; ci < panel.cards.Count; ci++)
            {
                var c = panel.cards[ci];
                if (c == null) continue;
                if (c.Value == CardValue.Zero)
                {
                    zeroHolderPlayerIndex = pi;
                    break;
                }
            }
            if (zeroHolderPlayerIndex != -1) break;
        }

        if (zeroHolderPlayerIndex != -1)
        {
            var allRealClientIds = gpm.GetAllHumanClientIds();
            if (allRealClientIds.Count > 0)
            {
                PlayJackZeroBonusSoundClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = allRealClientIds }
                });
            }

            ShowZeroHolderTimerEffectClientRpc(gpm.GetGlobalIndexFromLocal(zeroHolderPlayerIndex));
            StartCoroutine(JackZeroBonusEndTurnCoroutine());
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
            OnJackRevealDoneServerRpc();
        }

        botJackRevealActive = false;
    }
}