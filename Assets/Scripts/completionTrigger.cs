using UnityEngine;

public class completionTrigger : MonoBehaviour
{
    public AudioSource yay;
    private bool isUsed;

    private void OnTriggerEnter(Collider other)
    {
        if (isUsed || !other.CompareTag("Player"))
           
        {
            return;
        }

        yay.Play();
        isUsed = true;
        Destroy(gameObject, yay.clip.length);

    }
}
