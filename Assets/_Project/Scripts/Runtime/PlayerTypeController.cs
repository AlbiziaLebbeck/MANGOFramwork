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
        SetCursorUnlocked(flyablePlayer);
    }

    private void Update()
    {
        if (allowCursorToggle && Input.GetKeyDown(KeyCode.E))
        {
            SetCursorUnlocked(Cursor.lockState == CursorLockMode.Locked);
        }
    }

    private static void SetCursorUnlocked(bool unlocked)
    {
        Cursor.lockState = unlocked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = unlocked;
    }
}
