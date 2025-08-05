using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

[RequireComponent(typeof(Image))]
[ExecuteInEditMode]
public class Card : MonoBehaviour, IPointerClickHandler
{
    public bool _isOpen = true;
    public bool _isClickable;
    public CardType _type;
    public CardValue _value;
    [HideInInspector] public int localSeat;
    [HideInInspector] public int cardIndex;
    public bool PeekMode = false;
    public GameObject glowOutline;
    public GameObject killedOutline;
    public GameObject markedOutline;
    private Coroutine flashMarkedRoutine;
    public GameObject eyeOutline;
    public GameObject specialOutline;

    [Space(20)]
    public Text label1;
    public Text label2;
    public Text label3;
    public float moveSpeed = 0.3f;

    [HideInInspector]
    public int point;

    public CardType Type
    {
        get
        { return _type; }
        set
        {
            _type = value;
        }
    }
    public CardValue Value
    {
        get
        { return _value; }
        set
        {
            _value = value;
        }
    }
    public bool IsOpen
    {
        get
        { return _isOpen; }
        set
        {
            _isOpen = value;
            UpdateCard();
        }
    }

    public bool IsClickable
    {
        get
        {
            return _isClickable;
        }
        set
        {
            _isClickable = value;
        }
    }
    public Action<Card> onClick;

    public void SetTargetPosAndRot(Vector3 pos, float rotZ)
    {
        if (LeanTween.isTweening(gameObject))
            LeanTween.cancel(gameObject);
        float t = Vector2.Distance(transform.localPosition, pos) * moveSpeed / 1000f;
        LeanTween.moveLocal(gameObject, pos, t);
        LeanTween.rotateLocal(gameObject, new Vector3(0f, 0f, rotZ), t);
    }


    public void UpdateCard()
    {
        string txt = "";
        string spritePath = "Cards/BlankCard";

        // Special case: the only "0" card (gold card)
        if (Value == CardValue.Zero)
        {
            if (IsOpen)
            {
                spritePath = "Cards/gold card";
                txt = "0";
                label1.color = Color.white;
                label2.color = Color.white;
                label3.color = Color.white;
                label1.text = txt;
                label2.text = txt;
                label3.text = txt;

                if (specialOutline != null)
                    specialOutline.SetActive(true);
            }
            else
            {
                spritePath = "Cards/CardBack";
                label1.text = label2.text = label3.text = "";
                if (specialOutline != null)
                    specialOutline.SetActive(false);
            }

            // Always hide other outlines for zero
            if (glowOutline != null) glowOutline.SetActive(false);
            if (killedOutline != null) killedOutline.SetActive(false);
            if (markedOutline != null) markedOutline.SetActive(false);
            if (eyeOutline != null) eyeOutline.SetActive(false);
        }
        else // --- all other cards ---
        {
            if (IsOpen)
            {
                if (Value == CardValue.Skip)
                {
                    spritePath = $"Cards/Skip_{(int)Type + 1}";
                    txt = "";
                }
                else if (Value == CardValue.Jack)
                {
                    spritePath = $"Cards/Number_{(int)Type + 1}";
                    txt = "J";
                }
                else if (Value == CardValue.Queen)
                {
                    spritePath = $"Cards/Number_{(int)Type + 1}";
                    txt = "Q";
                }
                else if (Value == CardValue.King)
                {
                    spritePath = $"Cards/Number_{(int)Type + 1}";
                    txt = "K";
                }
                else if (Value == CardValue.Fiend)
                {
                    spritePath = $"Cards/Number_{(int)Type + 1}";
                    txt = "F";
                }
                else
                {
                    int value = (int)Value;
                    spritePath = $"Cards/Number_{(int)Type + 1}";
                    if (value == 6 || value == 9)
                        spritePath += "_Underline";
                    txt = value.ToString();
                }

                label1.color = Color.white;
                label2.color = Type.GetColor();
                label3.color = Color.white;
                label1.text = txt;
                label2.text = txt;
                label3.text = txt;
            }
            else
            {
                spritePath = "Cards/CardBack";
                label1.text = label2.text = label3.text = "";
            }

            // Zero's outline is only for zero card!
            if (specialOutline != null)
                specialOutline.SetActive(false);
        }

        Sprite loadedSprite = Resources.Load<Sprite>(spritePath);
        if (loadedSprite == null)
            loadedSprite = Resources.Load<Sprite>("Cards/BlankCard");
        GetComponent<Image>().sprite = loadedSprite;
    }


    public void CalcPoint()
    {
        if (Value == CardValue.King || Value == CardValue.Queen || Value == CardValue.Jack)
        {
            point = 20;
        }
        else
        {
            point = (int)Value;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[Card Click] {gameObject.name} - IsClickable: {IsClickable}");
        if (IsClickable && onClick != null)
        {
            onClick.Invoke(this);
        }
    }

    //public bool IsAllowCard()
    //{
    //    return Type == GamePlayManager.instance.CurrentType ||
    //        Value == GamePlayManager.instance.CurrentValue ||
    //        Type == CardType.Other;
    //}


    public void ShowGlow(bool show)
    {
        if (glowOutline != null)
            glowOutline.SetActive(show);
    }
    public void ShowKilledOutline(bool show)
    {
        if (killedOutline != null)
            killedOutline.SetActive(show);
    }

    public void FlashMarkedOutline(float duration = 2f, float pulseSpeed = 6f)
    {
        if (flashMarkedRoutine != null)
            StopCoroutine(flashMarkedRoutine);
        flashMarkedRoutine = StartCoroutine(DoFlashMarkedOutline(duration, pulseSpeed));
    }

    private IEnumerator DoFlashMarkedOutline(float duration, float pulseSpeed)
    {
        if (markedOutline == null)
            yield break;

        markedOutline.SetActive(true);

        // If your markedOutline is a UI Image, animate the alpha. Otherwise, just toggle on/off for a basic effect.
        Image outlineImg = markedOutline.GetComponent<Image>();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (outlineImg != null)
            {
                float alpha = Mathf.Abs(Mathf.Sin(elapsed * pulseSpeed)); // Pulses 0..1
                Color c = outlineImg.color;
                c.a = alpha;
                outlineImg.color = c;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Hide or reset
        if (outlineImg != null)
        {
            Color c = outlineImg.color;
            c.a = 0f;
            outlineImg.color = c;
        }
        markedOutline.SetActive(false);
        flashMarkedRoutine = null;
    }

    private Coroutine flashEyeRoutine;

    public void FlashEyeOutline(float duration = 2f, float pulseSpeed = 6f)
    {
        if (flashEyeRoutine != null)
            StopCoroutine(flashEyeRoutine);
        flashEyeRoutine = StartCoroutine(DoFlashEyeOutline(duration, pulseSpeed));
    }

    private IEnumerator DoFlashEyeOutline(float duration, float pulseSpeed)
    {
        if (eyeOutline == null)
            yield break;

        eyeOutline.SetActive(true);

        Image outlineImg = eyeOutline.GetComponent<Image>();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (outlineImg != null)
            {
                float alpha = Mathf.Abs(Mathf.Sin(elapsed * pulseSpeed));
                Color c = outlineImg.color;
                c.a = alpha;
                outlineImg.color = c;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (outlineImg != null)
        {
            Color c = outlineImg.color;
            c.a = 0f;
            outlineImg.color = c;
        }
        eyeOutline.SetActive(false);
        flashEyeRoutine = null;
    }

}