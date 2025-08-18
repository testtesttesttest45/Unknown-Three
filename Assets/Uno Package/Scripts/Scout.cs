using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class Scout : NetworkBehaviour
{
    public static Scout Instance;
    private GamePlayManager gpm => GamePlayManager.instance;

    private Coroutine _immediateCo;
    private Coroutine _passiveWatchCo;
    private bool _immediateActive = false;

    private readonly Dictionary<int, GameObject> _seatToTotalContainer = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, TextMeshProUGUI> _seatToTotalTMP = new Dictionary<int, TextMeshProUGUI>();

    void Awake() => Instance = this;

    void OnEnable()
    {
        if (_passiveWatchCo == null)
            _passiveWatchCo = StartCoroutine(Co_PassiveOwnershipWatcher());
    }

    void OnDisable()
    {
        if (_immediateCo != null) { StopCoroutine(_immediateCo); _immediateCo = null; }
        if (_passiveWatchCo != null) { StopCoroutine(_passiveWatchCo); _passiveWatchCo = null; }
        _immediateActive = false;
        SetTotalsVisible(false, excludeSelf: false);
        _seatToTotalTMP.Clear();
    }

    [ClientRpc]
    public void StartScoutRevealLocalOnlyClientRpc(ulong targetClientId, float durationSec, ClientRpcParams rpcParams = default)
    {
        // local guard (prevents showing on non-targets and host during bot turns)
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        StartImmediateRevealLocal(durationSec);
    }

    public void StartImmediateRevealLocal(float durationSec)
    {
        if (_immediateCo != null) StopCoroutine(_immediateCo);
        _immediateCo = StartCoroutine(Co_ImmediateReveal(durationSec));
    }

    private IEnumerator Co_ImmediateReveal(float duration)
    {
        _immediateActive = true;

        SetTotalsVisible(true, excludeSelf: false);

        float t = 0f;
        const float tick = 0.25f;
        while (t < duration)
        {
            UpdateAllTotalsText(excludeSelf: false);
            t += tick;
            yield return new WaitForSeconds(tick);
        }

        SetTotalsVisible(false, excludeSelf: false);
        _immediateActive = false;
        _immediateCo = null;
    }


    private IEnumerator Co_PassiveOwnershipWatcher()
    {
        yield return null;

        bool showing = false;

        while (true)
        {
            bool iHaveScout = LocalPlayerHasScout();

            if (!_immediateActive)
            {
                if (iHaveScout && !showing)
                {
                    showing = true;
                    SetTotalsVisible(true, excludeSelf: true);
                }
                else if (!iHaveScout && showing)
                {
                    showing = false;
                    SetTotalsVisible(false, excludeSelf: true);
                }

                if (showing)
                    UpdateAllTotalsText(excludeSelf: true);
            }

            yield return new WaitForSeconds(0.35f);
        }
    }

    public void NotifyHandChangedLocal()
    {
        if (_immediateActive) return;
        bool iHaveScout = LocalPlayerHasScout();

        SetTotalsVisible(iHaveScout, excludeSelf: true);
        if (iHaveScout)
            UpdateAllTotalsText(excludeSelf: true);
    }


    private bool LocalPlayerHasScout()
    {
        if (gpm == null || gpm.players == null || gpm.players.Count == 0) return false;

        int myLocalSeat = -1;
        for (int i = 0; i < gpm.players.Count; i++)
            if (gpm.players[i] != null && gpm.players[i].isUserPlayer) { myLocalSeat = i; break; }

        if (myLocalSeat < 0) return false;

        var p = gpm.players[myLocalSeat];
        if (p?.cardsPanel?.cards == null) return false;

        foreach (var c in p.cardsPanel.cards)
        {
            if (c == null) continue;
            if (c.Type == CardType.AntiMatter && c.Value == CardValue.Scout)
                return true;
        }
        return false;
    }

    [ClientRpc]
    public void ScoutFlashMarkedAllClientRpc(float durationSeconds, ClientRpcParams rpcParams = default)
    {
        if (gpm == null || gpm.players == null) return;

        foreach (var p in gpm.players)
        {
            if (p?.cardsPanel?.cards == null) continue;
            foreach (var card in p.cardsPanel.cards)
            {
                if (card == null) continue;
                card.FlashMarkedOutline(durationSeconds);
            }
        }
    }

    private void SetTotalsVisible(bool visible, bool excludeSelf)
    {
        if (gpm == null || gpm.players == null) return;

        for (int localSeat = 0; localSeat < gpm.players.Count; localSeat++)
        {
            var p = gpm.players[localSeat];
            if (p == null) continue;
            if (excludeSelf && p.isUserPlayer) continue;

            var tmp = GetOrFindTotalPointsTMP(localSeat);
            GameObject container = null;
            _seatToTotalContainer.TryGetValue(localSeat, out container);

            if (container != null) container.SetActive(visible);
            else if (tmp != null) tmp.gameObject.SetActive(visible);
        }
    }

    private void UpdateAllTotalsText(bool excludeSelf)
    {
        if (gpm == null || gpm.players == null) return;

        for (int localSeat = 0; localSeat < gpm.players.Count; localSeat++)
        {
            var p = gpm.players[localSeat];
            if (p == null) continue;

            if (excludeSelf && p.isUserPlayer) continue;

            var tmp = GetOrFindTotalPointsTMP(localSeat);
            if (tmp != null)
            {
                int pts = p.GetTotalPoints();
                tmp.text = pts.ToString();
            }
        }
    }

    private TextMeshProUGUI GetOrFindTotalPointsTMP(int localSeat)
    {
        if (_seatToTotalTMP.TryGetValue(localSeat, out var cachedTmp) && cachedTmp != null)
            return cachedTmp;

        if (gpm == null || gpm.players == null) return null;
        var p = gpm.players[localSeat];
        if (p == null) return null;

        TextMeshProUGUI tmp = p.totalPointsTMP;
        
        // Cache TMP
        _seatToTotalTMP[localSeat] = tmp;

        _seatToTotalContainer[localSeat] = FindTotalsContainer(tmp, (p as MonoBehaviour)?.transform);

        return tmp;
    }

    private GameObject FindTotalsContainer(TextMeshProUGUI tmp, Transform playerRoot)
    {
        if (tmp == null) return null;

        Transform bestImageAny = null;
        Transform bestImageCircley = null;

        var t = tmp.transform.parent;
        while (t != null && t != playerRoot)
        {
            var img = t.GetComponent<Image>();
            if (img != null)
            {
                if (bestImageAny == null) bestImageAny = t;

                string nm = t.name.ToLowerInvariant();
                if (nm.Contains("circle") || nm.Contains("ring") || nm.Contains("badge") || nm.Contains("chip"))
                {
                    bestImageCircley = t;
                    break;
                }
            }
            t = t.parent;
        }

        var chosen = bestImageCircley ?? bestImageAny;
        return chosen != null ? chosen.gameObject : tmp.gameObject;
    }

}
