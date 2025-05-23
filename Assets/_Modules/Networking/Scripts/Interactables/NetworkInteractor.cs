using FishNet.Object;
using UnityEngine;

public class NetworkInteractor : NetworkBehaviour
{
    public float interactDistance = 3f;
    public LayerMask interactableLayer;
    public bool IsInteracting = false;
    private NetworkInteractable currentInteractable;

    private void Update()
    {
        if (!base.IsOwner)
        {
            return;
        }

        if (IsInteracting)
        {
            if (currentInteractable != null)
            {
                currentInteractable.Client_OnLoseFocus();
                currentInteractable = null;
            }
            return;
        }

        DetectInteractble();
    }

    private void DetectInteractble()
    {
        NetworkInteractable detectedInteractable = null;

        Ray ray = new Ray(transform.position, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableLayer))
        {
            if (hit.collider.TryGetComponent(out NetworkInteractable interactable))
            {
                if (!interactable.IsInteracted.Value)
                {
                    detectedInteractable = interactable;
                }
            }
        }

        if (detectedInteractable == null)
        {
            detectedInteractable = FindClosestInteractable();
        }

        if (detectedInteractable != null)
        {
            if (detectedInteractable != currentInteractable)
            {
                currentInteractable?.Client_OnLoseFocus();

                detectedInteractable.Client_OnFocus();

                currentInteractable = detectedInteractable;
            }
            else
            {
                detectedInteractable.Client_OnFocus();
            }
        }

        if (detectedInteractable == null && currentInteractable != null)
        {
            currentInteractable.Client_OnLoseFocus();
            currentInteractable = null;
        }
    }

    private NetworkInteractable FindClosestInteractable()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, interactDistance, interactableLayer);
        NetworkInteractable closest = null;

        float closestDistance = Mathf.Infinity;

        foreach (Collider hit in hits)
        {
            if (hit.TryGetComponent(out NetworkInteractable networkInteractable))
            {
                if (!networkInteractable.IsInteracted.Value)
                {
                    float distance = Vector3.Distance(transform.position, hit.transform.position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closest = networkInteractable;
                    }
                }
            }
        }

        return closest;
    }
}
