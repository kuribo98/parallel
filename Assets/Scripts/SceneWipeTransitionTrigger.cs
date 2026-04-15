using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Collider))]
public class SceneWipeTransitionTrigger : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField]
    [Tooltip("If set, this scene name is loaded after the wipe fills the screen.")]
    private string sceneName = "";

    [SerializeField]
    [Tooltip("Used when Scene Name is empty. Set to -1 to load the next build-index scene.")]
    private int sceneBuildIndex = -1;

    [Header("Activation")]
    [SerializeField]
    private bool requirePlayerScript = true;

    [SerializeField]
    [Tooltip("Leave empty to allow any tag.")]
    private string requiredTag = "";

    [SerializeField]
    [Tooltip("Only objects on these layers can trigger the transition.")]
    private LayerMask allowedLayers = ~0;

    [SerializeField]
    private bool triggerOnlyOnce = true;

    [Header("Wipe")]
    [SerializeField]
    [Min(0.01f)]
    private float wipeDuration = 1.0f;

    [SerializeField]
    [Min(0.0f)]
    private float waitAfterWipe = 0.1f;

    [SerializeField]
    private bool useUnscaledTime = true;

    [SerializeField]
    private AnimationCurve wipeCurve = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

    [Header("Optional UI")]
    [SerializeField]
    [Tooltip("Optional existing image to use as the wipe. If empty, one is created automatically.")]
    private Image transitionImage;

    [SerializeField]
    [Tooltip("Optional canvas to receive the generated image. If empty, a screen-space overlay canvas is created.")]
    private Canvas transitionCanvas;

    [SerializeField]
    private Sprite wipeSprite;

    [SerializeField]
    private Color wipeColor = Color.black;

    [SerializeField]
    private Material wipeMaterial;

    [SerializeField]
    private int generatedCanvasSortingOrder = 1000;

    [Header("Events")]
    [SerializeField]
    private UnityEvent onTransitionStarted;

    [SerializeField]
    private UnityEvent onWipeFilled;

    private bool isTransitioning;

    private void Reset()
    {
        Collider triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
    }

    private void Awake()
    {
        HideWipe();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isTransitioning || !CanTrigger(other))
        {
            return;
        }

        StartCoroutine(TransitionRoutine());
    }

    private bool CanTrigger(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        GameObject source = other.gameObject;
        if ((allowedLayers.value & (1 << source.layer)) == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredTag) && !source.CompareTag(requiredTag))
        {
            return false;
        }

        if (requirePlayerScript && other.GetComponentInParent<playerScript>() == null)
        {
            return false;
        }

        return true;
    }

    private IEnumerator TransitionRoutine()
    {
        isTransitioning = true;
        PrepareWipe();
        SetWipeProgress(0.0f);
        onTransitionStarted?.Invoke();

        float elapsed = 0.0f;
        while (elapsed < wipeDuration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / wipeDuration);
            float progress = wipeCurve != null ? wipeCurve.Evaluate(t) : t;
            SetWipeProgress(progress);
            yield return null;
        }

        SetWipeProgress(1.0f);
        onWipeFilled?.Invoke();

        if (waitAfterWipe > 0.0f)
        {
            if (useUnscaledTime)
            {
                yield return new WaitForSecondsRealtime(waitAfterWipe);
            }
            else
            {
                yield return new WaitForSeconds(waitAfterWipe);
            }
        }

        LoadDestinationScene();

        if (!triggerOnlyOnce)
        {
            isTransitioning = false;
        }
    }

    private void PrepareWipe()
    {
        if (transitionImage == null)
        {
            transitionImage = CreateRuntimeTransitionImage();
        }

        transitionImage.gameObject.SetActive(true);
        transitionImage.enabled = true;
        transitionImage.raycastTarget = false;
        transitionImage.sprite = wipeSprite;
        transitionImage.color = wipeColor;
        transitionImage.material = wipeMaterial;
        transitionImage.type = Image.Type.Simple;
        transitionImage.preserveAspect = false;
    }

    private Image CreateRuntimeTransitionImage()
    {
        Canvas canvas = transitionCanvas != null ? transitionCanvas : CreateRuntimeCanvas();

        var imageObject = new GameObject("Scene Wipe Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(canvas.transform, false);
        return imageObject.GetComponent<Image>();
    }

    private Canvas CreateRuntimeCanvas()
    {
        var canvasObject = new GameObject("Scene Wipe Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = generatedCanvasSortingOrder;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
        scaler.matchWidthOrHeight = 0.5f;

        DontDestroyOnLoad(canvasObject);
        return canvas;
    }

    private void SetWipeProgress(float progress)
    {
        if (transitionImage == null)
        {
            return;
        }

        progress = Mathf.Clamp01(progress);
        RectTransform rectTransform = transitionImage.rectTransform;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = new Vector2(progress, 1.0f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.0f, 0.5f);
    }

    private void HideWipe()
    {
        if (transitionImage == null)
        {
            return;
        }

        SetWipeProgress(0.0f);
        transitionImage.gameObject.SetActive(false);
    }

    private void LoadDestinationScene()
    {
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        int targetBuildIndex = sceneBuildIndex >= 0
            ? sceneBuildIndex
            : SceneManager.GetActiveScene().buildIndex + 1;

        if (targetBuildIndex < 0 || targetBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError(
                $"{name} cannot load scene build index {targetBuildIndex}. Set Scene Name or a valid Scene Build Index.",
                this);
            return;
        }

        SceneManager.LoadScene(targetBuildIndex);
    }
}
