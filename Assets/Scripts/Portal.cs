using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(BoxCollider))]
public class Portal : MonoBehaviour
{
    [field: SerializeField]
    public Portal OtherPortal { get; private set; }

    [SerializeField]
    [Tooltip("If enabled, this portal starts disconnected even if Other Portal is assigned in the inspector.")]
    private bool startDisconnected = false;

    [SerializeField]
    [Tooltip("Material used while this portal is connected and ready to render a portal view. Defaults to the renderer's starting material.")]
    private Material connectedPortalMaterial;

    [SerializeField]
    [Tooltip("Material used while this portal is disconnected or its linked portal is unavailable. If empty, a dark runtime material is created.")]
    private Material disconnectedPortalMaterial;

    [Tooltip("Optional: renderer used for the portal outline color.")]
    [SerializeField]
    private Renderer outlineRenderer;

    [field: SerializeField]
    public Color PortalColour { get; private set; }

    [field: SerializeField]
    [field: Tooltip("If enabled, objects exiting this portal are centered on it and their exit velocity is forced along the portal's forward direction.")]
    public bool CenterExit { get; private set; }

    [field: SerializeField]
    [field: Tooltip("If enabled, objects exiting this portal cannot exceed the max exit speed.")]
    public bool LimitExitVelocity { get; private set; }

    [field: SerializeField]
    [field: Min(0.0f)]
    [field: Tooltip("Maximum speed for objects exiting this portal when velocity limiting is enabled.")]
    public float MaxExitVelocity { get; private set; } = 0.0f;

    [field: SerializeField]
    [field: Tooltip("If enabled, objects exiting this portal temporarily ignore non-trigger collisions.")]
    public bool DisableCollisionsAfterExit { get; private set; }

    [field: SerializeField]
    [field: Min(0.0f)]
    [field: Tooltip("How long objects ignore non-trigger collisions after exiting this portal.")]
    public float ExitCollisionDisableDuration { get; private set; } = 0.0f;

    [field: SerializeField]
    [field: Tooltip("If enabled with Center Exit and Disable Collisions After Exit, the player keeps collisions enabled when movement input cancels center exit.")]
    public bool KeepPlayerCollisionsWhenMoving { get; private set; }

    [field: Header("Spline Exit")]
    [field: SerializeField]
    [field: Tooltip("If enabled, fast objects exiting this portal are carried along the assigned spline.")]
    public bool UseSplineExit { get; private set; }

    [field: SerializeField]
    [field: Min(0.0f)]
    [field: Tooltip("Minimum exit speed required before this portal routes the object onto the spline.")]
    public float SplineExitMinimumVelocity { get; private set; } = 0.0f;

    [field: SerializeField]
    [field: Tooltip("Spline that objects follow after exiting this portal.")]
    public SplineContainer SplineExitSpline { get; private set; }

    [field: SerializeField]
    [field: Min(0)]
    [field: Tooltip("Which spline in the container should be used.")]
    public int SplineExitIndex { get; private set; } = 0;

    [field: SerializeField]
    [field: Min(0.0f)]
    [field: Tooltip("Optional fixed spline travel speed. Leave at 0 to use the object's current exit speed. Max exit velocity still applies if enabled.")]
    public float SplineExitVelocity { get; private set; } = 0.0f;

    [field: SerializeField]
    [field: Tooltip("If enabled, objects travel backward along the selected spline.")]
    public bool ReverseSplineExit { get; private set; }

    [field: SerializeField]
    [field: Tooltip("If enabled, objects rotate to face along the spline while they travel.")]
    public bool AlignToSplineExit { get; private set; }

    [SerializeField]
    private LayerMask placementMask;

    [SerializeField]
    private Transform testTransform;

    [Tooltip("If true, this portal is fixed in the scene and does not need to be shot onto a surface.")]
    [SerializeField]
    private bool prePlaced = false;

    [Tooltip("Optional: the wall/surface collider behind this portal. Objects inside the portal will ignore collisions with it.")]
    [SerializeField]
    private Collider prePlacedWallCollider = null;

    private List<PortalableObject> portalObjects = new List<PortalableObject>();
    public bool IsPlaced { get; private set; } = false;
    private Collider wallCollider;
    private Texture portalTexture;
    private Material connectedPortalMaterialInstance;
    private Material disconnectedPortalMaterialInstance;
    private bool showingConnectedVisual;
    private bool hasAppliedVisualState;

    // Components.
    public Renderer Renderer { get; private set; }
    private new BoxCollider collider;
    public bool IsConnected => OtherPortal != null;
    public bool CanRenderPortalView => IsPlaced && IsConnected && OtherPortal.IsPlaced;
    public bool CanTeleport => CanRenderPortalView;

