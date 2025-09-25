using UnityEngine;

public class PersistentCanvas : Singleton<PersistentCanvas>
{
    [SerializeField] private LoadingCanvas loadingCanvas;
    [SerializeField] private UserDataCanvas userDataCanvas;
    [SerializeField] private ChatCanvas chatCanvas;
    [SerializeField] private ShareScreenCanvas shareScreenCanvas;
    [SerializeField] private InteractionCanvas interactionCanvas;
    [SerializeField] private LocomotionManagerCanvas locomotionManagerCanvas;

    public static LoadingCanvas LoadingCanvas { get; private set; }
    public static UserDataCanvas UserDataCanvas { get; private set; }
    public static ChatCanvas ChatCanvas { get; private set; }
    public static ShareScreenCanvas ShareScreenCanvas { get; private set; }
    public static InteractionCanvas InteractionCanvas { get; private set; }
    public static LocomotionManagerCanvas LocomotionManagerCanvas { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        LoadingCanvas = this.loadingCanvas;
        if(!loadingCanvas.gameObject.activeInHierarchy) loadingCanvas.gameObject.SetActive(true);
        UserDataCanvas = this.userDataCanvas;
        ChatCanvas = this.chatCanvas;
        ShareScreenCanvas = this.shareScreenCanvas;
        InteractionCanvas = this.interactionCanvas;
        LocomotionManagerCanvas = this.locomotionManagerCanvas;
    }
}
