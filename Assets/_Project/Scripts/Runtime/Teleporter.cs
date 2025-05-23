using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Teleporter : MonoBehaviour
{
    public Teleporter Destination;
    public Transform SpawnPoint { get => spawnPoint; }
    public bool IsPlayerComing;
    [SerializeField] private Transform spawnPoint;
    public bool KeepRotation;

    public Transform currentplayer;
    public bool teleportAllow;

    public void DoTeleport(Transform player, bool keepRotation)
    {
        if(player != null)
        {
            if(player.TryGetComponent(out CharacterController controller))
            {
                controller.enabled = false;
                player.position = Destination.SpawnPoint.position;
                player.rotation = keepRotation ? player.transform.rotation : Destination.SpawnPoint.rotation;
                controller.enabled = true;
            }
        }
    }

    public float StayDuration = 1.5f;
    public float elaspedTime;

    private void Update()
    {
        if (!teleportAllow) return;

        elaspedTime += Time.deltaTime;

        if(elaspedTime >= StayDuration)
        {
            DoTeleport(currentplayer, KeepRotation);
            elaspedTime = 0;
            teleportAllow = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayerComing) return;

        if (other.CompareTag("Player"))
        {
            elaspedTime = 0;
            teleportAllow = true;
            currentplayer = other.transform;
            Destination.IsPlayerComing = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            elaspedTime = 0;
            teleportAllow = false;
            currentplayer = null;
            IsPlayerComing = false;
        }
    }
}
