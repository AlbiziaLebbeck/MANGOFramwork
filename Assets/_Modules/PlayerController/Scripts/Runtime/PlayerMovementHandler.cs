using StarterAssets;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerMovementHandler : MonoBehaviour
{
    [Header("General Settings")]
    [SerializeField] private Transform cinemachineCameraTarget;
    private StarterAssetsInputs starterAssetsInputs;
    private int interactableLayer;
    private int uILayer;
    private Animator animator;
    private ThirdPersonController thirdPersonController;
    private CharacterController characterController;
    private PlayerActions playerActions;

    //From ThirdPersonController
    private float _speedChangeRate = 10.0f;
    private float _speed;
    private float _animationBlend;
    private int _animIDSpeed;
    private int _animIDMotionSpeed;
    private int _animIDGrounded;
    private int _animIDFreeFall;
    private int _animIDJump;
    [Header("Player Grounded")]
    public float GroundedOffset = -0.14f;
    public float GroundedRadius = 0.28f;
    public LayerMask GroundLayers;
    public bool Grounded = true;
    
    [Header("Player Speed")]
    [SerializeField] private float playerSpeed;
    [SerializeField] private float sprintSpeed = 5;
    [SerializeField] private float sprintTime;
    private float groundTimer;
    private float startSpeed;
    private bool isCrouch;

    [Space(10)]
    [Header("Mouse Clicking")]
    [SerializeField] private LayerMask clickableLayers;
    private bool startClick;
    private bool doubleClicked;
    private float lookRotationSpeed = 8f;
    private Vector3 playerVelocity;
    private float playerVerticalVelocity;
    private Vector3 clickToMoveTarget;
    [SerializeField] private ParticleSystem clickEffectPrefab;
    private ParticleSystem particleEffectReference;

    [Header("Touch")]
    private Vector3 movePosition;
    private bool IsPinchZoom;
    private Vector2 primaryTouchDelta;
    private Vector2 primaryTouchPosition;
    private Vector2 secondaryTouchPosition;
    private float previousTouchDelta;
    private float lastPinchDistance;

    [Space(10)]
    [Header("Zoom Settings")]
    [SerializeField] private float minCameraDistance = 1.2f;
    [SerializeField] private float maxCameraDistance = 10f;
    private float mouseScrollY;
    private float targetDistance = 2.5f;

    [Space(10)]
    [Header("Rotate Settings")]
    [SerializeField] private float pitchMin = -20f;
    [SerializeField] private float pitchMax = 40f;
    [SerializeField] private float rotationSpeed = 0.3f;
    [SerializeField] private float rotationThreshold = 0.1f;
    [SerializeField] private bool isChangeView;
    private float cinemachineTargetYaw;
    private float cinemachineTargetPitch;

    private void Awake()
    {
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        cinemachineTargetYaw = cinemachineCameraTarget.transform.rotation.eulerAngles.y;

        thirdPersonController = GetComponent<ThirdPersonController>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        playerActions = new PlayerActions();

        uILayer = LayerMask.NameToLayer("UI");
        interactableLayer = LayerMask.NameToLayer("Interactable");

        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        _animIDGrounded = Animator.StringToHash("Grounded");
        _animIDFreeFall = Animator.StringToHash("FreeFall");
        _animIDJump = Animator.StringToHash("Jump");

        startSpeed = thirdPersonController.MoveSpeed;
    }

    private void OnEnable() => playerActions.Enable();

    private void OnDisable() => playerActions.Disable();

    private void Start()
    {
        AssignInputs();
        AssignCamera();

        Grounded = thirdPersonController.Grounded;
    }

    private bool IsDrag = false;

    /// <summary>
    /// Initializing Input Actions for Player Movement
    /// </summary>
    private void AssignInputs()
    {
        if (!CheckMobile.Instance.CheckIsMobile())
        {
            playerActions.KeyboardMouse.LeftMousePressed.canceled += _ =>
            {
                movePosition = Input.mousePosition;

                StartCoroutine(ClickCoroutine());
            };

            playerActions.KeyboardMouse.RightMousePressed.performed += _ => { isChangeView = true; };
            playerActions.KeyboardMouse.RightMousePressed.canceled += _ => { isChangeView = false; };
            playerActions.KeyboardMouse.DoubleLeftPressed.performed += _ => { doubleClicked = true; };
            playerActions.KeyboardMouse.DoubleLeftPressed.canceled += _ => { doubleClicked = false; };

            playerActions.KeyboardMouse.MouseAxis.performed += _rotation => primaryTouchDelta = _rotation.ReadValue<Vector2>();
            playerActions.KeyboardMouse.MouseScrollY.performed += _scrollAmount => mouseScrollY = _scrollAmount.ReadValue<float>();

            playerActions.KeyboardMouse.Crouch.performed += _ =>
            {
                ToggleCrouch();
            };
        }
        else
        {
            playerActions.Touch.PrimaryTouchContact.canceled += _ => { if (primaryTouchDelta == Vector2.zero || !IsDrag) ClickToMove(); };
            playerActions.Touch.PrimaryTouchContact.performed += _position =>
            {
                IsDrag = false;
            };

            playerActions.Touch.PrimaryDoubleTap.performed += _ => { doubleClicked = true; };
            playerActions.Touch.PrimaryDoubleTap.canceled += _ => { doubleClicked = false; };

            playerActions.Touch.PrimaryTouchDelta.performed += _rotation =>
            {
                if(_rotation.ReadValue<Vector2>().sqrMagnitude > 1)
                {
                    isChangeView = true;
                    IsDrag = true;
                    primaryTouchDelta = _rotation.ReadValue<Vector2>();
                }
            };

            playerActions.Touch.PrimaryTouchDelta.canceled += _rotation =>
            {
                isChangeView = false;
                primaryTouchDelta = _rotation.ReadValue<Vector2>();
            };

            playerActions.Touch.PrimaryTouchPosition.performed += _position =>
            {
                primaryTouchPosition = _position.ReadValue<Vector2>();
                movePosition = _position.ReadValue<Vector2>();
            };

            playerActions.Touch.SecondaryTouchContact.started += _ =>
            {
                IsPinchZoom = true;
                IsDrag = true;
            };
            playerActions.Touch.SecondaryTouchContact.canceled += _ => IsPinchZoom = false;

            playerActions.Touch.SecondaryTouchPosition.performed += _position =>
            {
                secondaryTouchPosition = _position.ReadValue<Vector2>();
            };
        }

    }

    /// <summary>
    /// For Cinemachine setup, set follow and aim for follow camera
    /// </summary>
    private void AssignCamera()
    {
        PlayerCameraHandler.Instance.AssignFollowCamera(PlayerCameraHandler.Instance.MainVirtualCamera, cinemachineCameraTarget);
        PlayerCameraHandler.Instance.AssignCameraLookAt(PlayerCameraHandler.Instance.MainVirtualCamera, cinemachineCameraTarget);
    }

    private void Update()
    {
        HandleController();
        HandleCameraRotate();
        HandleAnimation();
        HandleCameraZoom();
        HandleMovement();
        HandleRotation();
        GroundCheck();
    }

    private void ToggleCrouch()
    {
        bool unCrouchAllow = true;

        if (isCrouch)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.up, out hit, characterController.height / 0.67f, GroundLayers))
            {
                if (hit.collider != null)
                {
                    unCrouchAllow = false;
                }
                else
                {
                    unCrouchAllow = true;
                }
            }
        }

        if (!unCrouchAllow) return;

        isCrouch = !isCrouch;
        animator.SetBool("Crouch", isCrouch);

        if (isCrouch)
        {
            characterController.height = characterController.height * 0.67f;
            characterController.center = new Vector3(0f, (characterController.height / 2) + 0.03f, 0f);
        }
        else
        {
            characterController.height = characterController.height / 0.67f;
            characterController.center = new Vector3(0f, (characterController.height / 2) + 0.03f, 0f);
        }
    }

    private void GroundCheck()
    {
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
        Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
            QueryTriggerInteraction.Ignore);

        animator.SetBool(_animIDGrounded, Grounded);

        if (!thirdPersonController.enabled)
        {
            thirdPersonController.Grounded = Grounded;
        }
    }

    private void HandleMovement()
    {
        if (thirdPersonController.enabled) return;

        if ((clickToMoveTarget - transform.position).sqrMagnitude < 0.1f)
        {
            playerVelocity = Vector2.zero;
            return;
        }

        float targetSpeed = doubleClicked ? sprintSpeed : startSpeed;

        float currentSpeed = playerSpeed;

        float speedOffset = 0.1f;

        if (currentSpeed < targetSpeed - speedOffset || currentSpeed > targetSpeed + speedOffset)
        {
            playerSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * 10);
            playerSpeed = Mathf.Round(playerSpeed * 1000f) / 1000f;
        }
        else
        {
            playerSpeed = targetSpeed;
        }

        if(Grounded)
        {
            groundTimer = 0.2f;
        }

        if(groundTimer > 0)
        {
            groundTimer -= Time.deltaTime;
        }

        if (Grounded && playerVerticalVelocity < 0)
        {
            playerVerticalVelocity = 0f;
        }

        playerVerticalVelocity += thirdPersonController.Gravity * Time.deltaTime;

        playerVelocity = (clickToMoveTarget - transform.position).normalized * playerSpeed;

        playerVelocity.y += playerVerticalVelocity;

        characterController.Move(playerVelocity * Time.deltaTime);
    }

    private void HandleRotation()
    {
        if (thirdPersonController.enabled) return;

        if ((clickToMoveTarget - transform.position).sqrMagnitude < 0.1f)
        {
            playerVelocity = Vector2.zero;
            return;
        }

        if (playerVelocity.sqrMagnitude > 0.01f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(playerVelocity.normalized, Vector3.up);          
            lookRotation.x = 0;
            lookRotation.z = 0;
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * lookRotationSpeed);
        }
    }

    private void HandleCameraZoom()
    {
        var zoomAmount = 0f;

        if(CheckMobile.Instance && CheckMobile.Instance.CheckIsMobile() && IsPinchZoom)
        {
            var distance = Vector2.Distance(primaryTouchPosition, secondaryTouchPosition);

            if (distance > lastPinchDistance) zoomAmount = 1;
            else zoomAmount = -1;

            lastPinchDistance = distance;
        }
        else
        {
            zoomAmount = mouseScrollY;
        }

        if (zoomAmount > 0) targetDistance -= 0.5f;
        if (zoomAmount < 0) targetDistance += 0.5f;

        targetDistance = Mathf.Clamp(targetDistance, minCameraDistance, maxCameraDistance);

        float zoomSpeed = 10f;

        if(PlayerCameraHandler.Instance != null)
        {
            PlayerCameraHandler.Instance.MainCameraFramingTransposer.m_CameraDistance = Mathf.Lerp(PlayerCameraHandler.Instance.MainCameraFramingTransposer.m_CameraDistance, targetDistance, Time.deltaTime * zoomSpeed);
        }
    }

    private void HandleController()
    {
        if (starterAssetsInputs.move != Vector2.zero || starterAssetsInputs.jump)
        {
            startClick = false;
            thirdPersonController.enabled = true;
        }
    }

    private void HandleAnimation()
    {
        if (!startClick) return;

        float targetSpeed;

        if (playerVelocity == Vector3.zero)
        {
            targetSpeed = 0;
        }
        else
        {
            targetSpeed = playerVelocity.magnitude;
        }

        float currentHorizontalSpeed = new Vector3(characterController.velocity.x, 0.0f, characterController.velocity.z).magnitude;

        float speedOffset = 0.1f;

        if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
        {
            _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed, Time.deltaTime * _speedChangeRate);

            _speed = Mathf.Round(_speed * 1000f) / 1000f;
        }
        else
        {
            _speed = targetSpeed;
        }

        _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * _speedChangeRate);
        if (_animationBlend < 0.01f) _animationBlend = 0f;

        animator.SetFloat(_animIDSpeed, _animationBlend);
        animator.SetFloat(_animIDMotionSpeed, 1f);

        if (Grounded)
        {
            animator.SetBool(_animIDJump, false);
            animator.SetBool(_animIDFreeFall, false);
        }
        else
        {
            animator.SetBool(_animIDFreeFall, true);
        }
    }

    private void HandleCameraRotate()
    {
        cinemachineTargetYaw += 45 * starterAssetsInputs.move.x * Time.deltaTime;

        if (primaryTouchDelta.sqrMagnitude != previousTouchDelta)
        {
            previousTouchDelta = primaryTouchDelta.sqrMagnitude;

            if (isChangeView && primaryTouchDelta.sqrMagnitude >= rotationThreshold)
            {
                cinemachineTargetYaw += primaryTouchDelta.x * Time.deltaTime * rotationSpeed;
                cinemachineTargetPitch += primaryTouchDelta.y * Time.deltaTime * -rotationSpeed;

                cinemachineTargetPitch = Mathf.Clamp(cinemachineTargetPitch, pitchMin, pitchMax);
            }
        }

        cinemachineCameraTarget.transform.rotation = Quaternion.Euler(cinemachineTargetPitch, cinemachineTargetYaw, 0.0f);
    }

    private IEnumerator ClickCoroutine()
    {
        yield return new WaitUntil(() => Grounded);
        ClickToMove();
    }

    private void ClickToMove()
    {
        if (isChangeView) return;
        if (IsPointerOverUIElement()) return;

        thirdPersonController.enabled = false;
        startClick = true;

        RaycastHit hit;

        if(Physics.Raycast(Camera.main.ScreenPointToRay(movePosition), out hit, 100, clickableLayers))
        {
            clickToMoveTarget = hit.point;
            clickToMoveTarget.y = 0f;

            if (clickEffectPrefab == null) return;
            if(clickEffectPrefab != null && particleEffectReference == null)
            {
                var particleObject = GameObject.Find($"{clickEffectPrefab.name}(Clone)");

                if(particleObject == null)
                {
                    particleEffectReference = Instantiate(clickEffectPrefab, hit.point + new Vector3(0, 0.1f, 0), clickEffectPrefab.transform.rotation);
                }
                else
                {
                    particleEffectReference = particleObject.GetComponent<ParticleSystem>();
                }
            }
            
            if (particleEffectReference)
            {
                particleEffectReference.transform.position = hit.point + new Vector3(0, 0.1f, 0);
                particleEffectReference.Play();
            }
        }
    }

    #region Handle PointerOverUI
    public bool IsPointerOverUIElement()
    {
        return IsPointerOverUIElement(GetEventSystemRaycastResults());
    }

    private bool IsPointerOverUIElement(List<RaycastResult> eventSystemRaycastResults)
    {
        for (int index = 0; index < eventSystemRaycastResults.Count; index++)
        {
            RaycastResult cursorRaycastResult = eventSystemRaycastResults[index];
            if(cursorRaycastResult.gameObject.layer == uILayer || cursorRaycastResult.gameObject.layer == interactableLayer)
            {
                return true;
            }
        }
        return false;
    }

    private static List<RaycastResult> GetEventSystemRaycastResults()
    {
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = Input.mousePosition;
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, raycastResults);
        return raycastResults;
    }
    #endregion
}
