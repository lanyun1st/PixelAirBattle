using UnityEngine;

// Shared by every menu button; visual movement never changes its input rectangle.
public static class ButtonFeedback
{
    public static bool Handle(Rect rect, out bool pressed)
    {
        int id = GUIUtility.GetControlID(0x42B17, FocusType.Passive, rect);
        var ev = Event.current;
        bool inside = rect.Contains(ev.mousePosition);
        bool clicked = false;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (GUI.enabled && ev.button == 0 && inside && GUIUtility.hotControl == 0)
                { GUIUtility.hotControl = id; ev.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) ev.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id && ev.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    clicked = GUI.enabled && inside;
                    ev.Use();
                }
                break;
            case EventType.Ignore:
                if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                break;
        }
        pressed = GUI.enabled && GUIUtility.hotControl == id && inside;
        return clicked;
    }

    public static Rect VisualRect(Rect rect, bool pressed)
    {
        return pressed ? new Rect(rect.x + 2, rect.y + 3, rect.width - 4, rect.height - 4) : rect;
    }

    public static Color Tint(Color color, bool pressed)
    {
        return pressed ? color * new Color(.76f, .76f, .76f, 1f) : color;
    }
}
