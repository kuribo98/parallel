using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class PortalButton : MonoBehaviour
{
    private static readonly List<PortalButton> activeButtons = new List<PortalButton>();
    public static IReadOnlyList<PortalButton> ActiveButtons => activeButtons;

    public enum ActivationMode
    {
        ManualOnly,
        TriggerEnter,
        CollisionEnter
    }

    public enum ButtonActionType
    {
        SpawnPrefab,
        ConnectPortals,
        DisconnectPortal,
        UnityEvent
    }

    public enum ButtonActionResult
    {
        None,
        Completed,
        RefusedSpawnLimit
    }

    private struct ActionRunSummary
    {
        public bool CompletedAction;
        public bool RefusedSpawn;
    }

    [System.Serializable]
    public class ButtonAction
    {
        [SerializeField]
        private ButtonActionType actionType = ButtonActionType.UnityEvent;

        [Header("Spawn Prefab")]
        [SerializeField]
        private GameObject prefab;

        [SerializeField]
        private Transform spawnPoint;

        [SerializeField]
        private bool parentToSpawnPoint;

        [Header("Spawn Limits")]
        [SerializeField]
        [Tooltip("If enabled, this action will not spawn when the scene already contains the max number of matching prefab instances.")]
        private bool limitInstancesInScene;

        [SerializeField]
        [Min(0)]
        private int maxInstancesInScene = 1;

        [SerializeField]
        [Tooltip("Also count scene objects whose root GameObject name matches the prefab name or prefab clone name.")]
        private bool countExistingObjectsByPrefabName = true;

        [Header("Spawned Prefab Height Despawn")]
        [SerializeField]
        [Tooltip("If enabled, spawned prefabs from this action are destroyed when they go below the minimum height.")]
        private bool despawnSpawnedPrefabBelowHeight;

        [SerializeField]
        private float spawnedPrefabMinimumHeight = -50.0f;

        [SerializeField]
        [Tooltip("If enabled, spawned prefabs from this action are destroyed when they go above the maximum height.")]
        private bool despawnSpawnedPrefabAboveHeight;

        [SerializeField]
        private float spawnedPrefabMaximumHeight = 100.0f;

        [Header("Portal Connection")]
        [SerializeField]
        private Portal portal;

        [SerializeField]
        private Portal otherPortal;

        [SerializeField]
        [Tooltip("If enabled, a portal connection made by this action reverts after the delay.")]
        private bool revertConnectionAfterDelay;

        [SerializeField]
        [Min(0.0f)]
        private float connectionRevertDelay = 5.0f;

        [SerializeField]
        [Tooltip("If enabled, this button's indicator lights turn off when the portal connection reverts.")]
        private bool turnIndicatorsOffOnRevert = true;

        [Header("Custom Event")]
        [SerializeField]
        private UnityEvent unityEvent;

        public ButtonActionResult Invoke(PortalButton context)
        {
            switch (actionType)
            {
                case ButtonActionType.SpawnPrefab:
                    return Spawn(context);
                case ButtonActionType.ConnectPortals:
                    if (portal != null)
                    {
                        Portal previousPortalConnection = portal.OtherPortal;
                        Portal previousOtherPortalConnection = otherPortal != null ? otherPortal.OtherPortal : null;
                        portal.ConnectTo(otherPortal);

                        if (revertConnectionAfterDelay && connectionRevertDelay > 0.0f && otherPortal != null)
                        {
                            context.StartCoroutine(context.RevertPortalConnectionRoutine(
                                portal,
                                otherPortal,
                                previousPortalConnection,
                                previousOtherPortalConnection,
                                connectionRevertDelay,
                                turnIndicatorsOffOnRevert));
                        }
                    }
                    return ButtonActionResult.Completed;
                case ButtonActionType.DisconnectPortal:
                    if (portal != null)
                    {
                        portal.Disconnect();
                    }
                    return ButtonActionResult.Completed;
                case ButtonActionType.UnityEvent:
                    unityEvent?.Invoke();
                    return ButtonActionResult.Completed;
            }

            return ButtonActionResult.None;
        }

        private ButtonActionResult Spawn(Component context)
        {
            if (prefab == null)
            {
                return ButtonActionResult.None;
            }

            if (limitInstancesInScene && CountMatchingPrefabInstances() >= maxInstancesInScene)
            {
                return ButtonActionResult.RefusedSpawnLimit;
            }

            Transform targetSpawnPoint = spawnPoint != null ? spawnPoint : context.transform;
            var spawnedObject = Object.Instantiate(prefab, targetSpawnPoint.position, targetSpawnPoint.rotation);
            GetOrAddComponent<SpawnedPrefabInstance>(spawnedObject).Initialize(prefab);
            ConfigureHeightDespawn(spawnedObject);

            if (parentToSpawnPoint)
            {
                spawnedObject.transform.SetParent(targetSpawnPoint);
            }

            return ButtonActionResult.Completed;
        }

        private int CountMatchingPrefabInstances()
        {
            int count = 0;
            var spawnedInstances = Object.FindObjectsByType<SpawnedPrefabInstance>(FindObjectsSortMode.None);

            for (int i = 0; i < spawnedInstances.Length; ++i)
            {
                if (spawnedInstances[i] != null && spawnedInstances[i].SourcePrefab == prefab)
                {
                    ++count;
                }
            }

            if (!countExistingObjectsByPrefabName)
            {
                return count;
            }

            string prefabName = prefab.name;
            string cloneName = $"{prefabName}(Clone)";
            var sceneTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);

            for (int i = 0; i < sceneTransforms.Length; ++i)
            {
                Transform sceneTransform = sceneTransforms[i];
                if (sceneTransform == null || sceneTransform.GetComponent<SpawnedPrefabInstance>() != null)
                {
                    continue;
                }

                string objectName = sceneTransform.gameObject.name;
                if (objectName == prefabName || objectName == cloneName)
                {
                    ++count;
                }
            }

            return count;
        }

        private void ConfigureHeightDespawn(GameObject spawnedObject)
        {
            if (!despawnSpawnedPrefabBelowHeight && !despawnSpawnedPrefabAboveHeight)
            {
                return;
            }

            var despawnBounds = GetOrAddComponent<DespawnOnHeightBounds>(spawnedObject);
            despawnBounds.Configure(
                despawnSpawnedPrefabBelowHeight,
                spawnedPrefabMinimumHeight,
                despawnSpawnedPrefabAboveHeight,
                spawnedPrefabMaximumHeight);
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (!target.TryGetComponent(out T component))
            {
                component = target.AddComponent<T>();
            }

            return component;
        }
    }

    [Header("Activation")]
    [SerializeField]
    private ActivationMode activationMode = ActivationMode.ManualOnly;

    [SerializeField]
    [Tooltip("Optional tag filter for trigger/collision activation. Leave empty to allow anything.")]
    private string requiredActivatorTag = string.Empty;

    [SerializeField]
    [Tooltip("If disabled, the button can only complete once.")]
    private bool canRepeat;

    [SerializeField]
    [Tooltip("If enabled, repeated presses reset all indicator lights before starting again.")]
    private bool resetLightsWhenRepeated = true;

    [Header("Interaction Settings")]
    [Tooltip("How close (metres) the player must be for the prompt to appear.")]
    public float interactionRadius = 3.0f;

    [Tooltip("Optional point to measure range from. If empty, this button object's transform is used.")]
    public Transform interactionPoint;

    [Tooltip("If enabled, range ignores vertical distance and only compares X/Z positions.")]
    public bool useHorizontalDistance = true;

    [Header("Prompt UI")]
    [Tooltip("Optional prompt sprite shown over the button while it can be pressed.")]
    public Sprite promptSprite;

    public Vector2 promptSize = new Vector2(256.0f, 64.0f);

    public Vector2 promptOffset = new Vector2(0.0f, 60.0f);

    [Tooltip("Camera used to project the prompt onto the screen. Falls back to Camera.main if empty.")]
    public Camera promptCamera;

    [Header("Indicator Lights")]
    [SerializeField]
    [Tooltip("Optional light objects. The first Renderer found on each object is tinted in sequence.")]
    private GameObject[] indicatorLightObjects;

    [SerializeField]
    [Min(0.0f)]
    private float secondsBetweenLights = 0.25f;

    [SerializeField]
    private Color redColor = Color.red;

    [SerializeField]
    [FormerlySerializedAs("onColor")]
    private Color greenColor = Color.green;

    [SerializeField]
    private bool setOffColorOnAwake;

    [SerializeField]
    private Color offColor = Color.black;

    [SerializeField]
    [Tooltip("Also writes the color to emission properties when the light material supports them.")]
    private bool setEmission = true;

    [SerializeField]
    [Min(0.0f)]
    private float emissionIntensity = 1.5f;

    [Header("Audio")]
    [SerializeField]
    private AudioSource audioSource;

    [SerializeField]
    [FormerlySerializedAs("lightOnSound")]
    private AudioClip redLightSound;

    [SerializeField]
    private AudioClip allGreenSound;

    [SerializeField]
    private AudioClip noIndicatorPressSound;

    [SerializeField]
    private AudioClip spawnRefusedSound;

    [Header("Actions")]
    [SerializeField]
    private ButtonAction[] actions;

    private Renderer[] indicatorRenderers;
    private Coroutine pressRoutine;
    private bool hasCompleted;
    private bool showPrompt;
    private Texture2D promptTexture;

    private static readonly int baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int colorId = Shader.PropertyToID("_Color");
    private static readonly int emissionColorId = Shader.PropertyToID("_EmissionColor");

    private void OnEnable()
    {
        if (!activeButtons.Contains(this))
        {
            activeButtons.Add(this);
        }
    }

    private void OnDisable()
    {
        activeButtons.Remove(this);
        showPrompt = false;
    }

    private void Awake()
    {
        CacheIndicatorRenderers();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (promptCamera == null)
        {
            promptCamera = FindPromptCamera();
        }

        if (promptSprite != null)
        {
            promptTexture = promptSprite.texture;
        }

        if (setOffColorOnAwake)
        {
            SetAllIndicatorLights(offColor);
        }
    }

    private void OnGUI()
    {
        if (!showPrompt || promptTexture == null)
        {
            return;
        }

        if (promptCamera == null)
        {
            promptCamera = FindPromptCamera();
        }

        if (promptCamera == null)
        {
            return;
        }

        Vector3 screenPos = promptCamera.WorldToScreenPoint(transform.position);
        if (screenPos.z < 0.0f)
        {
            return;
        }

        float guiX = screenPos.x - promptSize.x * 0.5f + promptOffset.x;
        float guiY = (Screen.height - screenPos.y) - promptSize.y * 0.5f - promptOffset.y;

        Rect rect = new Rect(guiX, guiY, promptSize.x, promptSize.y);
        GUI.DrawTextureWithTexCoords(rect, promptTexture, GetPromptSpriteTexCoords());
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.0f, 0.8f, 1.0f, 0.3f);
        Gizmos.DrawSphere(GetInteractionPointPosition(), interactionRadius);

    }

    private void OnTriggerEnter(Collider other)
    {
        if (activationMode == ActivationMode.TriggerEnter && IsAllowedActivator(other.gameObject))
        {
            Press();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (activationMode == ActivationMode.CollisionEnter && IsAllowedActivator(collision.gameObject))
        {
            Press();
        }
    }

    public void Press()
    {
        if (!CanPress)
        {
            return;
        }

        if (hasCompleted && resetLightsWhenRepeated)
        {
            SetAllIndicatorLights(offColor);
        }

        pressRoutine = StartCoroutine(PressRoutine());
    }

    public bool CanPress => pressRoutine == null && (!hasCompleted || canRepeat);

    public void SetPromptVisible(bool visible)
    {
        showPrompt = visible && CanPress;
    }

    public void ResetButton()
    {
        if (pressRoutine != null)
        {
            StopCoroutine(pressRoutine);
            pressRoutine = null;
        }

        hasCompleted = false;
        SetAllIndicatorLights(offColor);
    }

    private IEnumerator PressRoutine()
    {
        if (indicatorRenderers.Length == 0)
        {
            PlaySound(noIndicatorPressSound);
            ActionRunSummary actionSummary = InvokeActions();

            if (actionSummary.RefusedSpawn)
            {
                PlaySound(spawnRefusedSound);
            }

            hasCompleted = actionSummary.CompletedAction;
            pressRoutine = null;
            yield break;
        }

        for (int i = 0; i < indicatorRenderers.Length; ++i)
        {
            SetIndicatorLight(indicatorRenderers[i], redColor);
            PlaySound(redLightSound);

            if (secondsBetweenLights > 0.0f)
            {
                yield return new WaitForSeconds(secondsBetweenLights);
            }
        }

        SetAllIndicatorLights(greenColor);
        PlaySound(allGreenSound);

        ActionRunSummary completedActionSummary = InvokeActions();
        if (completedActionSummary.RefusedSpawn)
        {
            PlaySound(spawnRefusedSound);
        }

        hasCompleted = completedActionSummary.CompletedAction;
        pressRoutine = null;
    }

    private ActionRunSummary InvokeActions()
    {
        ActionRunSummary summary = new ActionRunSummary();

        if (actions == null)
        {
            summary.CompletedAction = true;
            return summary;
        }

        for (int i = 0; i < actions.Length; ++i)
        {
            ButtonActionResult actionResult = actions[i]?.Invoke(this) ?? ButtonActionResult.None;

            if (actionResult == ButtonActionResult.Completed)
            {
                summary.CompletedAction = true;
            }
            else if (actionResult == ButtonActionResult.RefusedSpawnLimit)
            {
                summary.RefusedSpawn = true;
            }
        }

        if (actions.Length == 0)
        {
            summary.CompletedAction = true;
        }

        return summary;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void CacheIndicatorRenderers()
    {
        if (indicatorLightObjects == null)
        {
            indicatorRenderers = System.Array.Empty<Renderer>();
            return;
        }

        indicatorRenderers = new Renderer[indicatorLightObjects.Length];

        for (int i = 0; i < indicatorLightObjects.Length; ++i)
        {
            if (indicatorLightObjects[i] != null)
            {
                indicatorRenderers[i] = indicatorLightObjects[i].GetComponentInChildren<Renderer>();
            }
        }
    }

    private void SetAllIndicatorLights(Color color)
    {
        if (indicatorRenderers == null)
        {
            return;
        }

        for (int i = 0; i < indicatorRenderers.Length; ++i)
        {
            SetIndicatorLight(indicatorRenderers[i], color);
        }
    }

    private void SetIndicatorLight(Renderer indicatorRenderer, Color color)
    {
        if (indicatorRenderer == null)
        {
            return;
        }

        var material = indicatorRenderer.material;

        if (material.HasProperty(baseColorId))
        {
            material.SetColor(baseColorId, color);
        }

        if (material.HasProperty(colorId))
        {
            material.SetColor(colorId, color);
        }

        if (setEmission && material.HasProperty(emissionColorId))
        {
            Color emissionColor = color.linear * emissionIntensity;
            material.SetColor(emissionColorId, emissionColor);
            material.EnableKeyword("_EMISSION");
        }
    }

    private bool IsAllowedActivator(GameObject activator)
    {
        return string.IsNullOrEmpty(requiredActivatorTag) || activator.CompareTag(requiredActivatorTag);
    }

    private IEnumerator RevertPortalConnectionRoutine(
        Portal connectedPortal,
        Portal connectedOtherPortal,
        Portal previousPortalConnection,
        Portal previousOtherPortalConnection,
        float delay,
        bool turnIndicatorsOff)
    {
        yield return new WaitForSeconds(delay);

        if (connectedPortal != null &&
            connectedOtherPortal != null &&
            connectedPortal.OtherPortal == connectedOtherPortal &&
            connectedOtherPortal.OtherPortal == connectedPortal)
        {
            connectedPortal.Disconnect();

            if (previousPortalConnection != null && previousPortalConnection != connectedOtherPortal)
            {
                connectedPortal.ConnectTo(previousPortalConnection);
            }

            if (previousOtherPortalConnection != null &&
                previousOtherPortalConnection != connectedPortal &&
                connectedOtherPortal.OtherPortal == null)
            {
                connectedOtherPortal.ConnectTo(previousOtherPortalConnection);
            }
        }

        if (turnIndicatorsOff)
        {
            SetAllIndicatorLights(offColor);
        }
    }

    public float GetInteractionDistanceFrom(Vector3 worldPosition)
    {
        Vector3 targetPosition = worldPosition;
        Vector3 pointPosition = GetInteractionPointPosition();

        if (useHorizontalDistance)
        {
            targetPosition.y = 0.0f;
            pointPosition.y = 0.0f;
        }

        return Vector3.Distance(targetPosition, pointPosition);
    }

    public Vector3 GetInteractionPointPosition()
    {
        return interactionPoint != null ? interactionPoint.position : transform.position;
    }

    private Rect GetPromptSpriteTexCoords()
    {
        if (promptSprite == null || promptSprite.texture == null)
        {
            return new Rect(0.0f, 0.0f, 1.0f, 1.0f);
        }

        Rect spriteRect = promptSprite.textureRect;
        Texture texture = promptSprite.texture;
        return new Rect(
            spriteRect.x / texture.width,
            spriteRect.y / texture.height,
            spriteRect.width / texture.width,
            spriteRect.height / texture.height);
    }

    private static Camera FindPromptCamera()
    {
        GameObject mainCameraObject = GameObject.Find("Main Camera");
        if (mainCameraObject != null && mainCameraObject.TryGetComponent(out Camera namedCamera))
        {
            return namedCamera;
        }

        return Camera.main;
    }
}

public class SpawnedPrefabInstance : MonoBehaviour
{
    public GameObject SourcePrefab { get; private set; }

    public void Initialize(GameObject sourcePrefab)
    {
        SourcePrefab = sourcePrefab;
    }
}

public class DespawnOnHeightBounds : MonoBehaviour
{
    [SerializeField]
    private bool despawnBelowHeight;

    [SerializeField]
    private float minimumHeight = -50.0f;

    [SerializeField]
    private bool despawnAboveHeight;

    [SerializeField]
    private float maximumHeight = 100.0f;

    public void Configure(
        bool useMinimumHeight,
        float minHeight,
        bool useMaximumHeight,
        float maxHeight)
    {
        despawnBelowHeight = useMinimumHeight;
        minimumHeight = minHeight;
        despawnAboveHeight = useMaximumHeight;
        maximumHeight = maxHeight;
    }

    private void Update()
    {
        float currentHeight = transform.position.y;

        if ((despawnBelowHeight && currentHeight < minimumHeight) ||
            (despawnAboveHeight && currentHeight > maximumHeight))
        {
            Destroy(gameObject);
        }
    }
}
