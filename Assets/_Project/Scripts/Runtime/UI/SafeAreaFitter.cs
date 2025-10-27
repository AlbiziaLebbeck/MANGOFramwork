using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [SerializeField] private CanvasScaler canvasScaler;
    [SerializeField] private Vector2 additionalMargin = Vector2.zero;

    private RectTransform panel;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);
    private Vector2Int lastScreenSize = new Vector2Int(0, 0);
    private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

    void Awake()
    {
        panel = GetComponent<RectTransform>();
        if (!canvasScaler)
            canvasScaler = GetComponentInParent<CanvasScaler>();

        ApplySafeArea();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafeArea
            || Screen.width != lastScreenSize.x
            || Screen.height != lastScreenSize.y
            || Screen.orientation != lastOrientation)
        {
            ApplySafeArea();
        }
    }

    private void ApplySafeArea()
    {
        Rect safeArea = Screen.safeArea;

        // Apply additional margins in *screen pixels*
        safeArea.xMin += additionalMargin.x * Screen.width / canvasScaler.referenceResolution.x;
        safeArea.xMax -= additionalMargin.x * Screen.width / canvasScaler.referenceResolution.x;
        safeArea.yMin += additionalMargin.y * Screen.height / canvasScaler.referenceResolution.y;
        safeArea.yMax -= additionalMargin.y * Screen.height / canvasScaler.referenceResolution.y;

        // Convert safe area rectangle to normalized anchor values
        Vector2 anchorMin = safeArea.position;
        Vector2 anchorMax = safeArea.position + safeArea.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        panel.anchorMin = anchorMin;
        panel.anchorMax = anchorMax;
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        lastSafeArea = Screen.safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastOrientation = Screen.orientation;
    }
}
