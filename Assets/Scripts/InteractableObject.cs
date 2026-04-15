using UnityEngine;

/// <summary>
/// Add this component to any object you want the player to be able to pick up.
/// It displays a screen-space prompt image when the player enters the interaction radius,
/// and exposes pickup/drop events so PickupController can parent the object to the player.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class InteractableObject : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("How close (metres) the player must be for the prompt to appear.")]
    public float interactionRadius = 3f;

    [Header("Prompt UI")]
    [Tooltip("Assign your 'Press E to interact' sprite / UI image here.")]
    public Sprite promptSprite;
    [Tooltip("Size of the prompt image in screen pixels.")]
    public Vector2 promptSize = new Vector2(256f, 64f);
    [Tooltip("Offset from the object's screen position (pixels).")]
    public Vector2 promptOffset = new Vector2(0f, 60f);
    [Tooltip("The camera used to project the prompt onto the screen. Falls back to Camera.main if left empty.")]
    public Camera promptCamera;

    private bool _isHeld = false;
    private bool _showPrompt = false;

    // Cached for OnGUI (GUI.DrawTexture path)
    private Texture2D _promptTexture;
    private Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        // Fall back to the scene's "Main Camera" object if no camera was assigned in the Inspector.
        if (promptCamera == null)
            promptCamera = FindPromptCamera();

        // Convert the sprite to a Texture2D so we can draw it with GUI.DrawTexture
        if (promptSprite != null)
            _promptTexture = SpriteToTexture(promptSprite);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.3f);
        Gizmos.DrawSphere(transform.position, interactionRadius);
    }

    // Called every frame by PickupController

    // Tell this object whether the player is close enough for the prompt.
    public void SetPromptVisible(bool visible) => _showPrompt = visible && !_isHeld;

    // Called by PickupController when the player picks up / drops

    // Disable physics so the object can be parented and carried.
    public void OnPickedUp(Transform holdPoint)
    {
        _isHeld = true;
        _showPrompt = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        transform.SetParent(holdPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    //Re-enable physics and detach from the player.
    public void OnDropped()
    {
        _isHeld = false;
        transform.SetParent(null);
        _rb.isKinematic = false;
    }

    public bool IsHeld => _isHeld;

    // Screen-space UI
    void OnGUI()
    {
        if (!_showPrompt || _promptTexture == null) return;

        // Project the object's world position to screen space
        if (promptCamera == null)
            promptCamera = FindPromptCamera();

        if (promptCamera == null) return;

        Vector3 screenPos = promptCamera.WorldToScreenPoint(transform.position);

        // Don't draw if the object is behind the camera
        if (screenPos.z < 0f) return;

        // Unity's GUI has Y=0 at the top; Camera.WorldToScreenPoint has Y=0 at the bottom
        float guiX = screenPos.x - promptSize.x * 0.5f + promptOffset.x;
        float guiY = (Screen.height - screenPos.y) - promptSize.y * 0.5f - promptOffset.y;

        Rect rect = new Rect(guiX, guiY, promptSize.x, promptSize.y);
        GUI.DrawTexture(rect, _promptTexture, ScaleMode.ScaleToFit, alphaBlend: true);
    }

    // Utility
    private static Camera FindPromptCamera()
    {
        GameObject mainCameraObject = GameObject.Find("Main Camera");
        if (mainCameraObject != null && mainCameraObject.TryGetComponent(out Camera namedCamera))
        {
            return namedCamera;
        }

        return Camera.main;
    }

    private static Texture2D SpriteToTexture(Sprite sprite)
    {
        // If the sprite occupies the full texture, return it directly
        Rect r = sprite.rect;
        if (Mathf.Approximately(r.width, sprite.texture.width) &&
            Mathf.Approximately(r.height, sprite.texture.height))
            return sprite.texture;

        // else blit only the sprite's region out of an atlas
        Texture2D tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
        tex.SetPixels(sprite.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height));
        tex.Apply();
        return tex;
    }
}
