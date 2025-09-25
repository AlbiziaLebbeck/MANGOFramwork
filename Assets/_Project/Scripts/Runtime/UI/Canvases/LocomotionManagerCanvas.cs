using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocomotionManagerCanvas : MonoBehaviour
{
    [SerializeField] private GameObject containerPC;
    [SerializeField] private GameObject containerMobile;


    private void Awake()
    {
        var isMobile = CheckMobile.CheckIsMobile();

        containerPC.SetActive(!isMobile);
        containerMobile.SetActive(isMobile);
    }
}

