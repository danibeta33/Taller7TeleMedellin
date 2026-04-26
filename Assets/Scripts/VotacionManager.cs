using System.Collections.Generic;
using Mediapipe;
using TMPro;
using UnityEngine;

public class VotacionManager : MonoBehaviour
{
    private enum EstadoVotacion
    {
        Waiting,
        Countdown,
        Voting,
        Finished,
    }

    [Header("Opciones de votacion (texto configurable)")]
    [SerializeField] private string opcion1 = "Opcion 1";
    [SerializeField] private string opcion2 = "Opcion 2";
    [SerializeField] private string opcion3 = "Opcion 3";

    [Header("Textos UI (TMP)")]
    [SerializeField] private TMP_Text opcion1Text;
    [SerializeField] private TMP_Text opcion2Text;
    [SerializeField] private TMP_Text opcion3Text;
    [SerializeField] private TMP_Text temporizadorText;
    [SerializeField] private TMP_Text estadoText;

    [Header("Configuracion de votacion")]
    [SerializeField] private float duracionVotacionSegundos = 10f;
    [SerializeField] private float duracionCuentaRegresivaSegundos = 3f;
    [SerializeField] private float intervaloDebugSegundos = 5f;
    [SerializeField] private int opcionActiva = 0;

    [Header("Fuente Centralizada de Manos")]
    [SerializeField] private HandTrackingCenter handTrackingCenter;

    [Header("Integracion con IngresoManager")]
    [SerializeField] private GameObject numberManagerPanel;

    private bool votacionActiva;
    private EstadoVotacion estadoActual = EstadoVotacion.Waiting;
    private float tiempoRestante;
    private float acumuladorVoto;
    private float acumuladorDebug;
    private int manosDetectadasActuales;
    private int manosIniciales;
    private readonly int[] votosPorOpcion = new int[3];
    private Coroutine flujoVotacionCoroutine;
    private bool esperandoCierrePanel;
    private bool panelEstabaActivo;
    void Start()
    {
        // Inicializa textos configurables de las 3 opciones.
        ActualizarTextosOpciones();
        ResolverFuenteManos();
        ActualizarTextoTemporizador(0f, false);
        ActualizarEstadoTexto("Esperando inicio...");
        panelEstabaActivo = numberManagerPanel != null && numberManagerPanel.activeSelf;
    }

    void Update()
    {
        // NUEVO: Si el panel pasa de activo a inactivo, arranca automaticamente el flujo.
        if (estadoActual == EstadoVotacion.Waiting && !esperandoCierrePanel && numberManagerPanel != null)
        {
            var panelActivoAhora = numberManagerPanel.activeSelf;
            if (panelEstabaActivo && !panelActivoAhora)
            {
                IniciarFlujoDeVotacion();
            }
            panelEstabaActivo = panelActivoAhora;
        }

        // El countdown inicia solo cuando el panel de IngresoManager ya esta desactivado.
        if (estadoActual == EstadoVotacion.Waiting && esperandoCierrePanel && numberManagerPanel != null && !numberManagerPanel.activeSelf)
        {
            esperandoCierrePanel = false;
            if (flujoVotacionCoroutine != null)
            {
                StopCoroutine(flujoVotacionCoroutine);
            }
            flujoVotacionCoroutine = StartCoroutine(CuentaRegresivaEIniciarVotacionCoroutine());
        }
    }

    // NUEVO: Inicia el flujo completo previo a la votacion.
    public void IniciarFlujoDeVotacion()
    {
        if (estadoActual == EstadoVotacion.Countdown || estadoActual == EstadoVotacion.Voting)
        {
            return;
        }

        LeerManosDesdeMediaPipeExistente();
        manosIniciales = manosDetectadasActuales;

        if (flujoVotacionCoroutine != null)
        {
            StopCoroutine(flujoVotacionCoroutine);
        }

        if (numberManagerPanel == null)
        {
            // Si no hay panel asignado, conserva el flujo previo.
            flujoVotacionCoroutine = StartCoroutine(CuentaRegresivaEIniciarVotacionCoroutine());
            return;
        }

        if (!numberManagerPanel.activeSelf)
        {
            flujoVotacionCoroutine = StartCoroutine(CuentaRegresivaEIniciarVotacionCoroutine());
            return;
        }

        esperandoCierrePanel = true;
        ActualizarEstadoTexto("Esperando cierre del panel...");
    }

    // Permite cambiar la opcion que recibira votos (0, 1 o 2).
    public void SeleccionarOpcion(int indice)
    {
        opcionActiva = Mathf.Clamp(indice, 0, 2);
    }

    public void IniciarVotacion()
    {
        votosPorOpcion[0] = 0;
        votosPorOpcion[1] = 0;
        votosPorOpcion[2] = 0;

        if (flujoVotacionCoroutine != null)
        {
            StopCoroutine(flujoVotacionCoroutine);
        }

        flujoVotacionCoroutine = StartCoroutine(SecuenciaVotacionPorOpcionesCoroutine());
    }

    public void DetenerVotacion()
    {
        if (!votacionActiva)
        {
            return;
        }

        votacionActiva = false;
        estadoActual = EstadoVotacion.Finished;
        ActualizarTextoTemporizador(0f, false);
        ActualizarEstadoTexto("Votacion finalizada");

        Debug.Log(
            "Votacion finalizada | " +
            opcion1 + ": " + votosPorOpcion[0] + " votos | " +
            opcion2 + ": " + votosPorOpcion[1] + " votos | " +
            opcion3 + ": " + votosPorOpcion[2] + " votos");
    }

