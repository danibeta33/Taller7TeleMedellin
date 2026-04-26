using UnityEngine;

public class ActivacionDeManos : MonoBehaviour
{
    [Header("Fuente de Hand Tracking")]
    [SerializeField] private HandTrackingCenter handTrackingCenter;
    [SerializeField] private bool contarSoloPulgarArriba = true;
    [SerializeField] private bool useExternalCount;

    [Header("Objetos por cantidad de manos")]
    [SerializeField] private GameObject[] objetosPorCantidad;

    [Header("Suavizado")]
    [SerializeField] private float stabilityTime = 1.5f;
    [SerializeField] private bool freezeOnFinish;

    [Header("Contador (solo lectura)")]
    [SerializeField] private int conteoManosActual;
    [SerializeField] private int conteoManosAceptado;

    private int externalHandsCount;
    private int conteoPendiente = -1;
    private float tiempoEstable;
    private bool isFrozen;

    public int ConteoManosActual => conteoManosActual;
    public int ConteoManosAceptado => conteoManosAceptado;

    private void Awake()
    {
        ResolverHandTrackingCenter();
        ReiniciarConteo();
    }

    private void OnEnable()
    {
        ReiniciarConteo();
    }

    private void Update()
    {
        if (isFrozen)
        {
            return;
        }

        var conteoCrudo = ObtenerConteoCrudo();
        conteoManosActual = conteoCrudo;

        ActualizarConteoSuavizado(conteoCrudo);
    }

    public void ReiniciarConteo()
    {
        conteoManosActual = 0;
        conteoManosAceptado = 0;
        externalHandsCount = 0;
        conteoPendiente = 0;
        tiempoEstable = 0f;
        isFrozen = false;
        AplicarActivacionPorConteo(0);
    }

    public void SetUseExternalCount(bool enabled)
    {
        useExternalCount = enabled;
    }

    public void SetExternalHandsCount(int count)
    {
        externalHandsCount = Mathf.Max(0, count);
    }

    public void FreezeCurrentState(bool freeze)
    {
        isFrozen = freeze;
    }

    public void FinishActivationPhase()
    {
        if (freezeOnFinish)
        {
            isFrozen = true;
        }
    }

    private int ObtenerConteoCrudo()
    {
        if (useExternalCount)
        {
            return externalHandsCount;
        }

        if (handTrackingCenter == null)
        {
            return 0;
        }

        if (contarSoloPulgarArriba)
        {
            return handTrackingCenter.GetThumbsUpHandsCount();
        }

        return handTrackingCenter.GetDetectedHandsCount();
    }

    private void ActualizarConteoSuavizado(int conteoCrudo)
    {
        if (conteoCrudo == conteoManosAceptado)
        {
            conteoPendiente = conteoCrudo;
            tiempoEstable = 0f;
            return;
        }

        if (conteoCrudo != conteoPendiente)
        {
            conteoPendiente = conteoCrudo;
            tiempoEstable = 0f;
            return;
        }

        tiempoEstable += Time.deltaTime;
        if (tiempoEstable >= Mathf.Max(0.05f, stabilityTime))
        {
            conteoManosAceptado = conteoPendiente;
            tiempoEstable = 0f;
            AplicarActivacionPorConteo(conteoManosAceptado);
        }
    }

    private void AplicarActivacionPorConteo(int conteo)
    {
        if (objetosPorCantidad == null || objetosPorCantidad.Length == 0)
        {
            return;
        }

        var activos = Mathf.Clamp(conteo, 0, objetosPorCantidad.Length);

        for (var i = 0; i < objetosPorCantidad.Length; i++)
        {
            var objetivo = objetosPorCantidad[i];
            if (objetivo == null)
            {
                continue;
            }

            var debeEstarActivo = i < activos;
            if (objetivo.activeSelf != debeEstarActivo)
            {
                objetivo.SetActive(debeEstarActivo);
            }
        }
    }

    private void ResolverHandTrackingCenter()
    {
        if (handTrackingCenter != null)
        {
            return;
        }

        handTrackingCenter = FindFirstObjectByType<HandTrackingCenter>();
    }
}
