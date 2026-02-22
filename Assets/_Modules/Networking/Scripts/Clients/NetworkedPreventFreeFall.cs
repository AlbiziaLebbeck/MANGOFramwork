using FishNet.Object;
using UnityEngine;

public class NetworkedPreventFreeFall : NetworkBehaviour
{
    [SerializeField] private CharacterController controller;
    [SerializeField] private float freeFalllimit = -100;
    private AreaSpawner spawner;

    private void Awake()
    {
        if(controller == null) controller = GetComponent<CharacterController>();

        if (AreaSpawner.PlayerSpawnerInstance != null) spawner = AreaSpawner.PlayerSpawnerInstance;

        if(spawner == null)
        {
            GameObject spawnerObj = GameObject.Find("PlayerSpawner");
            if(spawnerObj != null)
            {
                spawner = spawnerObj.GetComponent<AreaSpawner>();
            }
        }

        if(spawner == null)
        {
            spawner = Object.FindAnyObjectByType<AreaSpawner>();
        }
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

                if(spawner != null)
                {
                    transform.position = spawner.GetRandomSpawn();
                }
                else
                {
                    if (AreaSpawner.PlayerSpawnerInstance != null) spawner = AreaSpawner.PlayerSpawnerInstance;

                    if (spawner == null)
                    {
                        GameObject spawnerObj = GameObject.Find("AreaSpawner");
                        if (spawnerObj != null)
                        {
                            spawner = spawnerObj.GetComponent<AreaSpawner>();
                        }
                    }

                    if (spawner == null)
                    {
                        spawner = Object.FindAnyObjectByType<AreaSpawner>();
                    }

                    if (spawner == null)
                    {
                        transform.position = Vector3.zero;
                    }
                }
            }
        }
    }
}
