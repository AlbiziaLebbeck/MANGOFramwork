using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class DragRotator : MonoBehaviour
{
    [SerializeField] private InputAction pressed, axis;
    [SerializeField] private float speed = 0.3f;
    [SerializeField] private Transform target;

    private Vector2 rotation;
    private bool rotateAllowed;


    private void Awake()
    {
        AddActionBinding();
    }
    private void AddActionBinding()
    {
        pressed = new InputAction();
        pressed.AddBinding("<Mouse>/leftButton");
        pressed.AddBinding("<Touchscreen>/Press");

        pressed.performed += _ => { StartCoroutine(Rotate()); };
        pressed.canceled += _ => { rotateAllowed = false; };

        axis = new InputAction();
        axis.AddBinding("<Mouse>/delta");
        axis.AddBinding("<Touchscreen>/delta");

        axis.performed += context => { rotation = context.ReadValue<Vector2>(); };
    }

    private void OnEnable()
    {
        pressed.Enable();
        axis.Enable();
    }

    private void OnDisable()
    {
        pressed.Disable();
        axis.Disable();
    }

    private IEnumerator Rotate()
    {
        rotateAllowed = true;
        while (rotateAllowed)
        {
            rotation *= speed;
            if (target != null) target.Rotate(Vector3.down, rotation.x, Space.World);
            yield return null;
        }
    }

    public void SetRotateTarget(Transform _target)
    {
        target = _target;
    }
}
