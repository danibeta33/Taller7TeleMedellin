using UnityEngine;

public class ActivacionDeManos : MonoBehaviour
{
    [Header("Fuente de Hand Tracking")]
    [SerializeField] private HandTrackingCenter handTrackingCenter;
    [SerializeField] private bool contarSoloPulgarArriba = true;

    [Header("Objetos por cantidad de manos")]
    [SerializeField] private GameObject[] objetosPorCantidad;

    [Header("Suavizado")]
    [SerializeField] private int framesParaAceptarConteo = 4;

    [Header("Contador (solo lectura)")]
    [SerializeField] private int conteoManosActual;
    [SerializeField] private int conteoManosAceptado;

    private int ultimoConteoCrudo = -1;
    private int framesConMismoConteo;

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
        var conteoCrudo = ObtenerConteoCrudo();
        conteoManosActual = conteoCrudo;

        ActualizarConteoAceptado(conteoCrudo);
        AplicarActivacionPorConteo(conteoManosAceptado);
    }

    public void ReiniciarConteo()
    {
        conteoManosActual = 0;
        conteoManosAceptado = 0;
        ultimoConteoCrudo = -1;
        framesConMismoConteo = 0;
        AplicarActivacionPorConteo(0);
    }

    private int ObtenerConteoCrudo()
    {
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

    private void ActualizarConteoAceptado(int conteoCrudo)
    {
        if (conteoCrudo != ultimoConteoCrudo)
        {
            ultimoConteoCrudo = conteoCrudo;
            framesConMismoConteo = 1;
            return;
        }

        framesConMismoConteo++;
        if (framesConMismoConteo >= Mathf.Max(1, framesParaAceptarConteo))
        {
            conteoManosAceptado = conteoCrudo;
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
