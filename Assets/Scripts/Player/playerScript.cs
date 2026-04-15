using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// Handles player movement, jumping, wall-jumping, animation, and death

public class playerScript : MonoBehaviour
{
    // Inspector fields
    [Header("Movement")]
    public float speed = 3f;
    public float jumpHeight = 1f;
    public float lookSpeed = 5f;
    public bool rotateMovement = false;
    public bool invertHorizontal = false;
    public bool invertVertical = false;

    [Header("Wall Detection")]
    public Transform wallCaster;
    public LayerMask playerMask;
    public float wallDistance = 1f;

    [Header("References")]
    public Rigidbody rb;
    public Animator pAnimator;
    public GameObject playerObject;
    public GameObject dieModel;
    public GameObject glassEmit;
    public AudioSource jumpSound;
    public AudioClip jumpClip;

    [Header("State")]
    public bool canDie = true;
    public bool isDead = false;

    [Header("Grounding")]
    [SerializeField]
    [Tooltip("If enabled, the player can jump on any sufficiently floor-like collider except objects on Jump Excluded Layers. If disabled, the old Ground tag behavior is used.")]
    private bool jumpOnAnyLayerExceptExcluded;

    [SerializeField]
    [Tooltip("When Jump On Any Layer Except Excluded is enabled, these layers cannot be jumped from.")]
    private LayerMask jumpExcludedLayers;

    [SerializeField]
    [Range(0.0f, 1.0f)]
    [Tooltip("Minimum upward contact normal for a collision to count as jumpable ground. Higher values require flatter floors.")]
    private float minimumJumpNormalY = 0.5f;

    [Header("Ground Stick")]
    [SerializeField]
    [Tooltip("If enabled, the player is gently held down onto nearby walkable ground until jumping or leaving an edge.")]
    private bool stickToGround;

    [SerializeField]
    [Tooltip("Layers that should not pull the player down with ground stick.")]
    private LayerMask groundStickExcludedLayers;

