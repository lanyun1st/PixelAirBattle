using System.Globalization;
using UnityEngine;

public static class SpriteNumbers
{
    private static readonly Texture2D[] digits = new Texture2D[10];
    private static Texture2D dot;
    private static bool loaded;

    public static bool Load()
    {
        if (!loaded)
        {
            for (int i = 0; i < 10; i++)
            {
                digits[i] = Resources.Load<Texture2D>("Art/digit" + i);
                if (digits[i] != null) digits[i].filterMode = FilterMode.Point;
            }
            dot = Resources.Load<Texture2D>("Art/digit_dot");
            if (dot != null) dot.filterMode = FilterMode.Point;
            loaded = true;
        }
        foreach (var digit in digits) if (digit == null) return false;
        return dot != null;
    }

    public static string Countdown(float seconds, int wholeDigits)
    {
        int tenths = Mathf.CeilToInt(Mathf.Max(0f, seconds) * 10f);
        return (tenths / 10).ToString("D" + wholeDigits, CultureInfo.InvariantCulture) + "." + tenths % 10;
    }

    public static void Draw(Rect rect, string text)
    {
        if (!Load() || string.IsNullOrEmpty(text)) return;
        float width = Mathf.Min(rect.width / text.Length, rect.height);
        float start = rect.x + (rect.width - width * text.Length) * .5f;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            var art = c == '.' ? dot : c >= '0' && c <= '9' ? digits[c - '0'] : null;
            if (art != null) GUI.DrawTexture(new Rect(start + i * width, rect.y, width, rect.height), art, ScaleMode.ScaleToFit);
        }
    }

    public static void Score(Rect rect, int value)
    {
        Draw(rect, Mathf.Max(0, value).ToString("D6", CultureInfo.InvariantCulture));
    }

    public static void Performance(Rect rect, float frameSeconds, GUIStyle labelStyle)
    {
        Draw(new Rect(rect.x, rect.y, 64, 20), (1f / Mathf.Max(.0001f, frameSeconds)).ToString("0", CultureInfo.InvariantCulture));
        GUI.Label(new Rect(rect.x + 64, rect.y, 48, 24), "FPS", labelStyle);
        Draw(new Rect(rect.x + 120, rect.y, 90, 20), (frameSeconds * 1000f).ToString("0.0", CultureInfo.InvariantCulture));
        GUI.Label(new Rect(rect.x + 210, rect.y, 42, 24), "ms", labelStyle);
    }
}
