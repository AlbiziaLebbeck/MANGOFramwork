using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class SimpleTrigger : MonoBehaviour
{
    public UnityEvent OnEnter;
    public UnityEvent OnExit;

    public string TargetTag;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(TargetTag))
        {
            if(OnEnter != null)
            {
                OnEnter.Invoke();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(TargetTag))
        {
            if (OnExit != null)
            {
                OnExit.Invoke();
            }
        }
    }
}
