using UnityEngine;
using UnityEngine.SceneManagement;
public class MainmenuManager : MonoBehaviour
{
  public void LoadScene(string sceneName)
            {
        SceneManager.LoadScene(sceneName);
    }
    public void QuitGame()
    {
        Application.Quit();
    }   
}
