using UnityEngine;

public class PersistentCanvas : Singleton<PersistentCanvas>
{
    [SerializeField] private LoadingCanvas loadingCanvas;
    [SerializeField] private UserDataCanvas userDataCanvas;
    [SerializeField] private ChatCanvas chatCanvas;
    public static LoadingCanvas LoadingCanvas { get; private set; }
    public static UserDataCanvas UserDataCanvas { get; private set; }
    public static ChatCanvas ChatCanvas { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        LoadingCanvas = this.loadingCanvas;
        UserDataCanvas = this.userDataCanvas;
        ChatCanvas = this.chatCanvas;
    }
}
