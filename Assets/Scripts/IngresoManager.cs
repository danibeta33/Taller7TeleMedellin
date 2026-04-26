using System.Collections;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IngresoManager : MonoBehaviour
{
    private enum IngresoState
    {
        DetectingPlayers,
        StableCountdown,
        Finished,
    }

    [Header("UI")]
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject panelToDisable;

    [Header("Sistema Central de Manos")]
    [SerializeField] private HandLandmarkerRunner handLandmarkerRunner;
    [SerializeField] private HandTrackingCenter handTrackingCenter;

    [Header("Tiempo")]
    [SerializeField] private float detectionWindowSeconds = 30f;
    [SerializeField] private float stabilitySeconds = 7f;
    [SerializeField] private float finalCountdownSeconds = 5f;
    [SerializeField] private float stabilitySampleInterval = 0.1f;

    [Header("Robustez")]
    [SerializeField] private int framesToAcceptCount = 4;
    [SerializeField] private float thumbExtensionFactor = 0.18f;
    [SerializeField] private float foldedFingerFactor = 0.08f;
    [SerializeField] private float minimumHandScale = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool enableManagerDebugLogs;
    [SerializeField] private float managerDebugLogIntervalSeconds = 0.75f;

    [Header("Integracion")]
    [SerializeField] private bool autoStartOnEnable = true;
    [SerializeField] private bool autoClosePanelOnFinish = true;
    [SerializeField] private string savedPlayersPrefsKey = "IngresoManager.FinalPlayerCount";
    [SerializeField] private int minNumHands = 1;
    [SerializeField] private int maxNumHands = 10;

    private HandLandmarkDetectionConfig config;
    private IngresoState currentState = IngresoState.DetectingPlayers;
    private Coroutine detectionWindowCoroutine;
    private Coroutine stabilityCoroutine;
    private Coroutine finalCountdownCoroutine;
    private float detectionTimeRemaining;
    private int lastRawHands = -1;
    private int consecutiveRawFrames;
    private int acceptedHands;
    private bool closeListenerRegistered;
    private float nextManagerLogTime;

    public int FinalPlayerCount { get; private set; }

    private void Awake()
    {
        ResolveHandLandmarkerRunner();
        ResolveHandTrackingCenter();

        if (handLandmarkerRunner == null)
        {
            Debug.LogError("IngresoManager: No se encontro HandLandmarkerRunner.");
            enabled = false;
            return;
        }

        if (handTrackingCenter == null)
        {
            Debug.LogError("IngresoManager: No se encontro HandTrackingCenter.");
            enabled = false;
            return;
        }

        config = handLandmarkerRunner.config;
    }

    private void OnEnable()
    {
        if (closeButton != null && closeButton.onClick.GetPersistentEventCount() == 0)
        {
            closeButton.onClick.AddListener(ClosePanel);
            closeListenerRegistered = true;
        }

        if (autoStartOnEnable)
        {
            StartPlayerDetection();
        }
    }

    private void OnDisable()
    {
        if (closeButton != null && closeListenerRegistered)
        {
            closeButton.onClick.RemoveListener(ClosePanel);
            closeListenerRegistered = false;
        }

        StopAllRuntimeCoroutines();
    }

    private void Update()
    {
        if (currentState != IngresoState.DetectingPlayers)
        {
            return;
        }

        var rawHands = CountValidThumbsUpHands();
        UpdateAcceptedHands(rawHands);
        UpdatePlayersText(acceptedHands);
    }

    public void StartPlayerDetection()
    {
        StopAllRuntimeCoroutines();

        FinalPlayerCount = 0;
        currentState = IngresoState.DetectingPlayers;
        detectionTimeRemaining = Mathf.Max(1f, detectionWindowSeconds);
        lastRawHands = -1;
        consecutiveRawFrames = 0;
        acceptedHands = 0;

        UpdatePlayersText(0);
        UpdateStatusText("Haz un like con tu mano para unirte");
        UpdateTimerText("" + Mathf.CeilToInt(detectionTimeRemaining));

        detectionWindowCoroutine = StartCoroutine(DetectionWindowCoroutine());
        stabilityCoroutine = StartCoroutine(StabilityWatchCoroutine());
    }

    public void ClosePanel()
    {
        if (panelToDisable != null)
        {
            panelToDisable.SetActive(false);
        }
    }

    public bool IsFinished()
    {
        return currentState == IngresoState.Finished;
    }

    // Compatibilidad con escenas antiguas que tenian botones + y - apuntando al manager anterior.
    public void IncreaseNumHands()
    {
        maxNumHands = Mathf.Clamp(maxNumHands + 1, 1, 20);
    }

    // Compatibilidad con escenas antiguas que tenian botones + y - apuntando al manager anterior.
    public void DecreaseNumHands()
    {
        maxNumHands = Mathf.Clamp(maxNumHands - 1, 1, 20);
    }

    private void ResolveHandLandmarkerRunner()
    {
        if (handLandmarkerRunner != null)
        {
            return;
        }

        var solution = GameObject.Find("Solution");
        if (solution != null)
        {
            handLandmarkerRunner = solution.GetComponent<HandLandmarkerRunner>();
        }
    }

    private void ResolveHandTrackingCenter()
    {
        if (handTrackingCenter != null)
        {
            return;
        }

        handTrackingCenter = FindFirstObjectByType<HandTrackingCenter>();
        if (handTrackingCenter != null)
        {
            return;
        }

        var host = new GameObject("HandTrackingCenter");
        handTrackingCenter = host.AddComponent<HandTrackingCenter>();
        EmitManagerDebugLog("[IngresoManager] Se creo HandTrackingCenter automaticamente.");
    }

    private int CountValidThumbsUpHands()
    {
        if (handTrackingCenter == null)
        {
            EmitManagerDebugLog("[IngresoManager] HandTrackingCenter no disponible.");
            return 0;
        }

        var hands = handTrackingCenter.GetHands();
        var thumbsUpCount = handTrackingCenter.GetThumbsUpHandsCount();
        EmitManagerDebugLog("[IngresoManager] Hands:" + hands.Count + " ThumbsUp:" + thumbsUpCount);
        return thumbsUpCount;
    }

    private void UpdateAcceptedHands(int rawHands)
    {
        // Debounce temporal: solo acepta un nuevo conteo cuando persiste varios frames.
        if (rawHands != lastRawHands)
        {
            lastRawHands = rawHands;
            consecutiveRawFrames = 1;
            return;
        }

        consecutiveRawFrames++;
        if (consecutiveRawFrames >= Mathf.Max(1, framesToAcceptCount))
        {
            acceptedHands = rawHands;
        }
    }

    private IEnumerator DetectionWindowCoroutine()
    {
        while (currentState == IngresoState.DetectingPlayers && detectionTimeRemaining > 0f)
        {
            detectionTimeRemaining -= Time.deltaTime;
            if (detectionTimeRemaining < 0f)
            {
                detectionTimeRemaining = 0f;
            }

            UpdateTimerText("" + Mathf.CeilToInt(detectionTimeRemaining));
            yield return null;
        }

        if (currentState == IngresoState.DetectingPlayers)
        {
            StartFinalCountdown("Listo!, Somos " + FinalPlayerCount);
        }
    }

    private IEnumerator StabilityWatchCoroutine()
    {
        var referenceHands = acceptedHands;
        var stableTime = 0f;
        var wait = new WaitForSeconds(Mathf.Max(0.05f, stabilitySampleInterval));

        while (currentState == IngresoState.DetectingPlayers)
        {
            if (acceptedHands <= 0)
            {
                referenceHands = acceptedHands;
                stableTime = 0f;
                yield return wait;
                continue;
            }

            if (acceptedHands != referenceHands)
            {
                referenceHands = acceptedHands;
                stableTime = 0f;
            }
            else
            {
                stableTime += Mathf.Max(0.05f, stabilitySampleInterval);
                if (stableTime >= Mathf.Max(0.1f, stabilitySeconds))
                {
                    StartFinalCountdown("¡Quieto ahí!");
                    yield break;
                }
            }

            yield return wait;
        }
    }

    private void StartFinalCountdown(string status)
    {
        if (currentState != IngresoState.DetectingPlayers)
        {
            return;
        }

        currentState = IngresoState.StableCountdown;
        UpdateStatusText(status);

        if (finalCountdownCoroutine != null)
        {
            StopCoroutine(finalCountdownCoroutine);
        }

        finalCountdownCoroutine = StartCoroutine(FinalCountdownCoroutine());
    }

    private IEnumerator FinalCountdownCoroutine()
    {
        var countdown = Mathf.Max(1f, finalCountdownSeconds);

        while (currentState == IngresoState.StableCountdown && countdown > 0f)
        {
            UpdateTimerText("" + Mathf.CeilToInt(countdown) + "");
            countdown -= Time.deltaTime;
            yield return null;
        }

        if (currentState == IngresoState.StableCountdown)
        {
            LockFinalPlayers();
        }
    }

    private void LockFinalPlayers()
    {
        currentState = IngresoState.Finished;
        FinalPlayerCount = Mathf.Max(0, acceptedHands);

        UpdatePlayersText(FinalPlayerCount);
        UpdateTimerText("0s");
        UpdateStatusText("Listo!, Somos " + FinalPlayerCount );

        if (config != null)
        {
            config.NumHands = Mathf.Clamp(FinalPlayerCount, minNumHands, maxNumHands);
        }

        if (!string.IsNullOrEmpty(savedPlayersPrefsKey))
        {
            PlayerPrefs.SetInt(savedPlayersPrefsKey, FinalPlayerCount);
            PlayerPrefs.Save();
        }

        if (autoClosePanelOnFinish)
        {
            ClosePanel();
        }
    }

    private void UpdatePlayersText(int players)
    {
        if (numberText != null)
        {
            numberText.text = players.ToString(); 
        }
    }

    private void UpdateTimerText(string value)
    {
        if (timerText != null)
        {
            timerText.text = value;
        }
    }

    private void UpdateStatusText(string value)
    {
        if (statusText != null)
        {
            statusText.text = value;
        }
    }

    private void StopAllRuntimeCoroutines()
    {
        if (detectionWindowCoroutine != null)
        {
            StopCoroutine(detectionWindowCoroutine);
            detectionWindowCoroutine = null;
        }

        if (stabilityCoroutine != null)
        {
            StopCoroutine(stabilityCoroutine);
            stabilityCoroutine = null;
        }

        if (finalCountdownCoroutine != null)
        {
            StopCoroutine(finalCountdownCoroutine);
            finalCountdownCoroutine = null;
        }
    }

    private void EmitManagerDebugLog(string message)
    {
        if (!enableManagerDebugLogs)
        {
            return;
        }

        if (Time.unscaledTime < nextManagerLogTime)
        {
            return;
        }

        nextManagerLogTime = Time.unscaledTime + Mathf.Max(0.05f, managerDebugLogIntervalSeconds);
        Debug.Log(message);
    }
}