    [SerializeField]
    [Tooltip("Optional tag that prevents ground stick. Leave empty to ignore tags.")]
    private string groundStickExcludedTag = "";

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("How far below the player's collider to look for ground before applying stickiness.")]
    private float groundStickDistance = 0.35f;

    [SerializeField]
    [Min(0.0f)]
    [Tooltip("The downward velocity used to keep the player attached to slopes and floors.")]
    private float groundStickDownVelocity = 4.0f;

    [SerializeField]
    [Range(0.0f, 1.0f)]
    [Tooltip("Minimum upward normal for a surface to use ground stick. Higher values prevent sticking to steep slopes.")]
    private float minimumGroundStickNormalY = 0.5f;

    [Header("Height Death")]
    [SerializeField]
    private bool dieBelowHeight;

    [SerializeField]
    private float minimumHeight = -50.0f;

    [SerializeField]
    private bool dieAboveHeight;

    [SerializeField]
    private float maximumHeight = 100.0f;

    [Header("Input Actions")]
    [SerializeField] private InputAction moveAction;
    [SerializeField] private InputAction jumpAction;

    // Private state
    private PlayerPortalable portalable;
    private Collider playerCollider;

    public bool HasMoveInput => moveAction.ReadValue<Vector2>().sqrMagnitude > 0.01f;

    private readonly HashSet<Collider> groundedColliders = new HashSet<Collider>();
    private readonly HashSet<Collider> groundedTriggers = new HashSet<Collider>();
    private bool isJumping = false;
    private bool justJumped = false;
    private bool onWall = false;
    private bool willDie = false;
    private float warpImmunityTime = 0f;
    private float groundStickDisabledUntil = 0f;

    private Vector3 lookDir = Vector3.zero;
    private Vector3 wallDirection = Vector3.zero;
    private Vector3 addedVelocity = Vector3.zero;
    private Vector3 currentGroundNormal = Vector3.up;
    private float ignoreInput = 0f;



    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        portalable = GetComponent<PlayerPortalable>();
        playerCollider = GetComponent<Collider>();
    }

    void Start()
    {
        SetAnimatorBool("onWall", false);
    }

    void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
    }

    void Update()
    {
        if (isDead) return;

        CheckHeightDeath();
        if (isDead) return;

        if (portalable != null && portalable.IsSplineFollowing) return;

        // Wall jump
        if (jumpAction.WasPressedThisFrame() && onWall && !IsOnGround && !justJumped)
        {
            jumpSound.PlayOneShot(jumpClip);
            SetAnimatorTrigger("wallJumped");
            addedVelocity = new Vector3(wallDirection.x * 7f, 10f, wallDirection.z * 7f);
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 5f, rb.linearVelocity.z);
            ignoreInput = 2f;
        }
    }

    void FixedUpdate()
    {
        // Decay wall-jump momentum over time
        addedVelocity *= 0.95f;
        ignoreInput *= 0.9f;
        if (Mathf.Abs(addedVelocity.x) + Mathf.Abs(addedVelocity.z) < 1f)
            addedVelocity = Vector3.zero;

        if (isDead) return;

        if (portalable != null && portalable.IsSplineFollowing)
        {
            SetAnimatorBool("isWalking", false);
            SetAnimatorBool("jumpButton", false);
            lookDir = Vector3.zero;
            justJumped = false;
            return;
        }

        HandleMovement();
        HandleJump();
        ApplyGroundStick();
        HandleFallAnimation();
        UpdateLookDirection();

        justJumped = false;
    }

    // Movement

    // Reads movement input and applies horizontal velocity
    private void HandleMovement()
    {
        Vector2 rawInput = moveAction.ReadValue<Vector2>();

        float hInput = rotateMovement ? rawInput.y : rawInput.x;
        float vInput = rotateMovement ? rawInput.x : rawInput.y;

        if (invertHorizontal) hInput *= -1f;
        if (invertVertical)   vInput *= -1f;

        // Calculate how much player input should affect movement based on ignoreInput
        // Causes the player to lose steering control during a wall jump or portal fling,
        // but still allows addedVelocity (momentum) to physically push the character
        float inputMultiplier = Mathf.Clamp01(1f - ignoreInput);
        
        Vector3 flatMoveVelocity = new Vector3(
            hInput * speed * inputMultiplier + addedVelocity.x,
            0.0f,
            vInput * speed * inputMultiplier + addedVelocity.z
        );

        Vector3 targetVelocity = new Vector3(
            flatMoveVelocity.x,
            rb.linearVelocity.y,
            flatMoveVelocity.z
        );

        if (TryGetSlopeMoveVelocity(flatMoveVelocity, out Vector3 slopeMoveVelocity))
        {
            targetVelocity = slopeMoveVelocity;
        }

        // Apply movement vector directly instead of fighting rb.position
        rb.linearVelocity = targetVelocity;

        bool isWalking = Mathf.Abs(hInput * speed) >= 1f || Mathf.Abs(vInput * speed) >= 1f;
        SetAnimatorBool("isWalking", isWalking);

        // Store for look-direction rotation
        lookDir.x = rb.linearVelocity.x;
        lookDir.z = rb.linearVelocity.z;
    }

    // Handles regular jump input and animator state
    private void HandleJump()
    {
        bool jumpHeld = jumpAction.IsPressed();

        if (jumpHeld)
        {
            if (IsOnGround && rb.linearVelocity.y < 5f)
            {
                jumpSound.PlayOneShot(jumpClip);
                SetAnimatorTrigger("jumpWhenFalling");
                DisableGroundStickFor(0.2f);
                ClearGrounding();
                rb.linearVelocity = new Vector3(0f, 5.5f * jumpHeight, 0f);
                ResetAnimatorTrigger("Landed");
                SetAnimatorTrigger("Jumped");
                isJumping = true;
                justJumped = true;
                SetAnimatorBool("isOnFloor", false);
            }
        }

        SetAnimatorBool("jumpButton", jumpHeld);
    }

    // Triggers falling animations based on vertical velocity
    private void HandleFallAnimation()
    {
        bool falling = (isJumping && rb.linearVelocity.y < 1f)
                    || (!IsOnGround && rb.linearVelocity.y < -2f);

        if (falling)
        {
            SetAnimatorTrigger("Falling");
            ResetAnimatorTrigger("jumpWhenFalling");
            ResetAnimatorTrigger("wallJumped");
            SetAnimatorBool("isOnFloor", false);
            SetAnimatorBool("isWalking", false);
            ClearGrounding();
        }
    }

    // Smoothly rotates the player to face movement direction
    private void UpdateLookDirection()
    {
        Vector3 flatVelocity = lookDir.normalized;
        if (flatVelocity != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(flatVelocity),
                Time.deltaTime * lookSpeed
            );
        }
    }

    private void CheckHeightDeath()
    {
        if (!canDie)
        {
            return;
        }

        float currentHeight = transform.position.y;
        if ((dieBelowHeight && currentHeight < minimumHeight) ||
            (dieAboveHeight && currentHeight > maximumHeight))
        {
            KillPlayer();
        }
    }

    // Ground / wall state

    // true if the player is standing on something
    private bool IsOnGround => groundedColliders.Count > 0 || groundedTriggers.Count > 0;

    public void almostFloor()
    {
        if (Time.time < warpImmunityTime) return;
        if (portalable != null && portalable.IsInPortal) return; // Immune while inside portal

        
        if (rb.linearVelocity.y < -13f)
            willDie = true;
    }

    public void IsOnWall(bool isOnWall)
    {
        if (isDead) return;
        onWall = isOnWall;
        SetAnimatorBool("onWall", isOnWall);
    }

    public void SetWallDirection(Vector3 wDirection)
    {
        wallDirection = wDirection;
    }

    public void IgnoreInput(float ignoreAmt)
    {
        Debug.Log("ignoreInput = " + ignoreAmt);
        ignoreInput = ignoreAmt;
    }

    public void SetWarpMomentum(Vector3 warpVelocity)
    {
        willDie = false; // Prevents fall-damage death upon exiting the portal
        warpImmunityTime = Time.time + 0.5f; // Half-second of immunity to high-speed trigger deaths

        Vector3 flatVelocity = new Vector3(warpVelocity.x, 0f, warpVelocity.z);
        
        // Boost momentum slightly to guarantee the player clears the exit portal cleanly
        // if they were just nudging into it slowly.
        if (flatVelocity.magnitude < speed && flatVelocity.magnitude > 0.1f)
        {
             addedVelocity = flatVelocity.normalized * speed;
        }
        else
        {
             addedVelocity = flatVelocity;
        }

        // Fast-suspend user input briefly so their world-space input doesn't pull them back into the portal
        ignoreInput = 1f;
        DisableGroundStickFor(0.25f);
    }

    public void PreventFallDamageFor(float seconds)
    {
        willDie = false;
        warpImmunityTime = Mathf.Max(warpImmunityTime, Time.time + Mathf.Max(0f, seconds));
    }

    public void DisableGroundStickFor(float seconds)
    {
        groundStickDisabledUntil = Mathf.Max(groundStickDisabledUntil, Time.time + Mathf.Max(0f, seconds));
    }

    // Freeze / Revive helpers

    public void Freeze(bool freeze) => isDead = freeze;

    public void Revive() => isDead = false;

    // Collision & trigger callbacks

    void OnTriggerEnter(Collider collider)
    {
        if (isDead)
            return;

        BouncePad bouncePad = collider.GetComponentInParent<BouncePad>();
        if (bouncePad != null && bouncePad.IsEnabled)
        {
            PreventFallDamageFor(0.5f);
        }

        if (collider.gameObject.layer == 10 || collider.gameObject.layer == 8)
            return;

        // Do not take fall damage or reset states if touching a Portal trigger
        if (collider.GetComponent<Portal>() != null || (portalable != null && portalable.IsInPortal))
        {
            willDie = false; // Instantly forgive fall damage
            return;
        }

        if (!IsGroundTrigger(collider))
        {
            return;
        }

        groundedTriggers.Add(collider);

        // Reset jump/fall animation state on landing in a trigger zone
        ResetAnimatorTrigger("Jumped");
        ResetAnimatorTrigger("Falling");
        ResetAnimatorTrigger("Landed");
        ResetAnimatorTrigger("wallJumped");
        isJumping = false;
        SetAnimatorBool("isOnFloor", true);

        // Fall-damage death
        if (willDie && canDie && Time.time > warpImmunityTime)
            KillPlayer();
    }

    void OnTriggerStay(Collider collider)
    {
        if (!isDead && IsGroundTrigger(collider))
        {
            SetAnimatorBool("isOnFloor", true);
            groundedTriggers.Add(collider);
        }
    }

    void OnTriggerExit(Collider collider)
    {
        if (isDead) return;
        groundedTriggers.Remove(collider);
        RefreshGroundedAnimator();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isDead)
        {
            return;
        }

        UpdateCollisionGrounding(collision);

        if (IsGroundCollision(collision) || IsOnGround)
        {
            isJumping = false;
            ResetAnimatorTrigger("Jumped");
            ResetAnimatorTrigger("Falling");
            SetAnimatorTrigger("Landed");
            SetAnimatorBool("isOnFloor", true);
        }
    }

    void OnCollisionStay(Collision collision)
    {
        if (!isDead)
        {
            UpdateCollisionGrounding(collision);
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.collider != null)
        {
            groundedColliders.Remove(collision.collider);
        }

        if (groundedColliders.Count == 0)
        {
            currentGroundNormal = Vector3.up;
        }

        RefreshGroundedAnimator();
    }

    private void UpdateCollisionGrounding(Collision collision)
    {
        if (collision.collider == null)
        {
            return;
        }

        if (TryGetGroundCollisionNormal(collision, out Vector3 groundNormal))
        {
            groundedColliders.Add(collision.collider);
            currentGroundNormal = groundNormal;
            SetAnimatorBool("isOnFloor", true);
        }
        else
        {
            groundedColliders.Remove(collision.collider);
            RefreshGroundedAnimator();
        }
    }

    private bool IsGroundCollision(Collision collision)
    {
        return TryGetGroundCollisionNormal(collision, out _);
    }

    private bool TryGetGroundCollisionNormal(Collision collision, out Vector3 groundNormal)
    {
        groundNormal = Vector3.up;

        if (collision.collider == null)
        {
            return false;
        }

        if (jumpOnAnyLayerExceptExcluded)
        {
            return !IsLayerInMask(collision.gameObject.layer, jumpExcludedLayers) &&
                TryGetWalkableContactNormal(collision, minimumJumpNormalY, out groundNormal);
        }

        if (!collision.gameObject.CompareTag("Ground"))
        {
            return false;
        }

        return TryGetWalkableContactNormal(collision, minimumJumpNormalY, out groundNormal);
    }

    private bool IsGroundTrigger(Collider triggerCollider)
    {
        if (triggerCollider == null)
        {
            return false;
        }

        if (jumpOnAnyLayerExceptExcluded)
        {
            return false;
        }

        return triggerCollider.gameObject.layer != 10 && triggerCollider.gameObject.layer != 8;
    }

    private bool TryGetWalkableContactNormal(Collision collision, float minimumNormalY, out Vector3 groundNormal)
    {
        groundNormal = Vector3.up;
        float highestNormalY = float.NegativeInfinity;

        for (int i = 0; i < collision.contactCount; ++i)
        {
            Vector3 normal = collision.GetContact(i).normal;
            if (normal.y >= minimumNormalY && normal.y > highestNormalY)
            {
                groundNormal = normal;
                highestNormalY = normal.y;
            }
        }

        return highestNormalY > float.NegativeInfinity;
    }

    private void ApplyGroundStick()
    {
        if (!stickToGround ||
            Time.time < groundStickDisabledUntil ||
            justJumped ||
            !IsOnGround ||
            isDead ||
            (portalable != null && (portalable.IsInPortal || portalable.IsSplineFollowing)))
        {
            return;
        }

        Vector3 flatVelocity = rb.linearVelocity;
        flatVelocity.y = 0.0f;
        if (TryGetSlopeMoveVelocity(flatVelocity, out Vector3 slopeVelocity) && slopeVelocity.y > 0.05f)
        {
            return;
        }

        if (!TryFindGroundStickSurface())
        {
            return;
        }

        Vector3 velocity = rb.linearVelocity;
        if (velocity.y > -groundStickDownVelocity)
        {
            velocity.y = -groundStickDownVelocity;
            rb.linearVelocity = velocity;
        }
    }

    private bool TryGetSlopeMoveVelocity(Vector3 flatMoveVelocity, out Vector3 slopeMoveVelocity)
    {
        slopeMoveVelocity = flatMoveVelocity;

        if (!stickToGround ||
            !IsOnGround ||
            currentGroundNormal.y < minimumGroundStickNormalY ||
            flatMoveVelocity.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector3 projectedVelocity = Vector3.ProjectOnPlane(flatMoveVelocity, currentGroundNormal);
        if (projectedVelocity.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        slopeMoveVelocity = projectedVelocity.normalized * flatMoveVelocity.magnitude;
        return true;
    }

    private bool TryFindGroundStickSurface()
    {
        if (playerCollider == null)
        {
            return false;
        }

        Bounds bounds = playerCollider.bounds;
        float radius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.85f);
        float castDistance = bounds.extents.y + groundStickDistance;
        int layerMask = GetGroundProbeLayerMask();
        RaycastHit[] hits = Physics.SphereCastAll(
            bounds.center,
            radius,
            Vector3.down,
            castDistance,
            layerMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; ++i)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null ||
                hitCollider == playerCollider ||
                hitCollider.transform.IsChildOf(transform) ||
                transform.IsChildOf(hitCollider.transform))
            {
                continue;
            }

            if (CanStickToGround(hitCollider, hits[i].normal))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanStickToGround(Collider hitCollider, Vector3 normal)
    {
        if (normal.y < minimumGroundStickNormalY)
        {
            return false;
        }

        return CanUseGroundStickCollider(hitCollider);
    }

    private bool CanUseGroundStickCollider(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return false;
        }

        GameObject hitObject = hitCollider.gameObject;
        if (IsLayerInMask(hitObject.layer, groundStickExcludedLayers))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(groundStickExcludedTag) || !hitObject.CompareTag(groundStickExcludedTag);
    }

    private bool IsSelfCollider(Collider otherCollider)
    {
        return otherCollider == null ||
            otherCollider == playerCollider ||
            otherCollider.transform.IsChildOf(transform) ||
            transform.IsChildOf(otherCollider.transform);
    }

    private int GetGroundProbeLayerMask()
    {
        return playerMask.value == 0 ? ~0 : ~playerMask.value;
    }

    private void RefreshGroundedAnimator()
    {
        if (isDead)
        {
            return;
        }

        bool onGroundNow = IsOnGround;
        SetAnimatorBool("isOnFloor", onGroundNow);

        if (!onGroundNow)
        {
            ResetAnimatorTrigger("Landed");
        }
    }

    private void ClearGrounding()
    {
        groundedColliders.Clear();
        groundedTriggers.Clear();
        currentGroundNormal = Vector3.up;
    }

    private static bool IsLayerInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private void SetAnimatorBool(string parameterName, bool value)
    {
        if (pAnimator != null)
        {
            pAnimator.SetBool(parameterName, value);
        }
    }

    private void SetAnimatorTrigger(string parameterName)
    {
        if (pAnimator != null)
        {
            pAnimator.SetTrigger(parameterName);
        }
    }

    private void ResetAnimatorTrigger(string parameterName)
    {
        if (pAnimator != null)
        {
            pAnimator.ResetTrigger(parameterName);
        }
    }

    // Death

    private void KillPlayer()
    {
        isDead = true;
        ClearGrounding();
        pAnimator = null;

        Vector3 oldVelocity = rb.linearVelocity;
        Vector3 oldPosition = playerObject.transform.position;

        Destroy(playerObject);

        GameObject brokenModel  = Instantiate(dieModel,   transform);
        GameObject glassEffect  = Instantiate(glassEmit,  transform);
        brokenModel.transform.position = oldPosition;
        glassEffect.transform.position = oldPosition;

        // Spread pieces with the player's velocity on death
        foreach (Rigidbody piece in brokenModel.GetComponentsInChildren<Rigidbody>())
            piece.linearVelocity = oldVelocity;

        rb.isKinematic = true;

        StartCoroutine(ReloadSceneAfterDelay(2f));
    }

    private IEnumerator ReloadSceneAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
