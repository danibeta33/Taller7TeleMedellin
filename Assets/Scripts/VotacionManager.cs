using System.Collections;
using TMPro;
using UnityEngine;

public class VotacionManager : MonoBehaviour
{
    public enum VotingMode
    {
        Registration,
        FreeHands,
    }

    [Header("UI")]
    [SerializeField] private TMP_Text estadoText;
    [SerializeField] private TMP_Text temporizadorText;
    [SerializeField] private TMP_Text opcion1Text;
    [SerializeField] private TMP_Text opcion2Text;
    [SerializeField] private TMP_Text opcion3Text;

    [Header("Votacion")]
    [SerializeField] private float votingTimePerOptionSeconds = 6f;
    [SerializeField] private VotingMode votingMode = VotingMode.FreeHands;

    [Header("Visualizacion por opcion")]
    [SerializeField] private ActivacionDeManos[] activacionPorOpcion = new ActivacionDeManos[3];
    [SerializeField] private bool freezeActivacionOnFinish = true;

    [Header("Fuente Centralizada de Manos")]
    [SerializeField] private HandTrackingCenter handTrackingCenter;

    private readonly string[] optionLabels = { "Opcion A", "Opcion B", "Opcion C" };
    private readonly int[] votesPerOption = new int[3];
    private int lastWinnerIndex = -1;
    private Coroutine activeVotingCoroutine;
    private bool cancelRequested;

    public bool VotingInProgress { get; private set; }
    public VotingMode CurrentVotingMode => votingMode;

    private void Awake()
    {
        ResolveHandSource();
        ApplyOptionsText();
    }

    public IEnumerator StartVoting()
    {
        if (VotingInProgress)
        {
            yield break;
        }

        ResolveHandSource();
        cancelRequested = false;
        VotingInProgress = true;
        lastWinnerIndex = -1;

        for (var i = 0; i < votesPerOption.Length; i++)
        {
            votesPerOption[i] = 0;
        }

        PrepareActivationDisplays();

        ApplyOptionsText();

        if (!cancelRequested)
        {
            yield return StartCoroutine(VotingCoroutine());
        }

        UpdateStatusText("Votacion finalizada");
        UpdateTimerText("Votacion finalizada");

        if (freezeActivacionOnFinish)
        {
            FreezeAllActivationDisplays(true);
        }

        VotingInProgress = false;
        activeVotingCoroutine = null;
    }

    // Compatibilidad: mantiene firmas usadas por escenas previas.
    public void IniciarFlujoDeVotacion()
    {
        if (activeVotingCoroutine != null)
        {
            StopCoroutine(activeVotingCoroutine);
        }

        activeVotingCoroutine = StartCoroutine(StartVoting());
    }

    // Compatibilidad con UI antigua.
    public void IniciarVotacion()
    {
        IniciarFlujoDeVotacion();
    }

    public void DetenerVotacion()
    {
        if (!VotingInProgress)
        {
            return;
        }

        cancelRequested = true;
    }

    public void ResetVotingData()
    {
        cancelRequested = true;

        if (activeVotingCoroutine != null)
        {
            StopCoroutine(activeVotingCoroutine);
            activeVotingCoroutine = null;
        }

        VotingInProgress = false;
        lastWinnerIndex = -1;

        for (var i = 0; i < votesPerOption.Length; i++)
        {
            votesPerOption[i] = 0;
        }

        UpdateStatusText(string.Empty);
        UpdateTimerText(string.Empty);
        FreezeAllActivationDisplays(false);
    }

    public int GetWinnerIndex()
    {
        return lastWinnerIndex;
    }

    public void SetVotingMode(VotingMode mode)
    {
        votingMode = mode;
    }

    public int ObtenerVotosOpcion(int indice)
    {
        var safeIndex = Mathf.Clamp(indice, 0, votesPerOption.Length - 1);
        return votesPerOption[safeIndex];
    }

    public int[] GetVotesSnapshot()
    {
        var snapshot = new int[votesPerOption.Length];
        for (var i = 0; i < votesPerOption.Length; i++)
        {
            snapshot[i] = votesPerOption[i];
        }
        return snapshot;
    }

    public void SetOptionsLabels(string labelA, string labelB, string labelC)
    {
        optionLabels[0] = string.IsNullOrWhiteSpace(labelA) ? "Opcion A" : labelA;
        optionLabels[1] = string.IsNullOrWhiteSpace(labelB) ? "Opcion B" : labelB;
        optionLabels[2] = string.IsNullOrWhiteSpace(labelC) ? "Opcion C" : labelC;
        ApplyOptionsText();
    }

