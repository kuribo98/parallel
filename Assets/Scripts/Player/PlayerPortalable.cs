using UnityEngine;

public class PlayerPortalable : PortalableObject
{
    private CameraMove cameraMove;

    protected override void Awake()
    {
        base.Awake();
        cameraMove = GetComponent<CameraMove>();
    }

    public override void Warp()
    {
        if (Time.time - lastWarpTime < 0.05f) return;
        base.Warp();
        
        var pScript = GetComponent<playerScript>();
        if (pScript != null)
        {
            pScript.SetWarpMomentum(GetComponent<Rigidbody>().linearVelocity);
        }

        Physics.SyncTransforms();

        if (cameraMove != null)
            cameraMove.ResetTargetRotation();
    }
}
