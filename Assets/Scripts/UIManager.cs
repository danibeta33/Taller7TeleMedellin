using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class UIManager : MonoBehaviour
{
    [Header("Paneles")]
    [SerializeField] private GameObject panelMenu;
    [SerializeField] private GameObject panelSalir;

    [Header("Botones")]
    [SerializeField] private Button btnMenu;
    [SerializeField] private Button btnContinuarMenu;
    [SerializeField] private Button btnReiniciar;
    [SerializeField] private Button btnCerrarMenu;
    [SerializeField] private Button btnSalirApp;
    [SerializeField] private Button btnContinuarSalir;

    [Header("Referencias")]
    [SerializeField] private GameSequenceManager gameSequenceManager;
    [SerializeField] private VideoPlayer videoPlayer;

    private bool isPaused;
    private bool wasVideoPlayingBeforePause;

    private void Awake()
    {
        WireButtons();
        CloseAllPanels();
    }

    private void OnDestroy()
    {
        UnwireButtons();
    }

    public void PauseGame()
    {
        if (isPaused)
        {
            return;
        }

        isPaused = true;
        wasVideoPlayingBeforePause = videoPlayer != null && videoPlayer.isPlaying;

        if (gameSequenceManager != null)
        {
            gameSequenceManager.PauseFlow();
        }
        else if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }

        Time.timeScale = 0f;
    }

    public void ResumeGame()
    {
        if (!isPaused)
        {
            return;
        }

        Time.timeScale = 1f;

        if (gameSequenceManager != null)
        {
            gameSequenceManager.ResumeFlow();
        }
        else if (videoPlayer != null && wasVideoPlayingBeforePause && !videoPlayer.isPlaying && videoPlayer.clip != null)
        {
            videoPlayer.Play();
        }

        isPaused = false;
        wasVideoPlayingBeforePause = false;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        isPaused = false;
        wasVideoPlayingBeforePause = false;

        CloseAllPanels();

        if (gameSequenceManager != null)
        {
            gameSequenceManager.ResetExperience();
        }
    }

    public void OpenMenu()
    {
        PauseGame();

        if (panelSalir != null)
        {
            panelSalir.SetActive(false);
        }

        if (panelMenu != null)
        {
            panelMenu.SetActive(true);
        }
    }

    public void ContinueFromMenu()
    {
        if (panelMenu != null)
        {
            panelMenu.SetActive(false);
        }

        if (panelSalir != null)
        {
            panelSalir.SetActive(false);
        }

        ResumeGame();
    }

    public void OpenExitPanel()
    {
        if (panelMenu != null)
        {
            panelMenu.SetActive(false);
        }

        if (panelSalir != null)
        {
            panelSalir.SetActive(true);
        }
    }

    public void CloseExitPanel()
    {
        if (panelSalir != null)
        {
            panelSalir.SetActive(false);
        }

        if (panelMenu != null)
        {
            panelMenu.SetActive(true);
        }
    }

    public void QuitApplication()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }

    private void CloseAllPanels()
    {
        if (panelMenu != null)
        {
            panelMenu.SetActive(false);
        }

        if (panelSalir != null)
        {
            panelSalir.SetActive(false);
        }
    }

    private void WireButtons()
    {
        if (btnMenu != null)
        {
            btnMenu.onClick.AddListener(OpenMenu);
        }

        if (btnContinuarMenu != null)
        {
            btnContinuarMenu.onClick.AddListener(ContinueFromMenu);
        }

        if (btnReiniciar != null)
        {
            btnReiniciar.onClick.AddListener(RestartGame);
        }

        if (btnCerrarMenu != null)
        {
            btnCerrarMenu.onClick.AddListener(OpenExitPanel);
        }

        if (btnSalirApp != null)
        {
            btnSalirApp.onClick.AddListener(QuitApplication);
        }

        if (btnContinuarSalir != null)
        {
            btnContinuarSalir.onClick.AddListener(CloseExitPanel);
        }
    }

    private void UnwireButtons()
    {
        if (btnMenu != null)
        {
            btnMenu.onClick.RemoveListener(OpenMenu);
        }

        if (btnContinuarMenu != null)
        {
            btnContinuarMenu.onClick.RemoveListener(ContinueFromMenu);
        }

        if (btnReiniciar != null)
        {
            btnReiniciar.onClick.RemoveListener(RestartGame);
        }

        if (btnCerrarMenu != null)
        {
            btnCerrarMenu.onClick.RemoveListener(OpenExitPanel);
        }

        if (btnSalirApp != null)
        {
            btnSalirApp.onClick.RemoveListener(QuitApplication);
        }

        if (btnContinuarSalir != null)
        {
            btnContinuarSalir.onClick.RemoveListener(CloseExitPanel);
        }
    }
}
