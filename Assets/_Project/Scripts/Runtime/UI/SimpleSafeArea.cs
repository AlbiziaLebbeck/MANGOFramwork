using UnityEngine;

public class SimpleSafeArea : MonoBehaviour
{
    RectTransform rectTransform;
    Rect safeArea;
    Vector2 minAnchor;
    Vector2 maxAnchor;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Refresh();
    }

    void Refresh()
    {
        if (Screen.width <= 0 || Screen.height <= 0) return;
        safeArea = Screen.safeArea;
        minAnchor = safeArea.position;
        maxAnchor = minAnchor + safeArea.size;

        minAnchor.x /= Screen.width;
        minAnchor.y /= Screen.height;
        maxAnchor.x /= Screen.width;
        maxAnchor.y /= Screen.height;

        rectTransform.anchorMin = minAnchor;
        rectTransform.anchorMax = maxAnchor;
    }
    void Update()
    {
        if (safeArea != Screen.safeArea || lastWidth != Screen.width || lastHeight != Screen.height)
        {
            lastWidth = Screen.width; lastHeight = Screen.height; Refresh();
        }
    }
    int lastWidth, lastHeight;
}
