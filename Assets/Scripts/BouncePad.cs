using UnityEngine;
using UnityEngine.Events;

public class BouncePad : MonoBehaviour
{
    [Header("State")]
    [SerializeField]
    private bool startEnabled;

    [Header("Bounce")]
    [SerializeField]
    [Min(0.0f)]
    private float launchVelocity = 15.0f;

    [SerializeField]
    private Vector3 launchDirection = Vector3.up;

    [SerializeField]
    private bool preserveSidewaysVelocity = true;

    [SerializeField]
    [Min(0.0f)]
    private float bounceCooldown = 0.2f;

    [SerializeField]
    [Min(0.0f)]
    private float fallDamageImmunityAfterBounce = 0.5f;

    [Header("Player Detection")]
    [SerializeField]
    private bool bounceOnTrigger = true;

    [SerializeField]
    private bool bounceOnCollision = true;

    [SerializeField]
    private bool requirePlayerScript = true;

    [SerializeField]
    [Tooltip("Leave empty to allow any tag.")]
    private string requiredTag = "";

    [SerializeField]
    [Tooltip("Only objects on these layers can use the bounce pad.")]
    private LayerMask allowedLayers = ~0;

    [Header("Emission")]
    [SerializeField]
    private Renderer padRenderer;

    [SerializeField]
    [Min(0)]
    private int materialIndex;

    [SerializeField]
    private Color disabledEmissionColor = Color.black;

    [SerializeField]
    [Min(0.0f)]
    private float disabledEmissionIntensity;

    [SerializeField]
    private Texture disabledEmissionTexture;

    [SerializeField]
    private Color enabledEmissionColor = Color.cyan;

    [SerializeField]
    [Min(0.0f)]
    private float enabledEmissionIntensity = 1.5f;

    [SerializeField]
    private Texture enabledEmissionTexture;

    [SerializeField]
    private bool setBaseColorToo = true;

    [Header("Feedback")]
    [SerializeField]
    private AudioSource audioSource;

    [SerializeField]
    private AudioClip bounceSound;

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private string bounceAnimationTrigger = "Bounce";

    [SerializeField]
    private string enabledAnimatorBool = "Enabled";

    [Header("Events")]
    [SerializeField]
    private UnityEvent onEnabled;

    [SerializeField]
    private UnityEvent onDisabled;

    [SerializeField]
    private UnityEvent onBounce;

    private bool isEnabled;
    private float nextAllowedBounceTime;

    public bool IsEnabled => isEnabled;

    private void Reset()
    {
        padRenderer = GetComponentInChildren<Renderer>();
        audioSource = GetComponent<AudioSource>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        SetEnabled(startEnabled, invokeEvents: false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (bounceOnTrigger)
        {
            TryBounce(other);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (bounceOnCollision && collision.collider != null)
        {
            TryBounce(collision.collider);
        }
    }

    public void SetEnabled(bool enabled)
    {
        SetEnabled(enabled, invokeEvents: true);
    }

    public void Enable()
    {
        SetEnabled(true);
    }

    public void Disable()
    {
        SetEnabled(false);
    }

    private void SetEnabled(bool enabled, bool invokeEvents)
    {
        if (isEnabled == enabled)
        {
            ApplyVisualState();
            ApplyAnimatorEnabledState();
            return;
        }

        isEnabled = enabled;
        ApplyVisualState();
        ApplyAnimatorEnabledState();

        if (!invokeEvents)
        {
            return;
        }

        if (isEnabled)
        {
            onEnabled?.Invoke();
        }
        else
        {
            onDisabled?.Invoke();
        }
    }

    private void TryBounce(Collider collider)
    {
        if (!isEnabled || Time.time < nextAllowedBounceTime || collider == null)
        {
            return;
        }

        if (!IsAllowed(collider.gameObject))
        {
            return;
        }

        if (!TryGetBounceRigidbody(collider, out Rigidbody targetRigidbody, out playerScript player))
        {
            return;
        }

        nextAllowedBounceTime = Time.time + bounceCooldown;

        if (player != null)
        {
            player.DisableGroundStickFor(fallDamageImmunityAfterBounce);
        }

        Launch(targetRigidbody);
        PlayFeedback();
        onBounce?.Invoke();
    }

    private bool IsAllowed(GameObject source)
    {
        if (!string.IsNullOrWhiteSpace(requiredTag) && !source.CompareTag(requiredTag))
        {
            return false;
        }

        return (allowedLayers.value & (1 << source.layer)) != 0;
    }

    private bool TryGetBounceRigidbody(Collider collider, out Rigidbody targetRigidbody, out playerScript player)
    {
        targetRigidbody = null;
        player = collider.GetComponentInParent<playerScript>();

        if (requirePlayerScript && player == null)
        {
            return false;
        }

        if (player != null)
        {
            targetRigidbody = player.rb != null ? player.rb : player.GetComponent<Rigidbody>();
        }

        if (targetRigidbody == null)
        {
            targetRigidbody = collider.attachedRigidbody;
        }

        return targetRigidbody != null;
    }

    private void Launch(Rigidbody targetRigidbody)
    {
        Vector3 direction = launchDirection.sqrMagnitude > 0.0001f
            ? launchDirection.normalized
            : Vector3.up;

        Vector3 currentVelocity = preserveSidewaysVelocity ? targetRigidbody.linearVelocity : Vector3.zero;
        Vector3 velocityWithoutLaunchAxis = currentVelocity - Vector3.Project(currentVelocity, direction);
        targetRigidbody.linearVelocity = velocityWithoutLaunchAxis + direction * launchVelocity;
    }

    private void PlayFeedback()
    {
        if (audioSource != null && bounceSound != null)
        {
            audioSource.PlayOneShot(bounceSound);
        }

        if (animator != null && !string.IsNullOrWhiteSpace(bounceAnimationTrigger))
        {
            animator.SetTrigger(bounceAnimationTrigger);
        }
    }

    private void ApplyVisualState()
    {
        EmissiveMaterialHelper.Apply(
            padRenderer,
            materialIndex,
            isEnabled ? enabledEmissionColor : disabledEmissionColor,
            isEnabled ? enabledEmissionIntensity : disabledEmissionIntensity,
            isEnabled ? enabledEmissionTexture : disabledEmissionTexture,
            setBaseColorToo,
            this);
    }

    private void ApplyAnimatorEnabledState()
    {
        if (animator != null && !string.IsNullOrWhiteSpace(enabledAnimatorBool))
        {
            animator.SetBool(enabledAnimatorBool, isEnabled);
        }
    }
}
