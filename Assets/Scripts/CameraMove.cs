using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public class CameraMove : MonoBehaviour
{
    private const float moveSpeed = 7.5f;
    private const float cameraSpeed = 3.0f;
    private static Material swirlySkyboxMaterial;

    public Quaternion TargetRotation { private set; get; }
    
    private Vector3 moveVector = Vector3.zero;
    private float moveY = 0.0f;

    private new Rigidbody rigidbody;

    [Header("Skybox")]
    [SerializeField]
    private bool applySwirlySkybox = true;

    private void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        Cursor.lockState = CursorLockMode.Locked;

        ApplySwirlySkybox();
        TargetRotation = transform.rotation;
    }

    private void Update()
    {
        // Rotate the camera.
        var rotation = new Vector2(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"));
        var targetEuler = TargetRotation.eulerAngles + (Vector3)rotation * cameraSpeed;
        if(targetEuler.x > 180.0f)
        {
            targetEuler.x -= 360.0f;
        }
        targetEuler.x = Mathf.Clamp(targetEuler.x, -75.0f, 75.0f);
        TargetRotation = Quaternion.Euler(targetEuler);

        transform.rotation = Quaternion.Slerp(transform.rotation, TargetRotation, 
            Time.deltaTime * 15.0f);
    }

    public void ResetTargetRotation()
    {
        TargetRotation = Quaternion.LookRotation(transform.forward, Vector3.up);
    }

    private void ApplySwirlySkybox()
    {
        if (!applySwirlySkybox || SceneManager.GetActiveScene().name != "Physics")
        {
            return;
        }

        if (swirlySkyboxMaterial == null)
        {
            var skyboxTemplate = Resources.Load<Material>("DistantCloudSkybox");
            if (skyboxTemplate != null)
            {
                swirlySkyboxMaterial = new Material(skyboxTemplate)
                {
                    name = "Runtime Swirly Cloud Skybox"
                };
            }
            else
            {
                var shader = Shader.Find("Skybox/DistantCloudVolume");
                if (shader == null)
                {
                    Debug.LogWarning("Swirly cloud skybox shader could not be found.");
                    return;
                }

                swirlySkyboxMaterial = new Material(shader)
                {
                    name = "Runtime Swirly Cloud Skybox"
                };

                swirlySkyboxMaterial.SetColor("_VoidColor", Color.black);
                swirlySkyboxMaterial.SetColor("_HorizonColor", new Color(0.02f, 0.03f, 0.08f));
                swirlySkyboxMaterial.SetColor("_BlueCloudColor", new Color(0.12f, 0.39f, 0.95f));
                swirlySkyboxMaterial.SetColor("_PurpleCloudColor", new Color(0.5f, 0.24f, 0.92f));
                swirlySkyboxMaterial.SetColor("_HighlightColor", new Color(0.82f, 0.9f, 1.0f));
                swirlySkyboxMaterial.SetFloat("_Exposure", 1.45f);
                swirlySkyboxMaterial.SetFloat("_CloudScale", 1.55f);
                swirlySkyboxMaterial.SetFloat("_CloudDepth", 2.6f);
                swirlySkyboxMaterial.SetFloat("_DensityThreshold", 0.53f);
                swirlySkyboxMaterial.SetFloat("_DensityStrength", 1.15f);
                swirlySkyboxMaterial.SetFloat("_Softness", 0.12f);
                swirlySkyboxMaterial.SetFloat("_WarpScale", 1.55f);
                swirlySkyboxMaterial.SetFloat("_WarpStrength", 0.72f);
                swirlySkyboxMaterial.SetFloat("_PrimarySpeed", 0.02f);
                swirlySkyboxMaterial.SetFloat("_SecondarySpeed", 0.032f);
                swirlySkyboxMaterial.SetFloat("_HorizonGlow", 0.08f);
            }
        }

        if (RenderSettings.skybox != swirlySkyboxMaterial)
        {
            RenderSettings.skybox = swirlySkyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }
    }
}