    private void Awake()
    {
        collider = GetComponent<BoxCollider>();
        Renderer = GetComponent<Renderer>();

        if (connectedPortalMaterial == null && Renderer != null)
        {
            connectedPortalMaterial = Renderer.sharedMaterial;
        }

        connectedPortalMaterialInstance = connectedPortalMaterial != null
            ? new Material(connectedPortalMaterial)
            : null;
        disconnectedPortalMaterialInstance = disconnectedPortalMaterial != null
            ? new Material(disconnectedPortalMaterial)
            : CreateRuntimeDisconnectedMaterial();

        if (startDisconnected)
        {
            if (OtherPortal != null && OtherPortal.OtherPortal == this)
            {
                OtherPortal.OtherPortal = null;
            }

            OtherPortal = null;
        }
    }

    private void Start()
    {
        if (outlineRenderer != null)
        {
            outlineRenderer.material.SetColor("_OutlineColour", PortalColour);
        }

        if (prePlaced)
        {
            wallCollider = ResolvePrePlacedWallCollider();
            IsPlaced = true;
            // gameObject stays active. Portal is already positioned in the scene
        }
        else
        {
            gameObject.SetActive(false);
        }

        if (!startDisconnected && OtherPortal != null && OtherPortal.OtherPortal != this)
        {
            ConnectTo(OtherPortal);
        }

        ApplyPortalVisualState(true);
    }

    private void Update()
    {
        ApplyPortalVisualState();

        if (!CanTeleport)
        {
            return;
        }

        for (int i = 0; i < portalObjects.Count; ++i)
        {
            Vector3 objPos = transform.InverseTransformPoint(portalObjects[i].transform.position);

            if (objPos.z > 0.0f)
            {
                portalObjects[i].Warp();
            }
        }

        CheckFastMovingObjects();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanTeleport)
        {
            return;
        }

        var obj = other.GetComponent<PortalableObject>();
        if (obj != null)
        {
            portalObjects.Add(obj);
            obj.SetIsInPortal(this, OtherPortal, wallCollider);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var obj = other.GetComponent<PortalableObject>();

        if(portalObjects.Contains(obj))
        {
            portalObjects.Remove(obj);
            obj.ExitPortal(wallCollider);
        }
    }

    public bool PlacePortal(Collider wallCollider, Vector3 pos, Quaternion rot)
    {
        testTransform.position = pos;
        testTransform.rotation = rot;
        testTransform.position -= testTransform.forward * 0.001f;

        FixOverhangs();
        FixIntersects();

        if (CheckOverlap())
        {
            this.wallCollider = wallCollider;
            transform.position = testTransform.position;
            transform.rotation = testTransform.rotation;

            gameObject.SetActive(true);
            IsPlaced = true;
            ApplyPortalVisualState(true);
            return true;
        }

        return false;
    }

    private Collider ResolvePrePlacedWallCollider()
    {
        if (prePlacedWallCollider != null)
        {
            return prePlacedWallCollider;
        }

        var parentCollider = GetComponentInParent<Collider>();
        if (parentCollider != null && parentCollider != collider)
        {
            return parentCollider;
        }

        var rayOrigin = transform.position - transform.forward * 0.5f;
        if (Physics.Raycast(rayOrigin, transform.forward, out var hit, 2.0f, placementMask, QueryTriggerInteraction.Ignore))
        {
            return hit.collider;
        }

        return null;
    }

