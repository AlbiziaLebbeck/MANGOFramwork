using FishNet.Object;
using UnityEngine;

public class NetworkedPreventFreeFall : NetworkBehaviour
{
    [SerializeField] private CharacterController controller;
    [SerializeField] private float freeFalllimit = -100;
    [SerializeField] private AreaSpawner spawner;

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
                if(spawner == null)
                {
                    spawner = GameObject.Find("PlayerAreaSpawner").GetComponent<AreaSpawner>();
                }

                if(spawner != null)
                {
                    transform.position = spawner.GetRandomSpawn();
                }
                else
                {
                    spawner = FindObjectOfType<AreaSpawner>();
                    transform.position = spawner.GetRandomSpawn();
                }

                PersistentCanvas.LoadingCanvas.ToggleLoadingScreen(false);
            }
        }
    }
}
