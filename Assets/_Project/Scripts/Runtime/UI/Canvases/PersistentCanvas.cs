using UnityEngine;

public class PersistentCanvas : Singleton<PersistentCanvas>
{
    [SerializeField] private LoadingCanvas loadingCanvas;
    [SerializeField] private UserDataCanvas userDataCanvas;
    [SerializeField] private ChatCanvas chatCanvas;
    [SerializeField] private InteractionCanvas interactionCanvas;

    public static LoadingCanvas LoadingCanvas { get; private set; }
    public static UserDataCanvas UserDataCanvas { get; private set; }
    public static ChatCanvas ChatCanvas { get; private set; }
    public static InteractionCanvas InteractionCanvas { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        LoadingCanvas = this.loadingCanvas;
        if(!loadingCanvas.gameObject.activeInHierarchy) loadingCanvas.gameObject.SetActive(true);
        UserDataCanvas = this.userDataCanvas;
        ChatCanvas = this.chatCanvas;
        InteractionCanvas = this.interactionCanvas;
    }
}
