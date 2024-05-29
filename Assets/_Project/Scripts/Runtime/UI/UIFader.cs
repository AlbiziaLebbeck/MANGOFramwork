using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(CanvasGroup))]
public class UIFader : MonoBehaviour
{
    [SerializeField] private float lerpTime = 0.5f;
    private CanvasGroup canvasGroup;

    private void Awake() => canvasGroup = GetComponent<CanvasGroup>();

    public void FadeIn() => StartCoroutine(DoFadeCoroutine(canvasGroup.alpha, 1));
    public void FadeIn(CanvasGroup _cg, Action _callback) => StartCoroutine(DoFadeWithTargetCoroutine(_cg, _cg.alpha, 1, _callback));
    public void FadeOut() => StartCoroutine(DoFadeCoroutine(canvasGroup.alpha, 0));
    public void FadeOut(CanvasGroup _cg, Action _callback) => StartCoroutine(DoFadeWithTargetCoroutine(_cg, _cg.alpha, 0, _callback));

    private IEnumerator DoFadeCoroutine(float _start, float _end)
    {
        float _timeStartedLerping = Time.time;
        float timeSinceStarted = Time.time - _timeStartedLerping;
        float percentageComplete = timeSinceStarted / lerpTime;

        while (true)
        {
            timeSinceStarted = Time.time - _timeStartedLerping;
            percentageComplete = timeSinceStarted / lerpTime;

            float currentValue = Mathf.Lerp(_start, _end, percentageComplete);

            canvasGroup.alpha = currentValue;

            if (percentageComplete >= 1) break;

            yield return new WaitForEndOfFrame();
        }
    }

    private IEnumerator DoFadeWithTargetCoroutine(CanvasGroup target, float _start, float _end, Action _callback)
    {
        float _timeStartedLerping = Time.time;
        float timeSinceStarted = Time.time - _timeStartedLerping;
        float percentageComplete = timeSinceStarted / lerpTime;

        while (true)
        {
            timeSinceStarted = Time.time - _timeStartedLerping;
            percentageComplete = timeSinceStarted / lerpTime;

            float currentValue = Mathf.Lerp(_start, _end, percentageComplete);

            target.alpha = currentValue;

            if (percentageComplete >= 1)
            {
                if (_callback != null) _callback();
                break;
            }

            yield return new WaitForEndOfFrame();
        }

    }
}
