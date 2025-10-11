using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Adjusts the UI RectTransform to fit within the screen's safe area.
/// Works for Mobile WebGL, Android, and iOS.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [Header("Extra Margin (in pixels)")]
    [Tooltip("Add extra padding beyond the safe area on each side.")]
    public Vector4 extraMargin; // Left, Top, Right, Bottom

    public static event Action<Rect> OnSafeAreaChanged;

    private RectTransform panel;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);
    private Vector2Int lastScreenSize = new Vector2Int(0, 0);
    private ScreenOrientation lastOriention;

    private void Awake()
    {
        panel = GetComponent<RectTransform>();
        ApplySafeArea();
        StartCoroutine(CheckSafeAreaRoutine());
    }

#if UNITY_EDITOR
    private void Update()
    {
        ApplySafeArea();
    }
#endif

    private IEnumerator CheckSafeAreaRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.25f);

            if(Screen.safeArea != lastSafeArea ||
                Screen.width != lastSafeArea.x ||
                Screen.height != lastSafeArea.y ||
                Screen.orientation != lastOriention)
            {
                ApplySafeArea();
            }
        }
    }

    private void ApplySafeArea()
    {
        if (panel == null)
            panel = GetComponent<RectTransform>();

        Rect safeArea = Screen.safeArea;

        safeArea.xMin -= extraMargin.x;
        safeArea.xMax += extraMargin.z;
        safeArea.yMin -= extraMargin.w;
        safeArea.yMax += extraMargin.y;

        safeArea.xMin = Mathf.Max(safeArea.xMin, 0);
        safeArea.yMin = Mathf.Max(safeArea.yMin, 0);
        safeArea.xMax = Mathf.Min(safeArea.xMax, Screen.width);
        safeArea.yMax = Mathf.Min(safeArea.yMax, Screen.height);

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

        lastSafeArea = safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastOriention = Screen.orientation;

        OnSafeAreaChanged?.Invoke(safeArea);
    }
}
