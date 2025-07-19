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
            else
            {
                int value = (int)Value;
                spritePath = $"Cards/Number_{(int)Type + 1}";
                if (value == 6 || value == 9)
                    spritePath += "_Underline";
                txt = value.ToString();
            }
        }

        Sprite loadedSprite = Resources.Load<Sprite>(spritePath);
        if (loadedSprite == null)
            loadedSprite = Resources.Load<Sprite>("Cards/BlankCard");
        GetComponent<Image>().sprite = loadedSprite;

        label1.color = Color.white;
        label2.color = Type.GetColor();
        label3.color = Color.white;

        label1.text = txt;
        label2.text = txt;
        label3.text = txt;
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
}