    private void CheckFastMovingObjects()
    {
        if (!CanTeleport)
        {
            return;
        }

        var portalCenter = collider.center;
        var halfWidth = Mathf.Abs(collider.size.x) * 0.5f;
        var halfHeight = Mathf.Abs(collider.size.y) * 0.5f;
        var scaleX = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.0001f);
        var scaleY = Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.0001f);

        for (int i = 0; i < PortalableObject.ActiveObjects.Count; ++i)
        {
            var obj = PortalableObject.ActiveObjects[i];

            if (obj == null || portalObjects.Contains(obj) || !obj.CanWarp)
            {
                continue;
            }

            Vector3 prevLocal = transform.InverseTransformPoint(obj.PreviousPosition);
            Vector3 currLocal = transform.InverseTransformPoint(obj.transform.position);

            if (prevLocal.z > 0.0f || currLocal.z <= 0.0f)
            {
                continue;
            }

            float denominator = prevLocal.z - currLocal.z;
            if (Mathf.Approximately(denominator, 0.0f))
            {
                continue;
            }

            float t = prevLocal.z / denominator;
            Vector3 crossingLocal = Vector3.Lerp(prevLocal, currLocal, t);

            Vector3 colliderExtents = obj.WorldCollider.bounds.extents;
            float paddingX =
                Mathf.Abs(transform.right.x) * colliderExtents.x +
                Mathf.Abs(transform.right.y) * colliderExtents.y +
                Mathf.Abs(transform.right.z) * colliderExtents.z;
            float paddingY =
                Mathf.Abs(transform.up.x) * colliderExtents.x +
                Mathf.Abs(transform.up.y) * colliderExtents.y +
                Mathf.Abs(transform.up.z) * colliderExtents.z;

            paddingX /= scaleX;
            paddingY /= scaleY;

            if (Mathf.Abs(crossingLocal.x - portalCenter.x) > halfWidth + paddingX ||
                Mathf.Abs(crossingLocal.y - portalCenter.y) > halfHeight + paddingY)
            {
                continue;
            }

            obj.PrepareWarp(this, OtherPortal);
            obj.Warp();
        }
    }

    public Vector3 ConstrainExitVelocity(Vector3 velocity)
    {
        if (!LimitExitVelocity)
        {
            return velocity;
        }

        float maxSpeed = Mathf.Max(0.0f, MaxExitVelocity);
        if (velocity.sqrMagnitude <= maxSpeed * maxSpeed)
        {
            return velocity;
        }

        if (maxSpeed == 0.0f)
        {
            return Vector3.zero;
        }

        return velocity.normalized * maxSpeed;
    }

    public void ApplyExitCollisionGrace(PortalableObject obj)
    {
        if (!DisableCollisionsAfterExit || ExitCollisionDisableDuration <= 0.0f || obj == null)
        {
            return;
        }

        obj.IgnoreNonTriggerCollisionsFor(ExitCollisionDisableDuration);
    }

    public void SetPortalTexture(Texture texture)
    {
        portalTexture = texture;
        ApplyPortalVisualState(true);
    }

    public void SetOtherPortal(Portal portal)
    {
        ConnectTo(portal);
    }

    public void ConnectTo(Portal portal)
    {
        if (portal == this)
        {
            Debug.LogWarning($"{name} cannot connect to itself.", this);
            return;
        }

        if (portal == null)
        {
            Disconnect();
            return;
        }

        if (OtherPortal != portal)
        {
            Disconnect();
        }

        if (portal.OtherPortal != this)
        {
            portal.Disconnect();
        }

        OtherPortal = portal;
        portal.OtherPortal = this;

        ApplyPortalVisualState(true);
        portal.ApplyPortalVisualState(true);
    }

    public void Disconnect()
    {
        var previousPortal = OtherPortal;
        OtherPortal = null;
        ApplyPortalVisualState(true);

        if (previousPortal != null && previousPortal.OtherPortal == this)
        {
            previousPortal.OtherPortal = null;
            previousPortal.ApplyPortalVisualState(true);
        }
    }

    public bool TryStartSplineExit(PortalableObject obj, Vector3 exitVelocity)
    {
        if (!UseSplineExit || obj == null || SplineExitSpline == null)
        {
            return false;
        }

        float exitSpeed = exitVelocity.magnitude;
        if (exitSpeed < SplineExitMinimumVelocity)
        {
            return false;
        }

        int splineCount = SplineExitSpline.Splines.Count;
        if (splineCount == 0)
        {
            return false;
        }

        float travelSpeed = SplineExitVelocity > 0.0f ? SplineExitVelocity : exitSpeed;
        if (LimitExitVelocity)
        {
            travelSpeed = Mathf.Min(travelSpeed, Mathf.Max(0.0f, MaxExitVelocity));
        }

        if (travelSpeed <= 0.0f)
        {
            return false;
        }

        int splineIndex = Mathf.Clamp(SplineExitIndex, 0, splineCount - 1);
        obj.FollowSpline(SplineExitSpline, splineIndex, travelSpeed, ReverseSplineExit, AlignToSplineExit);
        return true;
    }

    private void ApplyPortalVisualState(bool force = false)
    {
        if (Renderer == null)
        {
            return;
        }

        bool shouldShowConnectedVisual = CanRenderPortalView;
        Renderer.enabled = IsPlaced;

        if (!force && hasAppliedVisualState && showingConnectedVisual == shouldShowConnectedVisual)
        {
            return;
        }

        showingConnectedVisual = shouldShowConnectedVisual;
        hasAppliedVisualState = true;

        Material nextMaterial = shouldShowConnectedVisual
            ? connectedPortalMaterialInstance
            : disconnectedPortalMaterialInstance;

        if (nextMaterial != null)
        {
            Renderer.sharedMaterial = nextMaterial;
        }

        if (shouldShowConnectedVisual && portalTexture != null && Renderer.sharedMaterial != null)
        {
            Renderer.sharedMaterial.mainTexture = portalTexture;
        }
    }

    private static Material CreateRuntimeDisconnectedMaterial()
    {
        Shader shader = Shader.Find("Portals/DisabledPortal");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "Runtime Disabled Portal"
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", new Color(0.01f, 0.015f, 0.025f, 1.0f));
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", new Color(0.01f, 0.015f, 0.025f, 1.0f));
        }

        if (material.HasProperty("_GlowColor"))
        {
            material.SetColor("_GlowColor", new Color(0.05f, 0.25f, 0.35f, 1.0f));
        }

        return material;
    }

    // Ensure the portal cannot extend past the edge of a surface.
    private void FixOverhangs()
    {
        var testPoints = new List<Vector3>
        {
            new Vector3(-1.1f,  0.0f, 0.1f),
            new Vector3( 1.1f,  0.0f, 0.1f),
            new Vector3( 0.0f, -2.1f, 0.1f),
            new Vector3( 0.0f,  2.1f, 0.1f)
        };

        var testDirs = new List<Vector3>
        {
             Vector3.right,
            -Vector3.right,
             Vector3.up,
            -Vector3.up
        };

        for(int i = 0; i < 4; ++i)
        {
            RaycastHit hit;
            Vector3 raycastPos = testTransform.TransformPoint(testPoints[i]);
            Vector3 raycastDir = testTransform.TransformDirection(testDirs[i]);

            if(Physics.CheckSphere(raycastPos, 0.05f, placementMask))
            {
                break;
            }
            else if(Physics.Raycast(raycastPos, raycastDir, out hit, 2.1f, placementMask))
            {
                var offset = hit.point - raycastPos;
                testTransform.Translate(offset, Space.World);
            }
        }
    }

    // Ensure the portal cannot intersect a section of wall.
    private void FixIntersects()
    {
        var testDirs = new List<Vector3>
        {
             Vector3.right,
            -Vector3.right,
             Vector3.up,
            -Vector3.up
        };

        var testDists = new List<float> { 1.1f, 1.1f, 2.1f, 2.1f };

        for (int i = 0; i < 4; ++i)
        {
            RaycastHit hit;
            Vector3 raycastPos = testTransform.TransformPoint(0.0f, 0.0f, -0.1f);
            Vector3 raycastDir = testTransform.TransformDirection(testDirs[i]);

            if (Physics.Raycast(raycastPos, raycastDir, out hit, testDists[i], placementMask))
            {
                var offset = (hit.point - raycastPos);
                var newOffset = -raycastDir * (testDists[i] - offset.magnitude);
                testTransform.Translate(newOffset, Space.World);
            }
        }
    }

    // Once positioning has taken place, ensure the portal isn't intersecting anything.
    private bool CheckOverlap()
    {
        var checkExtents = new Vector3(0.9f, 1.9f, 0.05f);

        var checkPositions = new Vector3[]
        {
            testTransform.position + testTransform.TransformVector(new Vector3( 0.0f,  0.0f, -0.1f)),

            testTransform.position + testTransform.TransformVector(new Vector3(-1.0f, -2.0f, -0.1f)),
            testTransform.position + testTransform.TransformVector(new Vector3(-1.0f,  2.0f, -0.1f)),
            testTransform.position + testTransform.TransformVector(new Vector3( 1.0f, -2.0f, -0.1f)),
            testTransform.position + testTransform.TransformVector(new Vector3( 1.0f,  2.0f, -0.1f)),

            testTransform.TransformVector(new Vector3(0.0f, 0.0f, 0.2f))
        };

        // Ensure the portal does not intersect walls.
        var intersections = Physics.OverlapBox(checkPositions[0], checkExtents, testTransform.rotation, placementMask);

        if(intersections.Length > 1)
        {
            return false;
        }
        else if(intersections.Length == 1) 
        {
            // We are allowed to intersect the old portal position.
            if (intersections[0] != collider)
            {
                return false;
            }
        }

        // Ensure the portal corners overlap a surface.
        bool isOverlapping = true;

        for(int i = 1; i < checkPositions.Length - 1; ++i)
        {
            isOverlapping &= Physics.Linecast(checkPositions[i], 
                checkPositions[i] + checkPositions[checkPositions.Length - 1], placementMask);
        }

        return isOverlapping;
    }

    public void RemovePortal()
    {
        gameObject.SetActive(false);
        IsPlaced = false;
        ApplyPortalVisualState(true);
    }

    private void OnDestroy()
    {
        if (connectedPortalMaterialInstance != null)
        {
            Destroy(connectedPortalMaterialInstance);
        }

        if (disconnectedPortalMaterialInstance != null)
        {
            Destroy(disconnectedPortalMaterialInstance);
        }
    }
}
