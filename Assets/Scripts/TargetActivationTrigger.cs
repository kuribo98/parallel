using UnityEngine;
using UnityEngine.Events;

public class TargetActivationTrigger : MonoBehaviour
{
    [Header("Hit Detection")]
    [SerializeField]
    private bool activateOnCollision = true;

    [SerializeField]
    private bool activateOnTrigger = true;

    [SerializeField]
    private bool onlyActivateOnce = true;

    [SerializeField]
    [Tooltip("Leave empty to allow any tag.")]
    private string requiredTag = "";

    [SerializeField]
    [Tooltip("Only objects on these layers can activate the target.")]
    private LayerMask allowedLayers = ~0;

    [Header("Target Emission")]
    [SerializeField]
    private Renderer targetRenderer;

    [SerializeField]
    [Min(0)]
    private int materialIndex;

    [SerializeField]
    private Color activatedEmissionColor = Color.cyan;

    [SerializeField]
    [Min(0.0f)]
    private float activatedEmissionIntensity = 1.5f;

    [SerializeField]
    private Texture activatedEmissionTexture;

    [SerializeField]
    private bool setBaseColorToo = true;

    [Header("Target Light")]
    [SerializeField]
    private Light[] targetLights;

    [SerializeField]
    private Color activatedLightColor = Color.cyan;

    [SerializeField]
    private bool setLightIntensity;

    [SerializeField]
    [Min(0.0f)]
    private float activatedLightIntensity = 2.0f;

    [Header("Linked Bounce Pads")]
    [SerializeField]
    private BouncePad[] bouncePads;

    [Header("Events")]
    [SerializeField]
    private UnityEvent onActivated;

    private bool hasActivated;

    private void Reset()
    {
        targetRenderer = GetComponentInChildren<Renderer>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (activateOnCollision && collision.collider != null)
        {
            TryActivate(collision.collider);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (activateOnTrigger)
        {
            TryActivate(other);
        }
    }

    public void Activate()
    {
        if (onlyActivateOnce && hasActivated)
        {
            return;
        }

        hasActivated = true;
        ApplyTargetVisuals();
        EnableBouncePads();
        onActivated?.Invoke();
    }

    private void TryActivate(Collider source)
    {
        if (source == null || !IsAllowed(source.gameObject))
        {
            return;
        }

        Activate();
    }

    private bool IsAllowed(GameObject source)
    {
        if (!string.IsNullOrWhiteSpace(requiredTag) && !source.CompareTag(requiredTag))
        {
            return false;
        }

        return (allowedLayers.value & (1 << source.layer)) != 0;
    }

    private void ApplyTargetVisuals()
    {
        EmissiveMaterialHelper.Apply(
            targetRenderer,
            materialIndex,
            activatedEmissionColor,
            activatedEmissionIntensity,
            activatedEmissionTexture,
            setBaseColorToo,
            this);

        if (targetLights == null)
        {
            return;
        }

        for (int i = 0; i < targetLights.Length; ++i)
        {
            if (targetLights[i] == null)
            {
                continue;
            }

            targetLights[i].color = activatedLightColor;
            if (setLightIntensity)
            {
                targetLights[i].intensity = activatedLightIntensity;
            }
        }
    }

    private void EnableBouncePads()
    {
        if (bouncePads == null)
        {
            return;
        }

        for (int i = 0; i < bouncePads.Length; ++i)
        {
            if (bouncePads[i] != null)
            {
                bouncePads[i].SetEnabled(true);
            }
        }
    }
}