    // Punto de entrada opcional si ya recibes MultiHandLandmarkList/lista desde otro evento.
    public void ActualizarDesdeMultiHandLandmarkList(IReadOnlyList<NormalizedLandmarkList> multiHandLandmarkList)
    {
        manosDetectadasActuales = multiHandLandmarkList == null ? 0 : multiHandLandmarkList.Count;
    }

    public int ObtenerVotosOpcion(int indice)
    {
        var i = Mathf.Clamp(indice, 0, 2);
        return votosPorOpcion[i];
    }

    private void ActualizarTextosOpciones()
    {
        if (opcion1Text != null) opcion1Text.text = opcion1;
        if (opcion2Text != null) opcion2Text.text = opcion2;
        if (opcion3Text != null) opcion3Text.text = opcion3;
    }

    private void ResolverFuenteManos()
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

    private void LeerManosDesdeMediaPipeExistente()
    {
        if (handTrackingCenter == null)
        {
            ResolverFuenteManos();
        }

        if (handTrackingCenter == null)
        {
            manosDetectadasActuales = 0;
            return;
        }

        manosDetectadasActuales = handTrackingCenter.GetHands().Count;
    }

    private void ProcesarTemporizador()
    {
        tiempoRestante -= Time.deltaTime;
        if (tiempoRestante <= 0f)
        {
            tiempoRestante = 0f;
            ActualizarTextoTemporizador(0f, false);
            DetenerVotacion();
            return;
        }

        ActualizarTextoTemporizador(tiempoRestante, true);
    }

    // Cuenta regresiva previa 3 -> 2 -> 1 -> 0 antes de habilitar votos.
    private System.Collections.IEnumerator CuentaRegresivaEIniciarVotacionCoroutine()
    {
        estadoActual = EstadoVotacion.Countdown;
        ActualizarEstadoTexto("Preparando votacion...");

        var pasos = Mathf.Max(1, Mathf.RoundToInt(duracionCuentaRegresivaSegundos));
        for (var i = pasos; i >= 1; i--)
        {
            if (temporizadorText != null)
            {
                temporizadorText.text = i.ToString();
            }
            yield return new WaitForSeconds(1f);
        }

        if (temporizadorText != null)
        {
            temporizadorText.text = "0";
        }

        IniciarVotacion();
    }

    // NUEVO: Ejecuta opcion 1, luego 2, luego 3 con 10 segundos cada una.
    private System.Collections.IEnumerator SecuenciaVotacionPorOpcionesCoroutine()
    {
        votacionActiva = true;
        estadoActual = EstadoVotacion.Voting;

        for (var indiceOpcion = 0; indiceOpcion < 3; indiceOpcion++)
        {
            opcionActiva = indiceOpcion;
            votosPorOpcion[indiceOpcion] = 0;
            manosDetectadasActuales = 0;

            tiempoRestante = Mathf.Max(0.1f, duracionVotacionSegundos);
            acumuladorDebug = 0f;

            var textoOpcion = ObtenerTextoOpcion(indiceOpcion);
            ActualizarEstadoTexto("Es tiempo para la opcion " + (indiceOpcion + 1) + ": " + textoOpcion);

            while (tiempoRestante > 0f)
            {
                LeerManosDesdeMediaPipeExistente();

                tiempoRestante -= Time.deltaTime;
                if (tiempoRestante < 0f)
                {
                    tiempoRestante = 0f;
                }
                ActualizarTextoTemporizador(tiempoRestante, true);

                ProcesarDebugPeriodico();
                yield return null;
            }

            // El voto de la opcion se toma al finalizar su ventana de tiempo.
            LeerManosDesdeMediaPipeExistente();
            votosPorOpcion[indiceOpcion] = Mathf.Max(0, manosDetectadasActuales);

            Debug.Log("[VotacionManager] Resultado opcion " + (indiceOpcion + 1) + " (" + textoOpcion + "): " + votosPorOpcion[indiceOpcion] + " votos");
        }

        DetenerVotacion();
    }

    private void ProcesarDebugPeriodico()
    {
        if (intervaloDebugSegundos <= 0f)
        {
            return;
        }

        acumuladorDebug += Time.deltaTime;
        if (acumuladorDebug < intervaloDebugSegundos)
        {
            return;
        }

        acumuladorDebug -= intervaloDebugSegundos;
        Debug.Log("[VotacionManager] Manos detectadas actualmente: " + manosDetectadasActuales + " | manos iniciales: " + manosIniciales);
    }

    private void ActualizarTextoTemporizador(float segundos, bool activa)
    {
        if (temporizadorText == null)
        {
            return;
        }

        if (!activa)
        {
            temporizadorText.text = "Votacion finalizada";
            return;
        }

        temporizadorText.text = "Tiempo restante: " + Mathf.CeilToInt(segundos) + "s";
    }

    private void ActualizarEstadoTexto(string mensaje)
    {
        if (estadoText != null)
        {
            estadoText.text = mensaje;
        }
    }

    private string ObtenerTextoOpcion(int indice)
    {
        switch (indice)
        {
            case 0:
                return opcion1;
            case 1:
                return opcion2;
            case 2:
                return opcion3;
            default:
                return "Opcion";
        }
    }
}
