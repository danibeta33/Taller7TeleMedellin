using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private float tieMessageSeconds = 2f;
    [SerializeField] private float winnerMessageSeconds = 2f;

    [Header("Lectura por opcion")]
    [SerializeField] private float optionWarmupSeconds = 0.35f;

    [Header("Visualizacion por opcion")]
    [SerializeField] private ActivacionDeManos[] activacionPorOpcion = new ActivacionDeManos[3];
    [SerializeField] private bool freezeActivacionOnFinish = true;

    [Header("Fuente Centralizada de Manos")]
    [SerializeField] private HandTrackingCenter handTrackingCenter;

    private readonly string[] optionLabels = { "Opcion A", "Opcion B", "Opcion C" };
    private readonly int[] votesPerOption = new int[3];
    private readonly List<int> lastTiedWinnerIndices = new List<int>();
    private int lastWinnerIndex = -1;
    private Coroutine activeVotingCoroutine;
    private bool cancelRequested;
    private bool lastVoteWasTie;

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

        if (!lastVoteWasTie)
        {
            UpdateStatusText("Votacion finalizada");
            UpdateTimerText("Votacion finalizada");
        }

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
        lastVoteWasTie = false;
        lastTiedWinnerIndices.Clear();

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

    public bool WasLastVoteTie()
    {
        return lastVoteWasTie;
    }

    public int[] GetLastTiedWinnerIndicesSnapshot()
    {
        return lastTiedWinnerIndices.ToArray();
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
            var elapsedInOption = 0f;
            var optionSamples = new Dictionary<int, int>();
            var lastObservedHands = 0;

            ActivateDisplayForOption(i);

            UpdateStatusText("Vota ahora por: " + optionLabels[i]);

            while (voteWindow > 0f)
            {
                elapsedInOption += Time.deltaTime;
                voteWindow -= Time.deltaTime;
                if (voteWindow < 0f)
                {
                    voteWindow = 0f;
                }

                // Lectura directa de manos por frame para no mezclar estados entre opciones.
                var currentThumbsUp = Mathf.Max(0, GetCurrentHandsCountByMode());
                lastObservedHands = currentThumbsUp;

                // Evita arrastre de frames de transicion entre opciones.
                if (elapsedInOption >= Mathf.Max(0f, optionWarmupSeconds))
                {
                    RegisterOptionSample(optionSamples, currentThumbsUp);
                }

                PushLiveCountToOption(i, currentThumbsUp);

                UpdateTimerText(Mathf.CeilToInt(voteWindow).ToString());
                yield return null;
            }

            votesPerOption[i] = ResolveVoteFromOptionSamples(optionSamples, lastObservedHands);
            Debug.Log($"[VotacionManager] Opcion {i + 1} muestras: {FormatOptionSamples(optionSamples)} => voto: {votesPerOption[i]}");
            PushLiveCountToOption(i, votesPerOption[i]);
            FreezeDisplayForOption(i, true);
        }

        yield return StartCoroutine(ResolveWinnerCoroutine());
    }

    private IEnumerator ResolveWinnerCoroutine()
    {
        lastTiedWinnerIndices.Clear();

        var maxVotes = 0;
        for (var i = 0; i < votesPerOption.Length; i++)
        {
            if (votesPerOption[i] > maxVotes)
            {
                maxVotes = votesPerOption[i];
            }
        }

        for (var i = 0; i < votesPerOption.Length; i++)
        {
            if (votesPerOption[i] == maxVotes)
            {
                lastTiedWinnerIndices.Add(i);
            }
        }

        Debug.Log($"[VotacionManager] Votos finales => A:{votesPerOption[0]} B:{votesPerOption[1]} C:{votesPerOption[2]} | Max:{maxVotes}");

        if (lastTiedWinnerIndices.Count == 0)
        {
            lastWinnerIndex = 0;
            lastVoteWasTie = false;
            UpdateStatusText("Ganador: " + optionLabels[0] + " (" + votesPerOption[0] + " votos)");
            yield break;
        }

        // Solo se considera empate cuando hay mas de una opcion con el mismo voto maximo.
        lastVoteWasTie = lastTiedWinnerIndices.Count > 1;
        var randomChoice = Random.Range(0, lastTiedWinnerIndices.Count);
        lastWinnerIndex = lastTiedWinnerIndices[randomChoice];

        if (!lastVoteWasTie)
        {
            UpdateStatusText("Ganador: " + optionLabels[lastWinnerIndex] + " (" + votesPerOption[lastWinnerIndex] + " votos)");
            yield break;
        }

        UpdateTimerText("0");
        UpdateStatusText("Empate, Elijiendo al azar");

        // Existing scene instances may have these serialized values as 0.
        var tieWait = tieMessageSeconds > 0f ? tieMessageSeconds : 2f;
        if (tieWait > 0f)
        {
            yield return new WaitForSeconds(tieWait);
        }

        UpdateStatusText("Gano la opcion #" + (lastWinnerIndex + 1));

        var winnerWait = winnerMessageSeconds > 0f ? winnerMessageSeconds : 2f;
        if (winnerWait > 0f)
        {
            yield return new WaitForSeconds(winnerWait);
        }

        UpdateTimerText("0");
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

    private static void RegisterOptionSample(Dictionary<int, int> samples, int hands)
    {
        if (samples.TryGetValue(hands, out var count))
        {
            samples[hands] = count + 1;
            return;
        }

        samples[hands] = 1;
    }

    private static int ResolveVoteFromOptionSamples(Dictionary<int, int> samples, int fallbackHands)
    {
        if (samples == null || samples.Count == 0)
        {
            return Mathf.Max(0, fallbackHands);
        }

        var bestHands = 0;
        var bestFrequency = -1;

        foreach (var pair in samples)
        {
            var hands = pair.Key;
            var frequency = pair.Value;

            if (frequency > bestFrequency)
            {
                bestFrequency = frequency;
                bestHands = hands;
                continue;
            }

            // Si hay empate de frecuencia, preferimos el menor conteo para evitar sobrelecturas por picos transitorios.
            if (frequency == bestFrequency && hands < bestHands)
            {
                bestHands = hands;
            }
        }

        return Mathf.Max(0, bestHands);
    }

    private static string FormatOptionSamples(Dictionary<int, int> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            return "sin-muestras";
        }

        var text = string.Empty;
        var first = true;
        foreach (var pair in samples)
        {
            if (!first)
            {
                text += ", ";
            }

            text += pair.Key + "=>" + pair.Value;
            first = false;
        }

        return text;
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
