using System.Collections;
using System.Collections.Generic;
//using System.Numerics;
using UnityEngine;

public class wallDetector : MonoBehaviour
{
    public LayerMask playerMask;
    public float wallDistance;
    public float floorDistance;
    
    private Vector3 wallPosition = Vector3.zero;
    private Vector3 triggerPosition = Vector3.zero;
    private Vector3 wallDirection = Vector3.zero;
    
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if(Physics.Raycast(transform.position, transform.TransformDirection(UnityEngine.Vector3.down), out RaycastHit hit, floorDistance, ~playerMask))
        {
            if(hit.collider != null)
            {
                SendMessageUpwards("almostFloor");
                Debug.DrawRay(transform.position, transform.TransformDirection(UnityEngine.Vector3.down), Color.red);
            }
        }
    }

    void OnTriggerEnter(Collider collider)
    {
        if(collider.gameObject.layer != 10 && collider.gameObject.layer != 7 && collider.gameObject.layer != 8){
            SendMessageUpwards("IsOnWall", true);
        }
    }


    void OnTriggerStay(Collider collider)
    {
        /*
        wallPosition = collider.transform.position;
        triggerPosition = transform.position;
        wallDirection = (wallPosition - triggerPosition).normalized;
        SendMessageUpwards("SetWallDirection", wallDirection);
        */

        if(Physics.Raycast(transform.position, transform.TransformDirection(UnityEngine.Vector3.forward), out RaycastHit hit, wallDistance, ~playerMask))
        {
            if(hit.collider != null)
            {
                //Debug.Log(hit.normal);
                SendMessageUpwards("SetWallDirection", hit.normal);
                Vector3 reflectVec = Vector3.Reflect(hit.point - transform.position, hit.normal);
                Debug.DrawRay(transform.position, transform.TransformDirection(UnityEngine.Vector3.forward), Color.red);
            }
        }
    }


    void OnTriggerExit()
    {
        SendMessageUpwards("IsOnWall", false);
    }
}
