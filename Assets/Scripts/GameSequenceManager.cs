using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

public class GameSequenceManager : MonoBehaviour
{
    [Header("Referencias Principales")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private IngresoManager ingresoManager;
    [SerializeField] private VotacionManager votacionManager;

    [Header("Paneles")]
    [SerializeField] private GameObject panelDeteccionJugadores;
    [SerializeField] private GameObject panelVideo;
    [SerializeField] private GameObject panelVotacion;

    [Header("UI")]
    [SerializeField] private TMP_Text opcionLabelText;

    [Header("Ciclos")]
    [SerializeField] private List<CycleData> cycles = new List<CycleData>();

    [Header("Flujo")]
    [SerializeField] private bool autoStartOnEnable = true;
    [SerializeField] private float maxWaitPlayerDetectionSeconds = 90f;

    [Header("Debug")]
    [SerializeField] private bool enableLogs = true;

    private Coroutine sequenceCoroutine;
    private bool isPaused;

    public int DetectedPlayers { get; private set; }
    public int CurrentCycleIndex { get; private set; } = -1;
    public bool IsPaused => isPaused;

    private void OnEnable()
    {
        if (autoStartOnEnable)
        {
            StartSequence();
        }
    }

    public void StartSequence()
    {
        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
        }

        sequenceCoroutine = StartCoroutine(GameFlowCoroutine());
    }

    public void StopSequence()
    {
        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }

