using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Player2 : MonoBehaviour
{
    public GameObject CardPanelBG;
    public PlayerCards cardsPanel;
    public string playerName;
    public int avatarIndex;
    public bool isUserPlayer;
    public Image avatarImage;
    public Text avatarName;
    public Text messageLbl;
    public ParticleSystem starParticleSystem;
    public Image timerImage;
    public GameObject timerOjbect;

    public float turnTimerDuration = 6f;
    private float totalTimer;

    [HideInInspector]
    public bool pickFromDeck, unoClicked, choosingColor;
    [HideInInspector]
    public bool isInRoom = true;
    public bool wasTimeout = false;
    [Header("Emoji Emotes")]
    public RectTransform emojiPanel;
    public RectTransform emojiDisplay;
    public RectTransform[] emojiButtons;
    public RectTransform[] emojiHoverVisuals;
    public Image[] emojiCooldownOverlays;
    public GameObject[] emojiPrefabs;
    public float emojiCooldownSeconds = 10f;

    private Canvas _rootCanvas;
    private bool _isHoldingAvatar = false;
    private int _hoverIndex = -1;
    private float _emojiCooldownRemaining = 0f;
    private Coroutine _emojiCooldownCo;
    private const float HoverScale = 1.3f;

    [Header("Emoji Panel FX")]
    public float emojiFadeDuration = 0.15f;
    public AnimationCurve emojiFadeCurve = null;
    public bool emojiPopScale = true;

    private CanvasGroup _emojiCg;
    private Coroutine _emojiFadeCo;
    private Vector3 _emojiBaseScale = Vector3.one;

    [Header("Emoji SFX")]
    public AudioClip[] emojiSfx;
    [Range(0f, 1f)] public float emojiSfxVolume = 0.80f;

    [Header("Spotlight/Trophy")]
    public GameObject TrophyBackground;

    public TextMeshProUGUI totalPointsTMP;

    public void SetAvatarProfile(AvatarProfile p)
    {
        playerName = p.avatarName;
        avatarIndex = p.avatarIndex;
        if (avatarName != null)
        {
            avatarName.text = p.avatarName;
            avatarName.GetComponent<EllipsisText>().UpdateText();
        }
        if (avatarImage != null)
            avatarImage.sprite = Resources.Load<Sprite>("Avatar/" + p.avatarIndex);
    }

    void Start()
    {
        SetTimerVisible(false);
        _rootCanvas = GetComponentInParent<Canvas>();
        WireAvatarHold();
        HideEmojiPanel();
        _emojiBaseScale = emojiPanel ? emojiPanel.localScale : Vector3.one;
        if (emojiFadeCurve == null) emojiFadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        UpdateCooldownOverlays();
        if (TrophyBackground) TrophyBackground.SetActive(false);
    }

    public void OnTurn()
    {
        unoClicked = false;
        pickFromDeck = false;
        SetTimerVisible(true);
        timerImage.fillAmount = 1f;
    }

    public void UpdateTurnTimerUI(float secondsLeft, float totalSeconds)
    {
        SetTimerVisible(true);
        timerImage.fillAmount = Mathf.Clamp01(secondsLeft / totalSeconds);
    }

    public void AddCard(Card c, int slot)
    {
        if (c == null)
        {
            // No card, just clear the slot and return
            if (cardsPanel.cards[slot] != null)
                Destroy(cardsPanel.cards[slot].gameObject);

            cardsPanel.cards[slot] = null;
            cardsPanel.UpdatePos();
            ResyncCardIndices();
            return;
        }

        c.transform.SetParent(cardsPanel.transform);
        if (cardsPanel.cards[slot] != null)
        {
            Destroy(cardsPanel.cards[slot].gameObject);
        }
        cardsPanel.cards[slot] = c;
        if (isUserPlayer)
        {
            c.onClick = OnCardClick;
            c.IsClickable = false;
        }
        cardsPanel.UpdatePos();
        ResyncCardIndices();
    }

    public void RemoveCard(Card c, bool updatePos = true)
    {
        int idx = cardsPanel.cards.IndexOf(c);
        if (idx >= 0)
        {
            c.onClick = null;
            c.IsClickable = false;
            Destroy(c.gameObject);
            cardsPanel.cards[idx] = null;
            if (updatePos)
                cardsPanel.UpdatePos();
            ResyncCardIndices();
        }
    }

    public void AddSerializableCard(SerializableCard sc, int slot)
    {
        Card card = Instantiate(GamePlayManager.instance._cardPrefab, cardsPanel.transform);
        card.Type = sc.Type;
        card.Value = sc.Value;
        card.IsOpen = false;
        card.CalcPoint();
        card.name = $"{sc.Type}_{sc.Value}";

        if (cardsPanel.cards[slot] != null)
            Destroy(cardsPanel.cards[slot].gameObject);
        cardsPanel.cards[slot] = card;

        if (isUserPlayer)
        {
            card.onClick = OnCardClick;
            card.IsClickable = false;
        }

        cardsPanel.UpdatePos();
        ResyncCardIndices();
    }

    public void ResyncCardIndices()
    {
        if (GamePlayManager.instance == null || GamePlayManager.instance.players == null)
            return;
        int localSeat = GamePlayManager.instance.players.IndexOf(this);
        for (int i = 0; i < cardsPanel.cards.Count; i++)
        {
            var card = cardsPanel.cards[i];
            if (card == null) continue;
            card.cardIndex = i;
            card.localSeat = localSeat;
        }

    }

    public void OnCardClick(Card c)
    {
        if (timerOjbect.activeInHierarchy)
        {
            GamePlayManager.instance.PutCardToWastePile(c, this);
            OnTurnEnd();
        }
    }

    public void OnTurnEnd()
    {
        SetTimerVisible(false);
        cardsPanel.UpdatePos();

        foreach (var card in cardsPanel.cards)
        {
            if (card == null) continue;
            card.IsClickable = false;
            card.onClick = null;
        }

        GamePlayManager.instance.arrowObject.SetActive(false);
        GamePlayManager.instance.unoBtn.SetActive(false);
    }

    public void ShowMessage(string message, bool playStarParticle = false, float duration = 1.5f)
    {
        StopCoroutine("MessageHideRoutine");
        messageLbl.text = message;
        messageLbl.GetComponent<Animator>().SetTrigger("show");
        if (playStarParticle)
        {
            starParticleSystem.gameObject.SetActive(true);
            starParticleSystem.Emit(30);
        }
        StartCoroutine(MessageHideRoutine(duration));
    }

    private IEnumerator MessageHideRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        messageLbl.GetComponent<Animator>().SetTrigger("hide");
    }

    public int GetTotalPoints()
    {
        int points = 0;
        foreach (var c in cardsPanel.cards)
        {
            if (c == null) continue;

            int basePts = (c.Value >= CardValue.Zero && c.Value <= CardValue.Ten)
                            ? (int)c.Value
                            : 10;

            bool doubled = c.IsCursed && c.Value != CardValue.Nemesis; // Nemesis never doubles
            points += doubled ? (basePts * 2) : basePts;
        }
        return points;
    }

    public void SetTimerVisible(bool visible)
    {
        if (timerOjbect != null)
            timerOjbect.SetActive(visible);
        if (timerImage != null)
            timerImage.gameObject.SetActive(visible);
    }

    public void SpawnEmojiLocal(int emojiIndex)
    {
        if (emojiDisplay == null || emojiPrefabs == null) return;
        if (emojiIndex < 0 || emojiIndex >= emojiPrefabs.Length) return;

        var go = Instantiate(emojiPrefabs[emojiIndex], emojiDisplay);
        var rt = go.transform as RectTransform;
        if (rt != null)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
        }
        else
        {
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        Destroy(go, 3f);
    }

    private void WireAvatarHold()
    {
        if (avatarImage == null) return;

        var trig = avatarImage.gameObject.GetComponent<EventTrigger>();
        if (trig == null) trig = avatarImage.gameObject.AddComponent<EventTrigger>();

        AddTrigger(trig, EventTriggerType.PointerDown, OnAvatarPointerDown);
        AddTrigger(trig, EventTriggerType.Drag, OnAvatarDrag);
        AddTrigger(trig, EventTriggerType.PointerUp, OnAvatarPointerUp);
        AddTrigger(trig, EventTriggerType.PointerExit, OnAvatarPointerExit);
    }

    private void OnAvatarPointerExit(BaseEventData bed)
    {
        if (_isHoldingAvatar) return;

        HideEmojiPanel();
    }

    private static void AddTrigger(EventTrigger trig, EventTriggerType type, System.Action<BaseEventData> cb)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(cb.Invoke);
        trig.triggers.Add(e);
    }

    private void OnAvatarPointerDown(BaseEventData bed)
    {
        _isHoldingAvatar = true;
        ShowEmojiPanel();
        UpdateCooldownOverlays();
        _hoverIndex = -1;
        HighlightHover(-1);
    }

    private void OnAvatarDrag(BaseEventData bed)
    {
        if (!_isHoldingAvatar || emojiPanel == null) return;
        var ped = (PointerEventData)bed;
        int idx = GetEmojiIndexUnderPointer(ped.position);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            HighlightHover(_hoverIndex);
        }
    }

    private void OnAvatarPointerUp(BaseEventData bed)
    {
        if (!_isHoldingAvatar) return;
        _isHoldingAvatar = false;

        int chosen = _hoverIndex;
        HideEmojiPanel();

        // Respect cooldown
        if (_emojiCooldownRemaining > 0f) return;

        if (chosen >= 0 && chosen < (emojiPrefabs != null ? emojiPrefabs.Length : 0))
        {
            var gpm = GamePlayManager.instance;
            if (gpm != null)
            {
                int localSeat = gpm.players.IndexOf(this);
                int globalSeat = gpm.GetGlobalIndexFromLocal(localSeat);

                if (gpm.IsServer) gpm.PlayEmojiClientRpc(globalSeat, chosen);
                else gpm.RequestPlayEmojiServerRpc(globalSeat, chosen);
            }

            StartEmojiCooldown();
        }
    }

    private void ShowEmojiPanel()
    {
        if (!emojiPanel) return;

        var cg = EnsureEmojiCanvasGroup();
        if (_emojiFadeCo != null) StopCoroutine(_emojiFadeCo);

        emojiPanel.gameObject.SetActive(true);

        float fromA = cg.alpha;
        float toA = 1f;

        cg.blocksRaycasts = true;
        cg.interactable = true;

        Vector3 fromS = emojiPanel.localScale;
        Vector3 toS = emojiPopScale ? _emojiBaseScale : fromS;
        if (emojiPopScale && fromS == _emojiBaseScale) fromS = _emojiBaseScale * 0.96f; // subtle pop-in

        _emojiFadeCo = StartCoroutine(FadeEmojiPanel(fromA, toA, fromS, toS, true));
    }

    private void HideEmojiPanel()
    {
        if (!emojiPanel) return;

        var cg = EnsureEmojiCanvasGroup();
        if (_emojiFadeCo != null) StopCoroutine(_emojiFadeCo);

        float fromA = cg.alpha;
        float toA = 0f;

        cg.blocksRaycasts = false;
        cg.interactable = false;

        Vector3 fromS = emojiPanel.localScale;
        Vector3 toS = emojiPopScale ? _emojiBaseScale * 0.96f : fromS;

        _emojiFadeCo = StartCoroutine(FadeEmojiPanel(fromA, toA, fromS, toS, false));
    }

    private IEnumerator FadeEmojiPanel(float fromA, float toA, Vector3 fromS, Vector3 toS, bool showing)
    {
        var cg = EnsureEmojiCanvasGroup();
        float d = Mathf.Max(0.0001f, emojiFadeDuration);
        float t = 0f;

        if (showing) { cg.alpha = fromA; }

        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            float k = emojiFadeCurve != null ? emojiFadeCurve.Evaluate(t / d) : (t / d);

            cg.alpha = Mathf.Lerp(fromA, toA, k);
            if (emojiPopScale && emojiPanel)
                emojiPanel.localScale = Vector3.Lerp(fromS, toS, k);

            yield return null;
        }

        cg.alpha = toA;
        if (!showing)
        {
            // Reset and hide at the end
            if (emojiPopScale && emojiPanel) emojiPanel.localScale = _emojiBaseScale;
            emojiPanel.gameObject.SetActive(false);
        }
        _emojiFadeCo = null;
    }


    private void HighlightHover(int idx)
    {
        for (int i = 0; i < (emojiButtons?.Length ?? 0); i++)
        {
            if (emojiButtons[i] == null) continue;

            emojiButtons[i].localScale = Vector3.one;

            if (emojiHoverVisuals != null && i < emojiHoverVisuals.Length && emojiHoverVisuals[i] != null)
                emojiHoverVisuals[i].localScale = (i == idx) ? Vector3.one * HoverScale : Vector3.one;
        }
    }

    private int GetEmojiIndexUnderPointer(Vector2 screenPos)
    {
        if (emojiButtons == null || _rootCanvas == null) return -1;
        Camera uiCam = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _rootCanvas.worldCamera;

        for (int i = 0; i < emojiButtons.Length; i++)
        {
            var r = emojiButtons[i];
            if (r == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(r, screenPos, uiCam))
                return i;
        }
        return -1;
    }

    private void StartEmojiCooldown()
    {
        _emojiCooldownRemaining = emojiCooldownSeconds;
        if (_emojiCooldownCo != null) StopCoroutine(_emojiCooldownCo);
        _emojiCooldownCo = StartCoroutine(EmojiCooldownRoutine());
    }

    private IEnumerator EmojiCooldownRoutine()
    {
        while (_emojiCooldownRemaining > 0f)
        {
            _emojiCooldownRemaining -= Time.deltaTime;
            UpdateCooldownOverlays();
            yield return null;
        }
        _emojiCooldownRemaining = 0f;
        UpdateCooldownOverlays();
    }

    private void UpdateCooldownOverlays()
    {
        float fill = (emojiCooldownSeconds > 0f)
            ? Mathf.Clamp01(_emojiCooldownRemaining / emojiCooldownSeconds)
            : 0f;

        if (emojiCooldownOverlays != null)
        {
            for (int i = 0; i < emojiCooldownOverlays.Length; i++)
            {
                var img = emojiCooldownOverlays[i];
                if (img == null) continue;

                img.fillAmount = fill;
                img.gameObject.SetActive(fill > 0f);
            }
        }
    }

    private CanvasGroup EnsureEmojiCanvasGroup()
    {
        if (!_emojiCg && emojiPanel)
        {
            _emojiCg = emojiPanel.GetComponent<CanvasGroup>();
            if (!_emojiCg) _emojiCg = emojiPanel.gameObject.AddComponent<CanvasGroup>();
        }
        return _emojiCg;
    }

}
