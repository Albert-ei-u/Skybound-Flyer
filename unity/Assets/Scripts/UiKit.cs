using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Small helpers for building uGUI screens from code.</summary>
public static class UiKit
{
    public static readonly Color Accent = new Color(0.35f, 1f, 0.75f);
    public static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    public static readonly Color Money = new Color(0.45f, 0.9f, 0.4f);
    public static readonly Color PanelColor = new Color(0.02f, 0.05f, 0.08f, 0.6f);

    private static Font font;
    private static Sprite circle, white;

    public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    public static Canvas CreateCanvas(Transform parent, string name, int order)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
        return canvas;
    }

    public static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    /// <summary>A panel that stretches over its whole parent.</summary>
    public static RectTransform Fill(Transform parent, string name, Color color)
    {
        RectTransform rect = Panel(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, color);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.GetComponent<Image>().raycastTarget = true; // blocks clicks to things behind
        return rect;
    }

    public static Text Label(Transform parent, string text, int size, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 box)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = box;
        var label = go.GetComponent<Text>();
        label.font = Font;
        label.text = text;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        label.supportRichText = true;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        go.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        return label;
    }

    public static Button Button(Transform parent, string text, Vector2 anchor, Vector2 position, Vector2 size,
                                Color color, System.Action onClick)
    {
        RectTransform rect = Panel(parent, "Button", anchor, position, size, color);
        rect.GetComponent<Image>().raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
        button.colors = colors;
        if (onClick != null) button.onClick.AddListener(() => onClick());
        Label(rect, text, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size).fontStyle = FontStyle.Bold;
        return button;
    }

    public static void Outline(RectTransform rect, Color color)
    {
        var outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    public static Sprite Circle()
    {
        if (circle != null) return circle;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size / 2f - d)));
        }
        tex.Apply();
        circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return circle;
    }

    public static Sprite White()
    {
        if (white != null) return white;
        white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return white;
    }
}
