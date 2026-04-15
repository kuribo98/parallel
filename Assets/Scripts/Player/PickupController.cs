using UnityEngine;
using UnityEngine.InputSystem;

// Attach this to the Player root object (the same GameObject that has playerScript).
//
// Setup in Inspector:
//   • InteractAction  – bind to whatever button you want (e.g. E / South button).
//   • HoldPoint       – create an empty child of your Camera (or player) a metre or so
//                       in front of the player and assign it here. The held object will
//                       snap to this transform every frame.
public class PickupController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputAction interactAction;

    [Header("Hold Settings")]
    [Tooltip("Empty Transform positioned in front of the camera where held objects sit.")]
    public Transform holdPoint;
    [Tooltip("How fast the held object lerps to the hold point (set to a large value for snappy).")]
    public float holdLerpSpeed = 20f;

    [Header("Detection")]
    [Tooltip("Checked every frame – only objects with an InteractableObject component count.")]
    public LayerMask interactableLayer = ~0;  // Everything by default; narrow this if needed

    // Runtime state
    private InteractableObject _heldObject = null;
    private InteractableObject _nearestObject = null;
    private PortalButton _nearestButton = null;
    private Collider _heldCollider = null;
    void OnEnable()  => interactAction.Enable();
    void OnDisable() => interactAction.Disable();

    void Update()
    {
        UpdateNearestObject();

        if (interactAction.WasPressedThisFrame())
            TogglePickup();
    }

    void FixedUpdate()
    {
        // Smoothly move the held object to the hold point every physics tick
        if (_heldObject != null && holdPoint != null)
        {
            // First, lerp rotation so bounds are in the right orientation
            _heldObject.transform.rotation = Quaternion.Slerp(
                _heldObject.transform.rotation,
                holdPoint.rotation,
                Time.fixedDeltaTime * holdLerpSpeed
            );

            // Offset the center so the object's near edge sits at the hold point
            Vector3 targetPosition = HoldCenterFromEdge();

            _heldObject.transform.position = Vector3.Lerp(
                _heldObject.transform.position,
                targetPosition,
                Time.fixedDeltaTime * holdLerpSpeed
            );
        }
    }

    // Returns the world position the held object's center should move to so that
    // its nearest horizontal edge is at holdPoint.position, and it is vertically
    // centered on the hold point.
    private Vector3 HoldCenterFromEdge()
    {
        if (_heldCollider == null) return holdPoint.position;

        Bounds bounds = _heldCollider.bounds;

        // Horizontal offset (XZ)
        // Use only the flat direction so the vertical axis isn't skewed.
        Vector3 flatAway = holdPoint.position - transform.position;
        flatAway.y = 0f;
        flatAway.Normalize();

        // Support of the AABB along the flat direction (per-axis absolute values).
        float horizontalExtent = Mathf.Abs(flatAway.x) * bounds.extents.x
                               + Mathf.Abs(flatAway.z) * bounds.extents.z;

        // Push the center back so the near horizontal edge sits at the hold point.
        Vector3 target = holdPoint.position + flatAway * horizontalExtent;

        // Vertical centering
        // Keep the object's center at the exact same Y as the hold point, regardless of the object's height.
        target.y = holdPoint.position.y;

        return target;
    }

    // Proximity scan

    // Every frame, find the closest InteractableObject within its own radius and toggle its prompt accordingly.
    private void UpdateNearestObject()
    {
        // Broad-phase: grab every collider within a generous bubble.
        // We use the individual object's interactionRadius as the definitive check.
        Collider[] hits = Physics.OverlapSphere(transform.position, 20f, interactableLayer);

        InteractableObject closest = null;
        PortalButton closestButton = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            InteractableObject io = col.GetComponentInParent<InteractableObject>();
            if (io != null && !io.IsHeld)
            {
                float ioDist = Vector3.Distance(transform.position, io.transform.position);
                if (ioDist <= io.interactionRadius && ioDist < closestDist)
                {
                    closestDist = ioDist;
                    closest = io;
                    closestButton = null;
                }
            }

            PortalButton button = col.GetComponentInParent<PortalButton>();
            if (button != null && button.CanPress)
            {
                float buttonDist = button.GetInteractionDistanceFrom(transform.position);
                if (buttonDist <= button.interactionRadius && buttonDist < closestDist)
                {
                    closestDist = buttonDist;
                    closest = null;
                    closestButton = button;
                }
            }
        }

        for (int i = 0; i < PortalButton.ActiveButtons.Count; ++i)
        {
            PortalButton button = PortalButton.ActiveButtons[i];
            if (button == null || !button.CanPress)
            {
                continue;
            }

            float buttonDist = button.GetInteractionDistanceFrom(transform.position);
            if (buttonDist <= button.interactionRadius && buttonDist < closestDist)
            {
                closestDist = buttonDist;
                closest = null;
                closestButton = button;
            }
        }

        // Hide the previous nearest if it changed
        if (_nearestObject != null && _nearestObject != closest)
            _nearestObject.SetPromptVisible(false);

        if (_nearestButton != null && _nearestButton != closestButton)
            _nearestButton.SetPromptVisible(false);

        _nearestObject = closest;
        _nearestButton = closestButton;

        if (_nearestObject != null)
            _nearestObject.SetPromptVisible(true);

        if (_nearestButton != null)
            _nearestButton.SetPromptVisible(true);
    }

    // Pickup / drop

    private void TogglePickup()
    {
        if (_heldObject != null)
        {
            Drop();
        }
        else if (_nearestButton != null)
        {
            _nearestButton.Press();
        }
        else if (_nearestObject != null && holdPoint != null)
        {
            PickUp(_nearestObject);
        }
    }

    private void PickUp(InteractableObject obj)
    {
        _heldObject = obj;
        _heldCollider = obj.GetComponentInChildren<Collider>();
        _heldObject.OnPickedUp(holdPoint);
    }

    private void Drop()
    {
        _heldObject.OnDropped();
        _heldObject = null;
        _heldCollider = null;
    }

    // Gizmos
    void OnDrawGizmosSelected()
    {
        if (holdPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(holdPoint.position, 0.1f);
        }
    }
}
