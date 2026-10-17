using System.Collections;
using UnityEngine;

public class UIPopIn : MonoBehaviour
{
    public float scaleSpeed = 8f;

    private RectTransform rectTransform;
    private Coroutine scaleRoutine;
    private bool isOpen = false;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        //rectTransform.pivot = new Vector2(0f, 1f); // Top-left
        rectTransform.localScale = Vector3.zero;
    }

    public void TogglePanel()
    {
        if (isOpen)
            ClosePanel();
        else
            OpenPanel();
    }

    public void OpenPanel()
    {
        if (isOpen) return;

        isOpen = true;

        if (scaleRoutine != null)
            StopCoroutine(scaleRoutine);

        scaleRoutine = StartCoroutine(ScaleTo(Vector3.one));
    }

    public void ClosePanel()
    {
        if (!isOpen) return;

        isOpen = false;

        if (scaleRoutine != null)
            StopCoroutine(scaleRoutine);

        scaleRoutine = StartCoroutine(ScaleTo(Vector3.zero));
    }

    private IEnumerator ScaleTo(Vector3 target)
    {
        Vector3 start = rectTransform.localScale;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * scaleSpeed;
            rectTransform.localScale = Vector3.Lerp(start, target, t);
            yield return null;
        }

        rectTransform.localScale = target;
    }
}
