using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Nemesis : NetworkBehaviour
{
    public static Nemesis Instance;
    private GamePlayManager gpm => GamePlayManager.instance;

    public bool isNemesisPhase = false;

    private Coroutine serverNemesisFlow;

    private const float FX_SECONDS = 3f;

    void Awake() { Instance = this; }

    public void StartNemesisPhase()
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return;
        if (!gpm.IsMyTurn() || !gpm.players[0].isUserPlayer) return;

        isNemesisPhase = true;

        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            var p = gpm.players[seat];
            if (p?.cardsPanel?.cards == null) continue;

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
                    if (!isNemesisPhase) return;
                    isNemesisPhase = false;

                    gpm.DisableAllHandCardGlowAllPlayers();

                    int globalTarget = gpm.GetGlobalIndexFromLocal(s);

                    RequestCurseCardServerRpc(globalTarget, idx);

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

    // server applies curse & drives effects
    [ServerRpc(RequireOwnership = false)]
    private void RequestCurseCardServerRpc(int targetGlobalPlayerIndex, int cardIndex, ulong actorClientId = ulong.MaxValue, ServerRpcParams rpcParams = default)
    {
        // who activated? (human sender or explicit bot id)
        ulong nemesisUserId = (actorClientId == ulong.MaxValue)
            ? rpcParams.Receive.SenderClientId
            : actorClientId;

        if (serverNemesisFlow != null) StopCoroutine(serverNemesisFlow);
        serverNemesisFlow = StartCoroutine(ServerNemesisFlow(targetGlobalPlayerIndex, cardIndex, nemesisUserId));
    }

    private IEnumerator ServerNemesisFlow(int targetGlobalPlayerIndex, int cardIndex, ulong nemesisUserId)
    {
        // stop the turn timer & freeze UI state
        if (gpm.turnTimeoutCoroutine != null)
        {
            gpm.StopCoroutine(gpm.turnTimeoutCoroutine);
            gpm.turnTimeoutCoroutine = null;
        }
        gpm.FreezeTimerUI();

        // validate indices
        int localIdx = gpm.GetLocalIndexFromGlobal(targetGlobalPlayerIndex);
        if (localIdx < 0 || localIdx >= gpm.players.Count) { OnNemesisDoneServerRpc(); yield break; }

        var panel = gpm.players[localIdx]?.cardsPanel;
        if (panel == null || panel.cards == null) { OnNemesisDoneServerRpc(); yield break; }
        if (cardIndex < 0 || cardIndex >= panel.cards.Count) { OnNemesisDoneServerRpc(); yield break; }
        if (panel.cards[cardIndex] == null) { OnNemesisDoneServerRpc(); yield break; }

        // Mark the card as cursed on all clients (state only; visuals are gated by IsOpen)
        ApplyCurseStateClientRpc(targetGlobalPlayerIndex, cardIndex);

        // Only the power user sees the exact slot flash
        if (!gpm.IsBotClientId(nemesisUserId))
        {
            FlashCursedOutlineForUserClientRpc(
                targetGlobalPlayerIndex, cardIndex, FX_SECONDS,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { nemesisUserId } }
                }
            );
        }

        // Everyone including the activator sees the victim’s timer pulse purple for 2s
        var realClients = gpm.GetAllHumanClientIds();
        if (realClients.Count > 0)
        {
            ShowVictimPurplePulseClientRpc(
                targetGlobalPlayerIndex, FX_SECONDS,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = realClients }
                }
            );
        }

        // Wait the full effect time before ending power & advancing turn
        yield return new WaitForSeconds(FX_SECONDS);

        OnNemesisDoneServerRpc();
        serverNemesisFlow = null;
    }

    [ClientRpc]
    private void ApplyCurseStateClientRpc(int targetGlobalPlayerIndex, int cardIndex, ClientRpcParams rpcParams = default)
    {
        int local = gpm.GetLocalIndexFromGlobal(targetGlobalPlayerIndex);
        if (local < 0 || local >= gpm.players.Count) return;

        var panel = gpm.players[local]?.cardsPanel;
        if (panel == null || panel.cards == null) return;
        if (cardIndex < 0 || cardIndex >= panel.cards.Count) return;

        var card = panel.cards[cardIndex];
        if (card == null) return;

        // set state only. visuals are gated so the victim won't learn the slot unless the card is face-up
        card.SetCursed(true);
    }

    [ClientRpc]
    private void FlashCursedOutlineForUserClientRpc(int targetGlobalPlayerIndex, int cardIndex, float seconds, ClientRpcParams rpcParams = default)
    {
        int local = gpm.GetLocalIndexFromGlobal(targetGlobalPlayerIndex);
        if (local < 0 || local >= gpm.players.Count) return;

        var panel = gpm.players[local]?.cardsPanel;
        if (panel == null || panel.cards == null) return;
        if (cardIndex < 0 || cardIndex >= panel.cards.Count) return;

        var card = panel.cards[cardIndex];
        if (card == null) return;

        // local-only flash on the exact slot for the activator
        card.FlashCursedOutline(seconds);
    }

    [ClientRpc]
    private void ShowVictimPurplePulseClientRpc(int victimGlobalPlayerIndex, float seconds, ClientRpcParams rpcParams = default)
    {
        StartCoroutine(VictimPulseLocal(victimGlobalPlayerIndex, seconds));
    }

    private IEnumerator VictimPulseLocal(int victimGlobalPlayerIndex, float seconds)
    {
        int local = gpm.GetLocalIndexFromGlobal(victimGlobalPlayerIndex);
        if (local < 0 || local >= gpm.players.Count) yield break;

        var p = gpm.players[local];
        if (p?.timerOjbect == null) yield break;

        var img = p.timerOjbect.GetComponent<UnityEngine.UI.Image>();
        if (img != null) img.color = new Color(0.60f, 0.35f, 1.0f, 1f); // purple

        p.timerOjbect.SetActive(true);
        p.ShowMessage("1 Card Cursed!", true, Mathf.Min(seconds, 2f));

        float elapsed = 0f;
        var c0 = img ? img.color : Color.white;
        while (elapsed < seconds)
        {
            if (img)
            {
                float a = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(elapsed * 6f));
                img.color = new Color(c0.r, c0.g, c0.b, a);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (img != null) img.color = new Color(0.572f, 0.463f, 0.125f, 1f); // default yellow
        p.timerOjbect.SetActive(false);
    }

    [ServerRpc(RequireOwnership = false)]
    private void OnNemesisDoneServerRpc(ServerRpcParams rpcParams = default)
    {
        isNemesisPhase = false;

        if (gpm.currentPowerOwnerGlobalSeat >= 0)
        {
            var targets = gpm.GetAllHumanClientIds();
            if (targets.Count > 0)
                gpm.EndPowerAvatarClientRpc(
                    gpm.currentPowerOwnerGlobalSeat,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = targets } }
                );
        }
        gpm.currentPowerOwnerGlobalSeat = -1;

        if (IsHost)
            StartCoroutine(gpm.DelayedNextPlayerTurn(0.5f, gpm.CurrentTurnSerial));
    }

    // bot handling
    public void StartBotNemesisCurse(ulong botClientId) => StartCoroutine(BotNemesis(botClientId));

    private IEnumerator BotNemesis(ulong botClientId)
    {
        isNemesisPhase = true;
        yield return new WaitForSeconds(Random.Range(1f, 2f));

        var seats = new List<int>();
        for (int seat = 0; seat < gpm.players.Count; seat++)
        {
            int g = gpm.GetGlobalIndexFromLocal(seat);
            ulong cid = gpm.GetClientIdFromGlobalSeat(g);
            if (cid == botClientId) continue;
            var panel = gpm.players[seat]?.cardsPanel;
            if (panel != null && panel.cards != null && panel.cards.Exists(c => c != null))
                seats.Add(seat);
        }
        if (seats.Count == 0) { isNemesisPhase = false; yield break; }

        int tSeat = seats[Random.Range(0, seats.Count)];
        var tPanel = gpm.players[tSeat].cardsPanel;
        int idx = -1;
        for (int tries = 0; tries < 8 && idx < 0; tries++)
        {
            int tryIdx = Random.Range(0, tPanel.cards.Count);
            if (tPanel.cards[tryIdx] != null) idx = tryIdx;
        }
        if (idx < 0) { isNemesisPhase = false; yield break; }

        int globalTarget = gpm.GetGlobalIndexFromLocal(tSeat);

        RequestCurseCardServerRpc(globalTarget, idx, botClientId);

        isNemesisPhase = false;
    }
}
