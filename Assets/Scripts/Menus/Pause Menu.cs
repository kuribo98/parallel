using UnityEngine;
using UnityEngine.SceneManagement;
public class NewMonoBehaviourScript : MonoBehaviour
{
    public GameObject container;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            container.SetActive(true);
            Time.timeScale = 0;
        }
    }
    public void Resume()
    {
        container.SetActive(false);
        Time.timeScale = 1;
    }
    
    public void mainMenuButton()
    {         Time.timeScale = 1;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
    public void Restart()
    {         Time.timeScale = 1;
        UnityEngine.SceneManagement.SceneManager.LoadScene("PortalDemo");
    }
}
