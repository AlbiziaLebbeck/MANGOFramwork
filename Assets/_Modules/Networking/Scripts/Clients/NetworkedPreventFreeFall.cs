using FishNet.Object;
using UnityEngine;

public class NetworkedPreventFreeFall : NetworkBehaviour
{
    [SerializeField] private CharacterController controller;
    [SerializeField] private float freeFalllimit = -100;

    private void Awake()
    {
        if(controller == null) controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if(!IsOwner) return;

        if(controller && !controller.isGrounded)
        {
            // Falling 

            if(transform.position.y < freeFalllimit)
            {
                PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(true);
                PersistentCanvas.LoadingCanvas.SetInformationDisplay("Respawning...");
                transform.position = FindObjectOfType<AreaSpawner>().GetRandomSpawn();
                PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
            }
        }
    }
}
