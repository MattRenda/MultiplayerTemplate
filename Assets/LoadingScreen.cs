using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance;

    public CanvasGroup canvasGroup;
    public Slider progressBar; // optional

    // track our own scene load (for non-networked helper)
    AsyncOperation _localLoadOp;
    Coroutine _progressRoutine;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // Auto-find or create a CanvasGroup if not assigned
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>(true);
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        // Best-effort auto-hook a progress bar if not assigned
        if (progressBar == null)
            progressBar = GetComponentInChildren<Slider>(true);
    SetVisible(false, immediate:true);
    }

    void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        // Safety net: hide on scene activation in case a network callback didn’t fire
        Debug.Log($"LoadingScreen: activeSceneChanged {oldScene.name} -> {newScene.name}");
        StartCoroutine(HideNextFrame());
    }

    IEnumerator HideNextFrame()
    {
        yield return null; // wait one frame to let scene finish activating
        Hide();
    }

    public void Show() { Debug.Log($"LoadingScreen: Show at {Time.realtimeSinceStartup:F2}"); SetVisible(true, immediate:true); }
    public void Hide() { Debug.Log($"LoadingScreen: Hide at {Time.realtimeSinceStartup:F2}"); SetVisible(false, immediate:true); }

    void SetVisible(bool visible, bool immediate = false)
    {
        Debug.Log($"LoadingScreen: SetVisible({visible}), immediate={immediate}, alpha={canvasGroup.alpha:F2}");
        // always immediate to prevent flashing the empty scene
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;

        // manage progress updates
        if (visible)
        {
            if (_progressRoutine != null) StopCoroutine(_progressRoutine);
            _progressRoutine = StartCoroutine(UpdateProgressLoop());
        }
        else
        {
            if (_progressRoutine != null) { StopCoroutine(_progressRoutine); _progressRoutine = null; }
            if (progressBar) progressBar.value = 0f;
            _localLoadOp = null;
        }
    }

    IEnumerator UpdateProgressLoop()
    {
        // Update progress bar while visible, if available
        while (canvasGroup.alpha > 0.5f) // visible
        {
            if (progressBar)
            {
                float p = GetSceneLoadProgress();
                progressBar.value = p;
            }
            yield return null;
        }
    }

    public void LoadScene(string sceneName)
    {
        StartCoroutine(LoadRoutine(sceneName));
    }

    IEnumerator LoadRoutine(string sceneName)
    {
        Show();
        yield return null;

        _localLoadOp = SceneManager.LoadSceneAsync(sceneName);
        _localLoadOp.allowSceneActivation = false;

        while (_localLoadOp.progress < 0.9f)
        {
            if (progressBar) progressBar.value = Mathf.Clamp01(_localLoadOp.progress / 0.9f);
            yield return null;
        }

        if (progressBar) progressBar.value = 1f;
        yield return new WaitForSecondsRealtime(0.1f); // tiny polish delay
        _localLoadOp.allowSceneActivation = true;

        yield return null; // let scene activate
        Hide();
        _localLoadOp = null;
    }

    float GetSceneLoadProgress()
    {
        // prefer our own op if we started it
        if (_localLoadOp != null)
        {
            return _localLoadOp.progress < 0.9f ? Mathf.Clamp01(_localLoadOp.progress / 0.9f) : 1f;
        }

        // try to read Mirror.NetworkManager.loadingSceneAsync via reflection (optional, no hard dependency)
        try
        {
            var mirrorType = Type.GetType("Mirror.NetworkManager, Mirror");
            if (mirrorType != null)
            {
                var field = mirrorType.GetField("loadingSceneAsync");
                if (field != null)
                {
                    var asyncOp = field.GetValue(null) as AsyncOperation;
                    if (asyncOp != null)
                    {
                        float prog = asyncOp.progress;
                        return prog < 0.9f ? Mathf.Clamp01(prog / 0.9f) : (asyncOp.isDone ? 1f : 1f); // treat >=0.9 as 1 while waiting activation
                    }
                }
            }
        }
        catch { /* ignore reflection issues */ }

        // unknown; keep last value or 0
        return progressBar ? progressBar.value : 0f;
    }
}