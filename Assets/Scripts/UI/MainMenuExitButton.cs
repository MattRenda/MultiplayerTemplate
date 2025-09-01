using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MainMenuExitButton : MonoBehaviour
{
    [Tooltip("Assign the UI Button to trigger application quit.")]
    public Button exitButton;

    void Awake()
    {
        if (exitButton == null)
            exitButton = GetComponent<Button>();
        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(Exit);
        }
    }

    void OnDestroy()
    {
        if (exitButton != null)
            exitButton.onClick.RemoveListener(Exit);
    }

    public void Exit()
    {
#if UNITY_EDITOR
        // stop Play Mode in editor
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
