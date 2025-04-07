using Cinemachine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using StarterAssets;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstagramArea : NetworkBehaviour
{
    [SerializeField] private Vector2 spawnSize = Vector2.zero;
    [SerializeField] private Transform lockTarget;

    #region Draw Visualize Box
#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private Color gizmoColor = Color.white;
    [SerializeField] private float boxHeight = 0.5f;
    [SerializeField] private bool debug;

    private void OnDrawGizmosSelected()
    {
        if (!debug) return;

        Gizmos.color = gizmoColor;

        // Save the original Gizmos matrix
        Matrix4x4 originalMatrix = Gizmos.matrix;

        // Apply transformation matrix with rotation
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        // Draw the gizmo cube at the center with the given size
        var size = new Vector3(spawnSize.x, boxHeight, spawnSize.y);
        Gizmos.DrawCube(Vector3.zero, size);

        // Restore the original Gizmos matrix
        Gizmos.matrix = originalMatrix;
    }
#endif
    #endregion

    public List<CinemachineVirtualCamera> cameraList = new();

    private int currentCameraIndex;
    private CinemachineVirtualCamera currentCamera;

    public GameObject cameraButton;
    public GameObject cameraListButton;
    public GameObject poseListButton;
    public GameObject panel;

    #region Cache Local
    private GameObject playerRig;
    private NetworkObject localPlayer;
    private CharacterController localPlayer_CharacterController;
    private ThirdPersonController localPlayer_ThirdPersonController;
    private PlayerMovementHandler localPlayer_PlayerMovementHandler;
    private Animator localPlayer_Animator;
    #endregion

    public List<AnimationClip> newClips = new();
    private AnimationClip currentAnimationClip;
    public string targetStateNameToChange;
    
    private string modifiedStateName;
    private AnimationClip originalClip;
    private AnimatorOverrideController overrideController;

    private readonly SyncDictionary<NetworkObject, int> SelectAnim = new SyncDictionary<NetworkObject, int>();
    private readonly SyncList<NetworkObject> currentPlayerPoseInArea = new SyncList<NetworkObject>();

    public override void OnStartServer()
    {
        if (base.IsServerInitialized)
        {
            if (WorldManager.Instance != null)
            {
                WorldManager.Instance.OnClientLeftWorld += WorldManager_OnClientLeftWorld;
            }
        }
    }

    public override void OnStopServer()
    {
        if (base.IsServerInitialized)
        {
            if (WorldManager.Instance != null)
            {
                WorldManager.Instance.OnClientLeftWorld -= WorldManager_OnClientLeftWorld;
            }
        }
    }

    private void WorldManager_OnClientLeftWorld(WorldDetails worldDetails, NetworkObject leftPlayer)
    {
        var lefter = currentPlayerPoseInArea.Find(x => x.Owner == leftPlayer.Owner);
        Debug.Log("Remove left player from currentPlayerPoseInArea");

        if (lefter != null)
        {
            SelectAnim.Remove(lefter);
            Debug.Log("Remove left player from SelectAnim");
        }
    }

    public override void OnStartClient()
    {
        foreach (NetworkObject player in currentPlayerPoseInArea)
        {
            if (player.TryGetComponent(out Animator animator))
            {
                DoOverride(SelectAnim[player], animator);
            }
        }
    }

    public void OnClick_PrevCamera()
    {
        if(currentCamera != null)
        {
            currentCamera.gameObject.SetActive(false);
        }

        currentCameraIndex -= 1;

        currentCameraIndex = currentCameraIndex < 0 ? cameraList.Count - 1 : currentCameraIndex;

        currentCamera = cameraList[currentCameraIndex];

        currentCamera.gameObject.SetActive(true);
    }

    public void OnClick_NextCamera()
    {
        if (currentCamera != null)
        {
            currentCamera.gameObject.SetActive(false);
        }

        currentCameraIndex += 1;

        currentCameraIndex = currentCameraIndex >= cameraList.Count ? 0 : currentCameraIndex;

        currentCamera = cameraList[currentCameraIndex];

        currentCamera.gameObject.SetActive(true);
    }

    public void OnClick_Cancel()
    {
        if (currentCamera != null)
        {
            currentCamera.gameObject.SetActive(false);
        }

        panel.SetActive(false);
        cameraButton.SetActive(true);
        cameraListButton.SetActive(false);
        poseListButton.SetActive(false);

        TogglePoseMode(false);
    }

    public void OnClick_OpenCamera()
    {
        cameraButton.SetActive(false);
        cameraListButton.SetActive(true);
        poseListButton.SetActive(true);

        TogglePoseMode(true);


    }

    public void OnClick_SelectPose(int poseIndex)
    {
        if(localPlayer != null)
        {
            RPCRequestPose(localPlayer, poseIndex);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RPCRequestPose(NetworkObject player, int poseIndex)
    {
        if(poseIndex != -1)
        {
            currentPlayerPoseInArea.Add(player);
            SelectAnim.Add(player, poseIndex);

            ObserverOverrideAnimation(player, poseIndex, true);
        }
        else
        {
            currentPlayerPoseInArea.Remove(player);
            SelectAnim.Remove(player);
            ObserverOverrideAnimation(player, poseIndex, false);
        }

        
    }

    [ObserversRpc]
    private void ObserverOverrideAnimation(NetworkObject player,int poseIndex, bool isOverride)
    {
        if (isOverride)
        {
            if (player.TryGetComponent(out Animator animator))
            {
                
                DoOverride(poseIndex, animator);
            }
        }
        else
        {
            DoReset();
        }
    }

    private void TogglePoseMode(bool inPoseMode)
    {
        if(playerRig == null)
        {
            playerRig = UserReferencePersistent.Instance.PlayerGameObject;

            playerRig.TryGetComponent(out localPlayer);

            if (playerRig != null)
            {
                localPlayer_CharacterController = playerRig.GetComponent<CharacterController>();
                localPlayer_ThirdPersonController = playerRig.GetComponent<ThirdPersonController>();
                localPlayer_PlayerMovementHandler = playerRig.GetComponent<PlayerMovementHandler>();
                localPlayer_Animator = playerRig.GetComponent<Animator>();
            }
        }

        if (playerRig == null) return;

        lockTarget.position = playerRig.transform.position;

        localPlayer_CharacterController.enabled = !inPoseMode;
        localPlayer_ThirdPersonController.enabled = !inPoseMode;
        localPlayer_PlayerMovementHandler.SetLockToPlatformTarget(inPoseMode ? lockTarget : null);
        localPlayer_Animator.SetInteger("Motion", inPoseMode ? 1 : 0);

        if (localPlayer != null)
        {
            RPCRequestPose(localPlayer, inPoseMode? 0 : -1);
        }
    }

    private void DoOverride(int poseIndex, Animator animToOverride)
    {
        overrideController = new AnimatorOverrideController(animToOverride.runtimeAnimatorController);
        animToOverride.runtimeAnimatorController = overrideController;

        if (originalClip == null)
        {
            originalClip = GetOriginalAnimationClip(targetStateNameToChange, animToOverride);
            modifiedStateName = targetStateNameToChange;
        }

        if (originalClip != null)
        {
            if(poseIndex != -1)
            {
                overrideController[targetStateNameToChange] = newClips[poseIndex];

                if (!newClips[poseIndex].isLooping)
                {
                    StartCoroutine(WaitForAnimationToEnd(newClips[poseIndex].length, () =>
                    {
                        DoReset();
                    }));
                }
            }
        }
    }

    private void DoReset()
    {
        if (originalClip != null && !string.IsNullOrEmpty(modifiedStateName))
        {
            overrideController[modifiedStateName] = originalClip;
            originalClip = null;
            modifiedStateName = null;
        }
    }

    private AnimationClip GetOriginalAnimationClip(string stateName, Animator animator)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip.name == stateName)
                return clip;
        }
        return null;
    }

    private IEnumerator WaitForAnimationToEnd(float duration, Action onAnimationEnd)
    {
        yield return new WaitForSeconds(duration);
        // Call a function when the animation ends
        onAnimationEnd?.Invoke();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            panel.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            panel.SetActive(false);
        }
    }
}
