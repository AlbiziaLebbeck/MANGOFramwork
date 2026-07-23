using UnityEngine;

public sealed class PlayerTypeController : MonoBehaviour
{
    [Tooltip("Selects NetworkPlayer_Flyable for the next network spawn when enabled.")]
    public bool flyablePlayer;

    [Header("Cursor")]
    [SerializeField] private bool allowCursorToggle = true;

    public bool FlyablePlayer => flyablePlayer;

    private void Start()
    {
        // Flyable movement starts with a hidden cursor. Standard movement starts
        // with a visible cursor because it uses pointer-based controls.
        SetCursorUnlocked(!flyablePlayer);
    }

    private void Update()
    {
        if (allowCursorToggle && Input.GetKeyDown(KeyCode.E))
        {
            SetCursorUnlocked(!Cursor.visible);
        }
    }

    private static void SetCursorUnlocked(bool unlocked)
    {
        if (unlocked)
        {
            WebGlPointerLock.Release();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Browser pointer lock requires a user gesture. The WebGL bridge requests
        // it immediately when allowed or arms the next click/key press as a fallback.
        // Do not assign CursorLockMode.None here because Unity would release a lock
        // acquired by the authentication Continue button during scene loading.
        Cursor.visible = false;
        WebGlPointerLock.Acquire();
#else
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
#endif
    }
}
