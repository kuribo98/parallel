using UnityEngine;

public class GrowTrigger : MonoBehaviour
{
    public float scaleMultiplier = 0.3f;
    private bool isUsed;

    private void OnTriggerEnter(Collider other)
    {
        if (isUsed || !other.CompareTag("Player"))
        {
            return;
        }

        other.transform.localScale *= scaleMultiplier;
        isUsed = true;
        Destroy(gameObject);
    }
}