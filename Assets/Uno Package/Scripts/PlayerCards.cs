using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PlayerCards : MonoBehaviour
{
    public float maxSpace;
    public Vector2 cardSize;
    public List<Card> cards;

    public bool autoUpdatePositions = true;

    void Awake()
    {
        cards = new List<Card>(3);
        for (int i = 0; i < 3; i++)
            cards.Add(null);
    }

    public void UpdatePos(float delay = 0f)
    {
        if (!autoUpdatePositions) return;

        int slotCount = cards.Count; // always 3
        float totalWidth = GetComponent<RectTransform>().sizeDelta.x;
        float space = 0;
        float start = 0;

        if (slotCount > 1)
        {
            space = (totalWidth - cardSize.x) / (slotCount - 1);
            if (space > maxSpace)
            {
                space = maxSpace;
                totalWidth = (space * (slotCount - 1)) + cardSize.x;
            }
            start = (totalWidth / -2) + cardSize.x / 2;
        }

        for (int i = 0; i < slotCount; i++)
        {
            if (cards[i] == null) continue;
            RectTransform item = cards[i].GetComponent<RectTransform>();
            item.SetSiblingIndex(i);
            item.anchorMax = Vector2.one * .5f;
            item.anchorMin = Vector2.one * .5f;
            item.pivot = Vector2.one * .5f;
            item.sizeDelta = cardSize;
            cards[i].SetTargetPosAndRot(new Vector3(start + space * i, 0f, 0f), 0f);
        }
    }


    public void Clear()
    {
        // Destroy any card objects and set to null
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                Destroy(cards[i].gameObject);
            }
            cards[i] = null;
        }
        if (cards.Count < 3)
            for (int i = cards.Count; i < 3; i++)
                cards.Add(null);
        else if (cards.Count > 3)
            cards.RemoveRange(3, cards.Count - 3);
    }

}
