using System.Collections;
using UnityEngine;
using TMPro;

public class TypingEffect : MonoBehaviour
{
    [SerializeField] private TMP_Text textMeshPro;
    [SerializeField] private float typingSpeed = 0.1f;
    [SerializeField] private string fullText;

    Coroutine typingCoroutine;

    private void OnEnable()
    {
        fullText = textMeshPro.text;
        textMeshPro.text = string.Empty;
        typingCoroutine = StartCoroutine(TypeText());
    }

    private void OnDisable()
    {
        StopCoroutine(typingCoroutine);
    }
    private IEnumerator TypeText()
    {
        foreach (char letter in fullText)
        {
            textMeshPro.text += letter; // Append each letter to the text
            yield return new WaitForSeconds(typingSpeed); // Wait for the specified duration
        }
    }
}