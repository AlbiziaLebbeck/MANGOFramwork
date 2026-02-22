using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class LoadingSpinner : MonoBehaviour
{
    public bool Rotation = true;
    [Range(-10, 10)] public float RotationSpeed = 1;
    public AnimationCurve RotationAnimationCurve = AnimationCurve.Linear(0, 0, 1, 1);

    private Image _image;

    public void Start()
    {
        _image = GetComponent<Image>();
        Rotation = false;
    }

    public void Update()
    {
        if (Rotation)
        {
            transform.localEulerAngles = new Vector3(0, 0, -360 * RotationAnimationCurve.Evaluate((RotationSpeed * Time.time) % 1));
        }
    }
}

