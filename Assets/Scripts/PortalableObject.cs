using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PortalableObject : MonoBehaviour
{
    private const int splineSampleCount = 64;

    private static readonly List<PortalableObject> activeObjects = new List<PortalableObject>();
    public static IReadOnlyList<PortalableObject> ActiveObjects => activeObjects;

    private GameObject cloneObject;
    private readonly Dictionary<Collider, int> portalIgnoredColliders = new Dictionary<Collider, int>();
    private readonly HashSet<Collider> temporarilyIgnoredColliders = new HashSet<Collider>();
    private Coroutine temporaryCollisionRoutine;
    private Coroutine splineFollowRoutine;
    private bool splineFollowWasKinematic;
    private CollisionDetectionMode splineFollowCollisionMode;
    private Vector3 splineFollowVelocity;

    protected int inPortalCount = 0;
    public bool IsInPortal => inPortalCount > 0;
    public bool IsSplineFollowing { get; private set; }
    public bool CanWarp => Time.time - lastWarpTime >= 0.05f;
    public Vector3 PreviousPosition { get; private set; }
    
    private Portal inPortal;
    private Portal outPortal;

    private new Rigidbody rigidbody;
    protected new Collider collider;
    public Collider WorldCollider => collider;

    protected float lastWarpTime;

    private static readonly Quaternion halfTurn = Quaternion.Euler(0.0f, 180.0f, 0.0f);

    protected virtual void Awake()
    {
        cloneObject = new GameObject();
        cloneObject.SetActive(false);
        var meshFilter = cloneObject.AddComponent<MeshFilter>();
        var meshRenderer = cloneObject.AddComponent<MeshRenderer>();

        meshFilter.mesh = GetComponent<MeshFilter>().mesh;
        meshRenderer.materials = GetComponent<MeshRenderer>().materials;
        cloneObject.transform.localScale = transform.localScale;

        rigidbody = GetComponent<Rigidbody>();
        collider = GetComponent<Collider>();
        PreviousPosition = transform.position;

        if (rigidbody.collisionDetectionMode == CollisionDetectionMode.Discrete)
        {
            rigidbody.collisionDetectionMode = rigidbody.isKinematic
                ? CollisionDetectionMode.ContinuousSpeculative
                : CollisionDetectionMode.ContinuousDynamic;
        }
    }

    private void OnEnable()
    {
        PreviousPosition = transform.position;

        if (!activeObjects.Contains(this))
        {
            activeObjects.Add(this);
        }
    }

    private void OnDisable()
    {
        StopSplineFollow(Vector3.zero, false);

        if (temporaryCollisionRoutine != null)
        {
            StopCoroutine(temporaryCollisionRoutine);
            temporaryCollisionRoutine = null;
        }

        RestoreTemporaryIgnoredCollisions();
        activeObjects.Remove(this);
    }

    private void LateUpdate()
    {
        if(inPortal == null || outPortal == null)
        {
            PreviousPosition = transform.position;
            return;
        }

        if(cloneObject.activeSelf && inPortal.IsPlaced && outPortal.IsPlaced)
        {
            var inTransform = inPortal.transform;
            var outTransform = outPortal.transform;

            // Update position of clone.
            bool centerOnExit = ShouldCenterOnExit(outPortal);
            cloneObject.transform.position = GetWarpedPosition(transform.position, inTransform, outTransform, centerOnExit);

            // Update rotation of clone.
            Quaternion relativeRot = Quaternion.Inverse(inTransform.rotation) * transform.rotation;
            relativeRot = halfTurn * relativeRot;
            cloneObject.transform.rotation = outTransform.rotation * relativeRot;
        }
        else
        {
            cloneObject.transform.position = new Vector3(-1000.0f, 1000.0f, -1000.0f);
        }

        PreviousPosition = transform.position;
    }

    public void SetIsInPortal(Portal inPortal, Portal outPortal, Collider wallCollider)
    {
        this.inPortal = inPortal;
        this.outPortal = outPortal;

        AddPortalIgnoredCollider(wallCollider);

        cloneObject.SetActive(true);

        ++inPortalCount;
    }

    public void PrepareWarp(Portal inPortal, Portal outPortal)
    {
        this.inPortal = inPortal;
        this.outPortal = outPortal;
    }

    public void ExitPortal(Collider wallCollider)
    {
        RemovePortalIgnoredCollider(wallCollider);

        --inPortalCount;

        if (inPortalCount == 0)
        {
            cloneObject.SetActive(false);
        }
    }

    public virtual void Warp()
    {
        if (Time.time - lastWarpTime < 0.05f) return;
        lastWarpTime = Time.time;

        var inTransform = inPortal.transform;
        var outTransform = outPortal.transform;
        bool centerOnExit = ShouldCenterOnExit(outPortal);
        Vector3 currentVelocity = IsSplineFollowing ? splineFollowVelocity : rigidbody.linearVelocity;

        if (IsSplineFollowing)
        {
            StopSplineFollow(Vector3.zero, false);
        }

        // Update position of object.
        transform.position = GetWarpedPosition(transform.position, inTransform, outTransform, centerOnExit);

        // Update rotation of object.
        Quaternion relativeRot = Quaternion.Inverse(inTransform.rotation) * transform.rotation;
        relativeRot = halfTurn * relativeRot;
        transform.rotation = outTransform.rotation * relativeRot;

        // Update velocity of rigidbody.
        Vector3 relativeVel = inTransform.InverseTransformDirection(currentVelocity);
        relativeVel = halfTurn * relativeVel;
        Vector3 warpedVelocity = outTransform.TransformDirection(relativeVel);
        Vector3 exitVelocity = centerOnExit
            ? -outTransform.forward * warpedVelocity.magnitude
            : warpedVelocity;
        Vector3 constrainedExitVelocity = outPortal.ConstrainExitVelocity(exitVelocity);
        rigidbody.linearVelocity = constrainedExitVelocity;

        if (ShouldApplyExitCollisionGrace(outPortal, centerOnExit))
        {
            outPortal.ApplyExitCollisionGrace(this);
        }

        outPortal.TryStartSplineExit(this, constrainedExitVelocity);

        // Swap portal references.
        var tmp = inPortal;
        inPortal = outPortal;
        outPortal = tmp;
    }

    protected virtual bool ShouldCenterOnExit(Portal portal)
    {
        return portal != null && portal.CenterExit;
    }

    protected virtual bool ShouldApplyExitCollisionGrace(Portal portal, bool centerOnExit)
    {
        return portal != null;
    }

    protected virtual void OnSplineFollowFinished(Vector3 exitVelocity)
    {
    }

    public void FollowSpline(
        SplineContainer splineContainer,
        int splineIndex,
        float speed,
        bool reverse,
        bool alignToSpline)
    {
        if (splineContainer == null || speed <= 0.0f || !isActiveAndEnabled)
        {
            return;
        }

        StopSplineFollow(Vector3.zero, false);
        splineFollowRoutine = StartCoroutine(FollowSplineRoutine(splineContainer, splineIndex, speed, reverse, alignToSpline));
    }

    private IEnumerator FollowSplineRoutine(
        SplineContainer splineContainer,
        int splineIndex,
        float speed,
        bool reverse,
        bool alignToSpline)
    {
        if (!IsValidSpline(splineContainer, splineIndex))
        {
            splineFollowRoutine = null;
            yield break;
        }

        float splineLength = EstimateSplineLength(splineContainer, splineIndex);
        if (splineLength <= 0.001f)
        {
            splineFollowRoutine = null;
            yield break;
        }

        float direction = reverse ? -1.0f : 1.0f;
        float startT = FindClosestSplineT(splineContainer, splineIndex, transform.position);
        float currentT = startT;
        float distanceTravelled = 0.0f;
        bool isClosed = splineContainer[splineIndex].Closed;

        splineFollowWasKinematic = rigidbody.isKinematic;
        splineFollowCollisionMode = rigidbody.collisionDetectionMode;
        IsSplineFollowing = true;

        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
        rigidbody.isKinematic = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        Vector3 finalVelocity = Vector3.zero;

        while (true)
        {
            if (!splineContainer.Evaluate(splineIndex, currentT, out var position, out var tangent, out var upVector))
            {
                break;
            }

            Vector3 worldPosition = ToVector3(position);
            Vector3 worldTangent = ToVector3(tangent) * direction;
            Vector3 worldUp = ToVector3(upVector);

            rigidbody.MovePosition(worldPosition);

            if (alignToSpline)
            {
                rigidbody.MoveRotation(GetSplineRotation(worldTangent, worldUp, rigidbody.rotation));
            }

            finalVelocity = worldTangent.sqrMagnitude > 0.0001f
                ? worldTangent.normalized * speed
                : splineFollowVelocity;
            splineFollowVelocity = finalVelocity;

            yield return new WaitForFixedUpdate();

            distanceTravelled += speed * Time.fixedDeltaTime * direction;
            currentT = startT + distanceTravelled / splineLength;

            if (isClosed)
            {
                currentT = Mathf.Repeat(currentT, 1.0f);
                continue;
            }

            if (currentT < 0.0f || currentT > 1.0f)
            {
                currentT = Mathf.Clamp01(currentT);

                if (splineContainer.Evaluate(splineIndex, currentT, out position, out tangent, out upVector))
                {
                    Vector3 endPosition = ToVector3(position);
                    Vector3 endTangent = ToVector3(tangent) * direction;

                    rigidbody.position = endPosition;
                    transform.position = endPosition;

                    if (alignToSpline)
                    {
                        Quaternion endRotation = GetSplineRotation(endTangent, ToVector3(upVector), rigidbody.rotation);
                        rigidbody.rotation = endRotation;
                        transform.rotation = endRotation;
                    }

                    if (endTangent.sqrMagnitude > 0.0001f)
                    {
                        finalVelocity = endTangent.normalized * speed;
                    }
                }

                break;
            }
        }

        splineFollowRoutine = null;
        RestoreSplineFollowState(finalVelocity, true);
        OnSplineFollowFinished(finalVelocity);
    }

    private void StopSplineFollow(Vector3 exitVelocity, bool applyVelocity)
    {
        if (splineFollowRoutine != null)
        {
            StopCoroutine(splineFollowRoutine);
            splineFollowRoutine = null;
        }

        RestoreSplineFollowState(exitVelocity, applyVelocity);
    }

    private void RestoreSplineFollowState(Vector3 exitVelocity, bool applyVelocity)
    {
        if (!IsSplineFollowing)
        {
            return;
        }

        rigidbody.isKinematic = splineFollowWasKinematic;
        rigidbody.collisionDetectionMode = splineFollowCollisionMode;

        if (applyVelocity)
        {
            rigidbody.linearVelocity = exitVelocity;
        }

        splineFollowVelocity = Vector3.zero;
        IsSplineFollowing = false;
    }

    private Vector3 GetWarpedPosition(Vector3 worldPosition, Transform inTransform, Transform outTransform, bool centerOnExit)
    {
        Vector3 relativePos = inTransform.InverseTransformPoint(worldPosition);
        relativePos = halfTurn * relativePos;

        if (centerOnExit)
        {
            relativePos.x = 0.0f;
            relativePos.y = 0.0f;
        }

        return outTransform.TransformPoint(relativePos);
    }

    private static bool IsValidSpline(SplineContainer splineContainer, int splineIndex)
    {
        return splineContainer != null &&
            splineIndex >= 0 &&
            splineIndex < splineContainer.Splines.Count &&
            splineContainer[splineIndex] != null;
    }

    private static float EstimateSplineLength(SplineContainer splineContainer, int splineIndex)
    {
        if (!splineContainer.Evaluate(splineIndex, 0.0f, out var previousPosition, out _, out _))
        {
            return 0.0f;
        }

        float length = 0.0f;
        Vector3 previous = ToVector3(previousPosition);

        for (int i = 1; i <= splineSampleCount; ++i)
        {
            float t = i / (float)splineSampleCount;

            if (!splineContainer.Evaluate(splineIndex, t, out var position, out _, out _))
            {
                continue;
            }

            Vector3 current = ToVector3(position);
            length += Vector3.Distance(previous, current);
            previous = current;
        }

        return length;
    }

    private static float FindClosestSplineT(SplineContainer splineContainer, int splineIndex, Vector3 worldPoint)
    {
        float closestT = 0.0f;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i <= splineSampleCount; ++i)
        {
            float t = i / (float)splineSampleCount;

            if (!splineContainer.Evaluate(splineIndex, t, out var position, out _, out _))
            {
                continue;
            }

            float distance = (ToVector3(position) - worldPoint).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestT = t;
            }
        }

        return closestT;
    }

    private static Quaternion GetSplineRotation(Vector3 tangent, Vector3 up, Quaternion fallback)
    {
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return fallback;
        }

        if (up.sqrMagnitude <= 0.0001f)
        {
            up = Vector3.up;
        }

        return Quaternion.LookRotation(tangent.normalized, up.normalized);
    }

    private static Vector3 ToVector3(float3 value)
    {
        return new Vector3(value.x, value.y, value.z);
    }

    private void AddPortalIgnoredCollider(Collider wallCollider)
    {
        if (wallCollider == null)
        {
            return;
        }

        portalIgnoredColliders.TryGetValue(wallCollider, out int ignoreCount);
        portalIgnoredColliders[wallCollider] = ignoreCount + 1;

        if (ignoreCount == 0)
        {
            Physics.IgnoreCollision(collider, wallCollider);
        }
    }

    private void RemovePortalIgnoredCollider(Collider wallCollider)
    {
        if (wallCollider == null || !portalIgnoredColliders.TryGetValue(wallCollider, out int ignoreCount))
        {
            return;
        }

        --ignoreCount;

        if (ignoreCount > 0)
        {
            portalIgnoredColliders[wallCollider] = ignoreCount;
            return;
        }

        portalIgnoredColliders.Remove(wallCollider);

        if (!temporarilyIgnoredColliders.Contains(wallCollider))
        {
            Physics.IgnoreCollision(collider, wallCollider, false);
        }
    }

    public void IgnoreNonTriggerCollisionsFor(float duration)
    {
        if (duration <= 0.0f || collider == null || !isActiveAndEnabled)
        {
            return;
        }

        if (temporaryCollisionRoutine != null)
        {
            StopCoroutine(temporaryCollisionRoutine);
            temporaryCollisionRoutine = null;
            RestoreTemporaryIgnoredCollisions();
        }

        temporaryCollisionRoutine = StartCoroutine(IgnoreNonTriggerCollisionsRoutine(duration));
    }

    private IEnumerator IgnoreNonTriggerCollisionsRoutine(float duration)
    {
        var sceneColliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);

        for (int i = 0; i < sceneColliders.Length; ++i)
        {
            var otherCollider = sceneColliders[i];

            if (!ShouldTemporarilyIgnoreCollision(otherCollider))
            {
                continue;
            }

            Physics.IgnoreCollision(collider, otherCollider);
            temporarilyIgnoredColliders.Add(otherCollider);
        }

        yield return new WaitForSeconds(duration);

        RestoreTemporaryIgnoredCollisions();
        temporaryCollisionRoutine = null;
    }

    private bool ShouldTemporarilyIgnoreCollision(Collider otherCollider)
    {
        if (otherCollider == null ||
            otherCollider == collider ||
            otherCollider.isTrigger ||
            !otherCollider.enabled ||
            !otherCollider.gameObject.activeInHierarchy)
        {
            return false;
        }

        return !otherCollider.transform.IsChildOf(transform) &&
            !transform.IsChildOf(otherCollider.transform);
    }

    private void RestoreTemporaryIgnoredCollisions()
    {
        foreach (var ignoredCollider in temporarilyIgnoredColliders)
        {
            if (ignoredCollider != null && !portalIgnoredColliders.ContainsKey(ignoredCollider))
            {
                Physics.IgnoreCollision(collider, ignoredCollider, false);
            }
        }

        temporarilyIgnoredColliders.Clear();
    }
}