    private IEnumerator VotingCoroutine()
    {
        for (var i = 0; i < optionLabels.Length; i++)
        {
            if (cancelRequested)
            {
                yield break;
            }

            var voteWindow = Mathf.Max(0.1f, votingTimePerOptionSeconds);
            var maxThumbsUp = 0;

            ActivateDisplayForOption(i);

            UpdateStatusText("Vota ahora por: " + optionLabels[i]);

            while (voteWindow > 0f)
            {
                voteWindow -= Time.deltaTime;
                if (voteWindow < 0f)
                {
                    voteWindow = 0f;
                }

                var currentThumbsUp = GetCurrentHandsCountByMode();
                if (currentThumbsUp > maxThumbsUp)
                {
                    maxThumbsUp = currentThumbsUp;
                }

                PushLiveCountToOption(i, currentThumbsUp);

                UpdateTimerText(Mathf.CeilToInt(voteWindow).ToString());
                yield return null;
            }

            votesPerOption[i] = Mathf.Max(0, maxThumbsUp);
            PushLiveCountToOption(i, votesPerOption[i]);
            FreezeDisplayForOption(i, true);
        }

        ResolveWinner();
    }

    private void ResolveWinner()
    {
        var bestScore = int.MinValue;
        var bestIndex = 0;

        for (var i = 0; i < votesPerOption.Length; i++)
        {
            if (votesPerOption[i] <= bestScore)
            {
                continue;
            }

            bestScore = votesPerOption[i];
            bestIndex = i;
        }

        lastWinnerIndex = bestIndex;
        UpdateStatusText("Ganador: " + optionLabels[bestIndex] + " (" + votesPerOption[bestIndex] + " votos)");
    }

    private int GetCurrentHandsCountByMode()
    {
        if (handTrackingCenter == null)
        {
            ResolveHandSource();
        }

        if (handTrackingCenter == null)
        {
            return 0;
        }

        if (votingMode == VotingMode.Registration)
        {
            return handTrackingCenter.GetThumbsUpHandsCount();
        }

        return handTrackingCenter.GetDetectedHandsCount();
    }

    private void PrepareActivationDisplays()
    {
        if (activacionPorOpcion == null)
        {
            return;
        }

        for (var i = 0; i < activacionPorOpcion.Length; i++)
        {
            var activacion = activacionPorOpcion[i];
            if (activacion == null)
            {
                continue;
            }

            activacion.ReiniciarConteo();
            activacion.SetUseExternalCount(true);
            activacion.FreezeCurrentState(false);
            activacion.SetExternalHandsCount(0);
        }
    }

    private void ActivateDisplayForOption(int optionIndex)
    {
        if (activacionPorOpcion == null)
        {
            return;
        }

        for (var i = 0; i < activacionPorOpcion.Length; i++)
        {
            var activacion = activacionPorOpcion[i];
            if (activacion == null)
            {
                continue;
            }

            if (i == optionIndex)
            {
                activacion.SetUseExternalCount(true);
                activacion.FreezeCurrentState(false);
                continue;
            }

            activacion.FreezeCurrentState(true);
        }
    }

    private void PushLiveCountToOption(int optionIndex, int count)
    {
        if (activacionPorOpcion == null || optionIndex < 0 || optionIndex >= activacionPorOpcion.Length)
        {
            return;
        }

        var activacion = activacionPorOpcion[optionIndex];
        if (activacion == null)
        {
            return;
        }

        activacion.SetExternalHandsCount(Mathf.Max(0, count));
    }

    private void FreezeDisplayForOption(int optionIndex, bool freeze)
    {
        if (activacionPorOpcion == null || optionIndex < 0 || optionIndex >= activacionPorOpcion.Length)
        {
            return;
        }

        var activacion = activacionPorOpcion[optionIndex];
        if (activacion == null)
        {
            return;
        }

        activacion.FreezeCurrentState(freeze);
    }

    private void FreezeAllActivationDisplays(bool freeze)
    {
        if (activacionPorOpcion == null)
        {
            return;
        }

        for (var i = 0; i < activacionPorOpcion.Length; i++)
        {
            var activacion = activacionPorOpcion[i];
            if (activacion == null)
            {
                continue;
            }

            activacion.FreezeCurrentState(freeze);
        }
    }

    private void ResolveHandSource()
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
    }

    private void ApplyOptionsText()
    {
        if (opcion1Text != null)
        {
            opcion1Text.text = optionLabels[0];
        }

        if (opcion2Text != null)
        {
            opcion2Text.text = optionLabels[1];
        }

        if (opcion3Text != null)
        {
            opcion3Text.text = optionLabels[2];
        }
    }

    private void UpdateStatusText(string text)
    {
        if (estadoText != null)
        {
            estadoText.text = text;
        }
    }

    private void UpdateTimerText(string text)
    {
        if (temporizadorText != null)
        {
            temporizadorText.text = text;
        }
    }
}
