using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using RenderPipeline = UnityEngine.Rendering.RenderPipelineManager;

public class PortalCamera : MonoBehaviour
{
    [SerializeField]
    private Portal[] portals = new Portal[2];

    [SerializeField]
    private Camera portalCamera;

    [SerializeField]
    private int iterations = 7;

    private RenderTexture[] portalTextures;

    private Camera mainCamera;
    private bool isRenderingPortals;

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();

        if (portalCamera != null)
        {
            // This camera is rendered manually by this script. If Unity renders it normally too,
            // beginCameraRendering can re-enter and produce invalid viewport/frustum warnings.
            portalCamera.enabled = false;
        }

        portalTextures = new RenderTexture[portals.Length];
    }

    private void Start()
    {
        for (int i = 0; i < portals.Length && i < portalTextures.Length; ++i)
        {
            if (portals[i] != null)
            {
                portals[i].SetPortalTexture(portalTextures[i]);
            }
        }
    }

    private void OnEnable()
    {
        RenderPipeline.beginCameraRendering += UpdateCamera;
    }

    private void OnDisable()
    {
        RenderPipeline.beginCameraRendering -= UpdateCamera;
    }

    private void OnDestroy()
    {
        ReleasePortalTextures();
    }

    void UpdateCamera(ScriptableRenderContext SRC, Camera camera)
    {
        if (isRenderingPortals || camera != mainCamera || portalCamera == null)
        {
            return;
        }

        EnsurePortalTextures();
        isRenderingPortals = true;

        try
        {
            for (int i = 0; i < portals.Length && i < portalTextures.Length; ++i)
            {
                var inPortal = portals[i];
                if (inPortal == null || !inPortal.CanRenderPortalView || !inPortal.Renderer.isVisible)
                {
                    continue;
                }

                portalCamera.targetTexture = portalTextures[i];
                for (int iteration = iterations - 1; iteration >= 0; --iteration)
                {
                    RenderCamera(inPortal, inPortal.OtherPortal, iteration, SRC);
                }
            }
        }
        finally
        {
            portalCamera.targetTexture = null;
            isRenderingPortals = false;
        }
    }

    private void RenderCamera(Portal inPortal, Portal outPortal, int iterationID, ScriptableRenderContext SRC)
    {
        Transform inTransform = inPortal.transform;
        Transform outTransform = outPortal.transform;

        Transform cameraTransform = portalCamera.transform;
        cameraTransform.position = transform.position;
        cameraTransform.rotation = transform.rotation;
        portalCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
        portalCamera.ResetProjectionMatrix();

        for(int i = 0; i <= iterationID; ++i)
        {
            // Position the camera behind the other portal.
            Vector3 relativePos = inTransform.InverseTransformPoint(cameraTransform.position);
            relativePos = Quaternion.Euler(0.0f, 180.0f, 0.0f) * relativePos;
            cameraTransform.position = outTransform.TransformPoint(relativePos);

            // Rotate the camera to look through the other portal.
            Quaternion relativeRot = Quaternion.Inverse(inTransform.rotation) * cameraTransform.rotation;
            relativeRot = Quaternion.Euler(0.0f, 180.0f, 0.0f) * relativeRot;
            cameraTransform.rotation = outTransform.rotation * relativeRot;
        }

        // Set the camera's oblique view frustum.
        Plane p = new Plane(-outTransform.forward, outTransform.position);
        Vector4 clipPlaneWorldSpace = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
        Vector4 clipPlaneCameraSpace =
            Matrix4x4.Transpose(Matrix4x4.Inverse(portalCamera.worldToCameraMatrix)) * clipPlaneWorldSpace;

        var newMatrix = portalCamera.CalculateObliqueMatrix(clipPlaneCameraSpace);
        portalCamera.projectionMatrix = newMatrix;

        // Render the camera to its render target.
        UniversalRenderPipeline.RenderSingleCamera(SRC, portalCamera);
    }

    private void EnsurePortalTextures()
    {
        if (portalTextures == null || portalTextures.Length != portals.Length)
        {
            ReleasePortalTextures();
            portalTextures = new RenderTexture[portals.Length];
        }

        int width = Mathf.Max(1, mainCamera != null ? mainCamera.pixelWidth : Screen.width);
        int height = Mathf.Max(1, mainCamera != null ? mainCamera.pixelHeight : Screen.height);

        for (int i = 0; i < portalTextures.Length; ++i)
        {
            RenderTexture texture = portalTextures[i];
            if (texture != null && texture.width == width && texture.height == height)
            {
                continue;
            }

            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }

            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = $"Portal Texture {i}"
            };
            portalTextures[i] = texture;

            if (i < portals.Length && portals[i] != null)
            {
                portals[i].SetPortalTexture(texture);
            }
        }
    }

    private void ReleasePortalTextures()
    {
        if (portalTextures == null)
        {
            return;
        }

        for (int i = 0; i < portalTextures.Length; ++i)
        {
            if (portalTextures[i] != null)
            {
                portalTextures[i].Release();
                Destroy(portalTextures[i]);
                portalTextures[i] = null;
            }
        }
    }
}
