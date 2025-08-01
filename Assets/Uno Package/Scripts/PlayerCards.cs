using System.Collections.Generic;
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

        float space = 0;
        float start = 0;
        float totalWidth = GetComponent<RectTransform>().sizeDelta.x;
        int realCount = 0;
        for (int i = 0; i < cards.Count; i++) if (cards[i] != null) realCount++;
        if (realCount > 1)
        {
            space = (totalWidth - cardSize.x) / (realCount - 1);
            if (space > maxSpace)
            {
                space = maxSpace;
                totalWidth = (space * (realCount - 1)) + cardSize.x;
            }
            start = (totalWidth / -2) + cardSize.x / 2;
        }

        int idx = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            RectTransform item = cards[i].GetComponent<RectTransform>();
            item.SetSiblingIndex(i);
            item.anchorMax = Vector2.one * .5f;
            item.anchorMin = Vector2.one * .5f;
            item.pivot = Vector2.one * .5f;
            item.sizeDelta = cardSize;
            cards[i].SetTargetPosAndRot(new Vector3(start, 0f, 0f), 0f);
            start += space;
            idx++;
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
        // Ensure exactly 3 slots
        if (cards.Count < 3)
            for (int i = cards.Count; i < 3; i++)
                cards.Add(null);
        else if (cards.Count > 3)
            cards.RemoveRange(3, cards.Count - 3);
    }

}
