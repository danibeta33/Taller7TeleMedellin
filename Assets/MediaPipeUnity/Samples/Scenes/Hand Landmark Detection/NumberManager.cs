using Mediapipe.Unity.Sample.HandLandmarkDetection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NumberManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject panelToDisable;

    [Header("Mediapipe")]
    [SerializeField] private HandLandmarkerRunner handLandmarkerRunner;

    [Header("Limits")]
    [SerializeField] private int minNumHands = 1;
    [SerializeField] private int maxNumHands = 10;

    private HandLandmarkDetectionConfig config;
    private int currentNumHands;
    private bool plusListenerRegistered;
    private bool minusListenerRegistered;
    private bool closeListenerRegistered;

    private void Awake()
    {
        if (handLandmarkerRunner == null)
        {
            var solution = GameObject.Find("Solution");
            if (solution != null)
            {
                handLandmarkerRunner = solution.GetComponent<HandLandmarkerRunner>();
            }
        }

        if (handLandmarkerRunner == null)
        {
            Debug.LogError("NumberManager: No se encontro HandLandmarkerRunner.");
            enabled = false;
            return;
        }

        config = handLandmarkerRunner.config;
        currentNumHands = Mathf.Clamp(config.NumHands, minNumHands, maxNumHands);
        ApplyCurrentNumber();
    }

    private void OnEnable()
    {
        if (plusButton != null && plusButton.onClick.GetPersistentEventCount() == 0)
        {
            plusButton.onClick.AddListener(IncreaseNumHands);
            plusListenerRegistered = true;
        }

        if (minusButton != null && minusButton.onClick.GetPersistentEventCount() == 0)
        {
            minusButton.onClick.AddListener(DecreaseNumHands);
            minusListenerRegistered = true;
        }

        if (closeButton != null && closeButton.onClick.GetPersistentEventCount() == 0)
        {
            closeButton.onClick.AddListener(ClosePanel);
            closeListenerRegistered = true;
        }
    }

    private void OnDisable()
    {
        if (plusButton != null && plusListenerRegistered)
        {
            plusButton.onClick.RemoveListener(IncreaseNumHands);
            plusListenerRegistered = false;
        }

        if (minusButton != null && minusListenerRegistered)
        {
            minusButton.onClick.RemoveListener(DecreaseNumHands);
            minusListenerRegistered = false;
        }

        if (closeButton != null && closeListenerRegistered)
        {
            closeButton.onClick.RemoveListener(ClosePanel);
            closeListenerRegistered = false;
        }
    }

    public void IncreaseNumHands()
    {
        currentNumHands = Mathf.Min(currentNumHands + 1, maxNumHands);
        ApplyCurrentNumber();
    }

    public void DecreaseNumHands()
    {
        currentNumHands = Mathf.Max(currentNumHands - 1, minNumHands);
        ApplyCurrentNumber();
    }

    public void ClosePanel()
    {
        if (panelToDisable != null)
        {
            panelToDisable.SetActive(false);
        }
    }

    private void ApplyCurrentNumber()
    {
        if (numberText != null)
        {
            numberText.text = currentNumHands.ToString();
            
        }

        if (config != null)
        {
            config.NumHands = currentNumHands;
            
        }
    }
}