        HideAllPanels();
        ClearPreviewLabel();
    }

    public void PauseFlow()
    {
        isPaused = true;

        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }
    }

    public void ResumeFlow()
    {
        isPaused = false;

        if (videoPlayer != null && videoPlayer.clip != null && !videoPlayer.isPlaying)
        {
            videoPlayer.Play();
        }
    }

    public void ResetExperience()
    {
        isPaused = false;
        StopSequence();

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.clip = null;
        }

        if (votacionManager != null)
        {
            votacionManager.DetenerVotacion();
            votacionManager.ResetVotingData();
        }

        CurrentCycleIndex = -1;
        DetectedPlayers = 0;
        StartSequence();
    }

    private IEnumerator GameFlowCoroutine()
    {
        Log("[GameSequenceManager] Iniciando flujo principal");
        CurrentCycleIndex = -1;
        DetectedPlayers = 0;
        HideAllPanels();
        ClearPreviewLabel();

        yield return StartCoroutine(RunRegistrationPhase());

        for (var i = 0; i < cycles.Count; i++)
        {
            var cycle = cycles[i];
            if (cycle == null)
            {
                continue;
            }

            CurrentCycleIndex = i;
            Log("[GameSequenceManager] Iniciando ciclo " + (i + 1));

            SetOnlyPanelActive(panelVideo);
            ClearPreviewLabel();
            yield return StartCoroutine(PlayClipCoroutine(cycle.introVideo));

            yield return StartCoroutine(PreviewOptionCoroutine(cycle.labelA, cycle.optionA));
            yield return StartCoroutine(PreviewOptionCoroutine(cycle.labelB, cycle.optionB));
            yield return StartCoroutine(PreviewOptionCoroutine(cycle.labelC, cycle.optionC));

            SetOnlyPanelActive(panelVotacion);
            if (votacionManager != null)
            {
                votacionManager.SetVotingMode(VotacionManager.VotingMode.FreeHands);
                votacionManager.SetOptionsLabels(cycle.labelA, cycle.labelB, cycle.labelC);
                yield return StartCoroutine(votacionManager.StartVoting());
            }

            var winnerIndex = votacionManager != null ? votacionManager.GetWinnerIndex() : 0;
            var winnerClip = ResolveWinnerClip(cycle, winnerIndex);
            var winnerLabel = ResolveWinnerLabel(cycle, winnerIndex);

            SetOnlyPanelActive(panelVideo);
            SetPreviewLabel("Ganadora: " + winnerLabel);
            yield return StartCoroutine(PlayClipCoroutine(winnerClip));
        }

        HideAllPanels();
        ClearPreviewLabel();
        Log("[GameSequenceManager] Experiencia finalizada");

        sequenceCoroutine = null;
    }

    private IEnumerator RunRegistrationPhase()
    {
        if (panelDeteccionJugadores == null)
        {
            Log("[GameSequenceManager] Panel de deteccion no asignado, se omite fase 1");
            yield break;
        }

        SetOnlyPanelActive(panelDeteccionJugadores);

        if (ingresoManager != null)
        {
            ingresoManager.StartPlayerDetection();
            Log("[GameSequenceManager] Deteccion de jugadores iniciada");
        }
        else if (ingresoManager == null)
        {
            Log("[GameSequenceManager] IngresoManager no asignado, esperando cierre manual del panel");
        }

        var waitTime = 0f;
        var maxWait = Mathf.Max(1f, maxWaitPlayerDetectionSeconds);

        while (panelDeteccionJugadores.activeSelf)
        {
            waitTime += Time.deltaTime;

            if (ingresoManager != null && ingresoManager.IsFinished())
            {
                Log("[GameSequenceManager] IngresoManager finalizado");
                break;
            }

            if (waitTime >= maxWait)
            {
                Log("[GameSequenceManager] Timeout en deteccion de jugadores");
                break;
            }

            yield return null;
        }

        if (panelDeteccionJugadores.activeSelf)
        {
            panelDeteccionJugadores.SetActive(false);
        }

        DetectedPlayers = ingresoManager != null ? ingresoManager.FinalPlayerCount : 0;
        Log("[GameSequenceManager] Jugadores detectados: " + DetectedPlayers);
    }

    private IEnumerator PlayClipCoroutine(VideoClip clip)
    {
        if (videoPlayer == null || clip == null)
        {
            Log("[GameSequenceManager] VideoPlayer o clip nulo");
            yield break;
        }

        videoPlayer.Stop();
        videoPlayer.clip = clip;
        videoPlayer.isLooping = false;
        videoPlayer.Prepare();

        while (!videoPlayer.isPrepared)
        {
            yield return null;
        }

        videoPlayer.Play();
        yield return null;

        var endTime = Mathf.Max(0f, (float)clip.length - 0.05f);
        while (true)
        {
            if (isPaused)
            {
                yield return null;
                continue;
            }

            if (!videoPlayer.isPlaying && videoPlayer.time >= endTime)
            {
                break;
            }

            yield return null;
        }
    }

    private IEnumerator PreviewOptionCoroutine(string label, VideoClip clip)
    {
        SetOnlyPanelActive(panelVideo);
        SetPreviewLabel(string.IsNullOrWhiteSpace(label) ? "Opcion" : label);
        yield return StartCoroutine(PlayClipCoroutine(clip));
    }

    private VideoClip ResolveWinnerClip(CycleData cycle, int winnerIndex)
    {
        if (cycle == null)
        {
            return null;
        }

        switch (winnerIndex)
        {
            case 0:
                return cycle.optionA;
            case 1:
                return cycle.optionB;
            case 2:
                return cycle.optionC;
            default:
                return cycle.optionA;
        }
    }

    private string ResolveWinnerLabel(CycleData cycle, int winnerIndex)
    {
        if (cycle == null)
        {
            return "Opcion";
        }

        switch (winnerIndex)
        {
            case 0:
                return string.IsNullOrWhiteSpace(cycle.labelA) ? "Opcion A" : cycle.labelA;
            case 1:
                return string.IsNullOrWhiteSpace(cycle.labelB) ? "Opcion B" : cycle.labelB;
            case 2:
                return string.IsNullOrWhiteSpace(cycle.labelC) ? "Opcion C" : cycle.labelC;
            default:
                return string.IsNullOrWhiteSpace(cycle.labelA) ? "Opcion A" : cycle.labelA;
        }
    }

    private void SetOnlyPanelActive(GameObject panelToActivate)
    {
        if (panelDeteccionJugadores != null)
        {
            panelDeteccionJugadores.SetActive(panelToActivate == panelDeteccionJugadores);
        }

        if (panelVideo != null)
        {
            panelVideo.SetActive(panelToActivate == panelVideo);
        }

        if (panelVotacion != null)
        {
            panelVotacion.SetActive(panelToActivate == panelVotacion);
        }
    }

    private void HideAllPanels()
    {
        if (panelDeteccionJugadores != null)
        {
            panelDeteccionJugadores.SetActive(false);
        }

        if (panelVideo != null)
        {
            panelVideo.SetActive(false);
        }

        if (panelVotacion != null)
        {
            panelVotacion.SetActive(false);
        }
    }

    private void SetPreviewLabel(string value)
    {
        if (opcionLabelText != null)
        {
            opcionLabelText.text = value;
        }
    }

    private void ClearPreviewLabel()
    {
        if (opcionLabelText != null)
        {
            opcionLabelText.text = string.Empty;
        }
    }

    private void Log(string message)
    {
        if (!enableLogs)
        {
            return;
        }

        Debug.Log(message);
    }
}