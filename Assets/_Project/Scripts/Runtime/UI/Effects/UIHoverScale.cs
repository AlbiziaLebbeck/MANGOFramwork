using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIHoverScale : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public RectTransform targetImage;
    public Vector3 minScale = Vector3.one;
    public Vector3 maxScale = new Vector3(1.1f, 1.1f, 1.1f);
    public float lerpSpeed = 8f;

    private Coroutine scaleRoutine;

    public void OnPointerClick(PointerEventData eventData)
    {
        LerpExit();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        LerpEnter();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        LerpExit();
    }

    private void LerpEnter()
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(ScaleTo(maxScale));
    }

    private IEnumerator ScaleTo(Vector3 lerpTarget)
    {
        Vector3 start = targetImage.localScale;
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * lerpSpeed;
            targetImage.localScale = Vector3.Lerp(start, lerpTarget, t);
            yield return null;
        }

        targetImage.localScale = lerpTarget;
    }

    private void LerpExit()
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(ScaleTo(minScale));
    }
}
