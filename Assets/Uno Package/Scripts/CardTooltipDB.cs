using System.Collections.Generic;
using System.Text;

/// <summary>
/// Centralized, formatted tooltip builder for cards.
/// Returns TMP-rich-text strings (bullets, bold, colored section labels).
/// </summary>
public static class CardTooltipDB
{
    public struct Sections
    {
        public string Title;
        public string Activate;
        public string Passive;
        public string Bonus;
        public string Revenge;
    }

    private static readonly Dictionary<CardValue, Sections> _data =
        new Dictionary<CardValue, Sections>
    {
        { CardValue.GoldenJack, new Sections {
            Title   = "Golden Jack",
            Activate= "Reveal a player's hand cards.",
            Bonus   = "Exposes the player with a Zero card.",
            Revenge = "When killed, exposes the killer's cards to everyone. Can be picked up again."
        }},
        { CardValue.Fiend, new Sections {
            Title   = "Fiend",
            Activate= "Jumble a player's hand.",
            Revenge = "When killed, jumbles the killer's cards."
        }},
        { CardValue.Jack, new Sections {
            Title   = "Jack",
            Activate= "Reveal a card.",
            Bonus   = "Exposes the player with a Zero card."
        }},
        { CardValue.Queen, new Sections {
            Title   = "Queen",
            Activate= "Swap positions of two cards."
        }},
        { CardValue.King, new Sections {
            Title   = "King",
            Activate= "Kill a card."
        }},
        { CardValue.Scout, new Sections {
            Title   = "Scout",
            Activate= "Reveal total card points of everyone.",
            Passive = "Keep in hand to passively siphon points info."
        }},
        { CardValue.Nemesis, new Sections {
            Title   = "Nemesis",
            Activate= "Curse a card; double its value permanently."
        }},
        { CardValue.Skip, new Sections {
            Title   = "Skip",
            Activate= "Skip the next player's turn."
        }},
        { CardValue.Zero, new Sections {
            Title   = "Zero",
            Passive = "Lowest card. Keep it and protect it at all costs!"
        }},
    };

    public static bool TryBuild(CardValue value, out string richText)
    {
        Sections s;
        if (!_data.TryGetValue(value, out s))
        {
            richText = null;
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<size=120%><b>" + s.Title + "</b></size>");
        if (!string.IsNullOrEmpty(s.Activate)) sb.AppendLine("• <b>Activate:</b> " + s.Activate);
        if (!string.IsNullOrEmpty(s.Passive)) sb.AppendLine("• <color=#22B2FF><b>Passive:</b></color> " + s.Passive);
        if (!string.IsNullOrEmpty(s.Bonus)) sb.AppendLine("• <color=#FFD24D><b>Bonus:</b></color> " + s.Bonus);
        if (!string.IsNullOrEmpty(s.Revenge)) sb.AppendLine("• <color=#FF6B6B><b>Revenge:</b></color> " + s.Revenge);

        richText = sb.ToString();
        return true;
    }
}
