using UnityEngine;

public class PlayerPortalable : PortalableObject
{
    private CameraMove cameraMove;
    private playerScript playerMovement;

    protected override void Awake()
    {
        base.Awake();
        cameraMove = GetComponent<CameraMove>();
        playerMovement = GetComponent<playerScript>();
    }

    protected override bool ShouldCenterOnExit(Portal portal)
    {
        return base.ShouldCenterOnExit(portal) && (playerMovement == null || !playerMovement.HasMoveInput);
    }

    protected override bool ShouldApplyExitCollisionGrace(Portal portal, bool centerOnExit)
    {
        if (portal != null &&
            portal.KeepPlayerCollisionsWhenMoving &&
            portal.CenterExit &&
            !centerOnExit &&
            playerMovement != null &&
            playerMovement.HasMoveInput)
        {
            return false;
        }

        return base.ShouldApplyExitCollisionGrace(portal, centerOnExit);
    }

    protected override void OnSplineFollowFinished(Vector3 exitVelocity)
    {
        if (playerMovement != null)
        {
            playerMovement.SetWarpMomentum(exitVelocity);
        }

        if (cameraMove != null)
        {
            cameraMove.ResetTargetRotation();
        }
    }

    public override void Warp()
    {
        if (Time.time - lastWarpTime < 0.05f) return;
        base.Warp();
        
        if (playerMovement != null && !IsSplineFollowing)
        {
            playerMovement.SetWarpMomentum(GetComponent<Rigidbody>().linearVelocity);
        }

        Physics.SyncTransforms();

        if (cameraMove != null)
            cameraMove.ResetTargetRotation();
    }
}
