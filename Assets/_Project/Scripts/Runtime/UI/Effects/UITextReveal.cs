using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class UITextReveal : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public TMP_Text targetText;
    public Vector2 fullReachWidth = Vector2.one;
    public float lerpSpeed = 8f;

    private Coroutine revealRoutine;

    private void Start()
    {
        LerpExit();
    }

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

    private IEnumerator DoSlide(Vector2 lerpTarget)
    {
        Vector2 start = targetText.rectTransform.sizeDelta;
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * lerpSpeed;
            targetText.rectTransform.sizeDelta = Vector2.Lerp(start, lerpTarget, t);
            yield return null;
        }

        targetText.rectTransform.sizeDelta = lerpTarget;
    }

    private void LerpEnter()
    {
        if (revealRoutine != null) StopCoroutine(revealRoutine);
        revealRoutine = StartCoroutine(DoSlide(fullReachWidth));
    }

    private void LerpExit()
    {
        if (revealRoutine != null) StopCoroutine(revealRoutine);
        revealRoutine = StartCoroutine(DoSlide(Vector2.zero));
    } 
}
