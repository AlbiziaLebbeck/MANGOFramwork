using TMPro;
using UnityEngine;

public class DebugCanvas : Singleton<DebugCanvas>
{
    [SerializeField] private static TMP_Text debugText;

    protected override void Awake()
    {
        base.Awake();

        debugText = transform.GetChild(0).GetComponent<TMP_Text>();
    }

    public static void SetText(string text)
    {
        if(debugText != null)
        {
            debugText.text = text;
        }
    }
}
