using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;

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

    [Header("Input Actions")]
    [SerializeField] private InputAction moveAction;
    [SerializeField] private InputAction jumpAction;

    // Private state
    private PlayerPortalable portalable;

    private bool isGrounded = false;    // true while colliding with "Ground" tag
    private bool grounded = false;      // true while inside a ground trigger zone
    private bool isJumping = false;
    private bool justJumped = false;
    private bool onWall = false;
    private bool willDie = false;
    private float warpImmunityTime = 0f;

    private Vector3 lookDir = Vector3.zero;
    private Vector3 wallDirection = Vector3.zero;
    private Vector3 addedVelocity = Vector3.zero;
    private float ignoreInput = 0f;



    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        portalable = GetComponent<PlayerPortalable>();
    }

    void Start()
    {
        pAnimator.SetBool("onWall", false);
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

        // Wall jump
        if (jumpAction.WasPressedThisFrame() && onWall && !IsOnGround && !justJumped)
        {
            jumpSound.PlayOneShot(jumpClip);
            pAnimator.SetTrigger("wallJumped");
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

        HandleMovement();
        HandleJump();
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
        
        Vector3 targetVelocity = new Vector3(
            hInput * speed * inputMultiplier + addedVelocity.x,
            rb.linearVelocity.y,
            vInput * speed * inputMultiplier + addedVelocity.z
        );

        // Apply movement vector directly instead of fighting rb.position
        rb.linearVelocity = targetVelocity;

        bool isWalking = Mathf.Abs(hInput * speed) >= 1f || Mathf.Abs(vInput * speed) >= 1f;
        pAnimator.SetBool("isWalking", isWalking);

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
                pAnimator.SetTrigger("jumpWhenFalling");
                rb.linearVelocity = new Vector3(0f, 5.5f * jumpHeight, 0f);
                pAnimator.ResetTrigger("Landed");
                pAnimator.SetTrigger("Jumped");
                isJumping = true;
                grounded = false;
                justJumped = true;
                pAnimator.SetBool("isOnFloor", false);
            }
        }

        pAnimator.SetBool("jumpButton", jumpHeld);
    }

    // Triggers falling animations based on vertical velocity
    private void HandleFallAnimation()
    {
        bool falling = (isJumping && rb.linearVelocity.y < 1f)
                    || (!grounded && rb.linearVelocity.y < -2f);

        if (falling)
        {
            pAnimator.SetTrigger("Falling");
            pAnimator.ResetTrigger("jumpWhenFalling");
            pAnimator.ResetTrigger("wallJumped");
            pAnimator.SetBool("isOnFloor", false);
            pAnimator.SetBool("isWalking", false);
            grounded = false;
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

    // Ground / wall state

    // true if the player is standing on something
    private bool IsOnGround => isGrounded || grounded;

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
        pAnimator.SetBool("onWall", isOnWall);
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
    }

    // Freeze / Revive helpers

    public void Freeze(bool freeze) => isDead = freeze;

    public void Revive() => isDead = false;

    // Collision & trigger callbacks

    void OnTriggerEnter(Collider collider)
    {
        if (isDead || collider.gameObject.layer == 10 || collider.gameObject.layer == 8)
            return;

        // Do not take fall damage or reset states if touching a Portal trigger
        if (collider.GetComponent<Portal>() != null || (portalable != null && portalable.IsInPortal))
        {
            willDie = false; // Instantly forgive fall damage
            return;
        }

        // Reset jump/fall animation state on landing in a trigger zone
        pAnimator.ResetTrigger("Jumped");
        pAnimator.ResetTrigger("Falling");
        pAnimator.ResetTrigger("Landed");
        pAnimator.ResetTrigger("wallJumped");
        isJumping = false;
        grounded = true;
        pAnimator.SetBool("isOnFloor", true);

        // Fall-damage death
        if (willDie && canDie && Time.time > warpImmunityTime)
            KillPlayer();
    }

    void OnTriggerStay(Collider collider)
    {
        if (collider.gameObject.layer == 6 && !isDead)
        {
            pAnimator.SetBool("isOnFloor", true);
            grounded = true;
        }
    }

    void OnTriggerExit(Collider collider)
    {
        if (isDead) return;
        pAnimator.SetBool("isOnFloor", false);
        pAnimator.ResetTrigger("Landed");
        grounded = false;
    }

    void OnCollisionEnter(Collision collision)
    {
        if ((collision.gameObject.CompareTag("Ground") || grounded) && !isDead)
        {
            isJumping = false;
            pAnimator.ResetTrigger("Jumped");
            pAnimator.ResetTrigger("Falling");
            pAnimator.SetTrigger("Landed");
        }
    }

    void OnCollisionStay(Collision collision)
    {
        isGrounded = collision.gameObject.CompareTag("Ground");
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = false;
            grounded = false;
        }
    }

    // Death

    private void KillPlayer()
    {
        isDead = true;

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
