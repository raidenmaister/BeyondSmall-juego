using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mejoras del jugador que se crean con lo que sueltan los bichos.
/// Alva Majo: "no añadas cosas al tuntun"; antes Hueso, Piel, Colmillo, Cuerno, Alas, Aguijon y las patas
/// de ciempies se recogian y no servian para nada. Ahora cada una alimenta una mejora que refuerza los dos
/// verbos del juego, EXPLORAR (saltar mas, planear desde los muebles) y PELEAR (pegar mas fuerte, aguantar mas):
///   Guantes   (Colmillo o Aguijon + Piel)   golpes x1.5
///   Botas     (Pata de ciempies + Piel)      saltas un 35% mas alto
///   Planeador (Alas + Hueso)                 manten saltar en el aire para planear
///   Peto      (Cuerno + Exoesqueleto)        +50% de vida maxima
/// Como el escudo, funcionan mientras esten en la mochila.
/// La primera vez que recoges un ingrediente te dice que se puede crear con el (el jugador no es adivino).
/// </summary>
public class Mejoras : MonoBehaviour
{
    public const string Guantes = "Guantes", Botas = "Botas", Planeador = "Planeador", Peto = "Peto";

    public static bool TieneGuantes, TieneBotas, TienePlaneador, TienePeto;
    public static float MultDanio => TieneGuantes ? 1.5f : 1f;
    public static float MultSalto => TieneBotas ? 1.35f : 1f;

    static readonly Dictionary<string, string> descripcion = new Dictionary<string, string>
    {
        { Guantes, "Guantes de colmillo: tus golpes hacen un 50% mas de daño" },
        { Botas, "Botas de ciempies: saltas mucho mas alto" },
        { Planeador, "Planeador: manten SALTAR en el aire para planear" },
        { Peto, "Peto de cuerno: tu vida maxima sube un 50%" },
    };

    readonly HashSet<string> pistasDadas = new HashSet<string>();
    readonly HashSet<string> conocidas = new HashSet<string>();
    bool primeraVez = true, anuncio;

    PlayerHealth vida;
    bool vidaIniciada, petoAplicado;
    float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { TieneGuantes = TieneBotas = TienePlaneador = TienePeto = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Recetas()
    {
        // se añaden una sola vez a las recetas del inventario
        void Añadir(string a, string b, string r)
        {
            foreach (var x in Crafteo.Recetas) if (x.a == a && x.b == b) return;
            Crafteo.Recetas.Add(new Crafteo.Receta { a = a, b = b, resultado = r, cantidad = 1 });
        }
        Añadir("Colmillo", "Piel", Guantes);
        Añadir("Aguijon", "Piel", Guantes);
        Añadir("PataCiempies", "Piel", Botas);
        Añadir("Alas", "Hueso", Planeador);
        Añadir("Cuerno", "Exoesqueleto", Peto);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null && pc.GetComponent<Mejoras>() == null) pc.gameObject.AddComponent<Mejoras>();
    }

    void Start()
    {
        vida = GetComponent<PlayerHealth>();
        // iconos de las mejoras (Resources/Mejoras) en la base de iconos del inventario
        var inv = ControladorInventario.Instance != null ? ControladorInventario.Instance : FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv != null)
            foreach (var n in new[] { Guantes, Botas, Planeador, Peto })
            {
                var sp = Resources.Load<Sprite>("Mejoras/" + n);
                if (sp != null && !inv.iconDatabase.Contains(sp)) { sp.name = n; inv.iconDatabase.Add(sp); }
            }
    }

    void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.25f;
        var inv = ControladorInventario.Instance;
        if (inv == null) return;

        anuncio = false;
        bool g = inv.GetTotalCount(Guantes) > 0, b = inv.GetTotalCount(Botas) > 0, p = inv.GetTotalCount(Planeador) > 0, v = inv.GetTotalCount(Peto) > 0;
        Anunciar(Guantes, g); Anunciar(Botas, b); Anunciar(Planeador, p); Anunciar(Peto, v);
        TieneGuantes = g; TieneBotas = b; TienePlaneador = p;
        if (v != TienePeto) { TienePeto = v; AjustarVida(); }

        // pistas de recetas al recoger un ingrediente por primera vez (sin pisar el anuncio de una mejora)
        // (cualquier ingrediente de cualquier receta: el jugador siempre sabe para que sirve lo que recoge)
        foreach (var r in Crafteo.Recetas)
        {
            if (anuncio) break;
            string nuevo = null;
            foreach (var id in new[] { r.a, r.b })
                if (!pistasDadas.Contains(id) && inv.GetTotalCount(id) > 0) { nuevo = id; break; }
            if (nuevo == null) continue;
            pistasDadas.Add(nuevo);
            if (primeraVez || !Ajustes.Pistas) continue;
            // la receta mas util para ese ingrediente: mejor si ya tienes el otro
            Crafteo.Receta mejor = r; bool completa = false;
            foreach (var x in Crafteo.Recetas)
            {
                if (x.a != nuevo && x.b != nuevo) continue;
                string otro = x.a == nuevo ? x.b : x.a;
                if (inv.GetTotalCount(otro) > 0) { mejor = x; completa = true; break; }
            }
            string texto = Catalogo.Nombre(mejor.a) + " + " + Catalogo.Nombre(mejor.b) + " = " + Catalogo.Nombre(mejor.resultado);
            SurvivalUI.Aviso((completa ? "¡Ya puedes crear! " : "Idea: ") + texto + "   [" + Controles.NombreTecla(Controles.Accion.Inventario) + "] > RECETAS",
                             completa ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.85f, 0.5f));
            break;
        }
        primeraVez = false;   // lo que ya traias al cargar la partida no se anuncia
    }

    void Anunciar(string id, bool tiene)
    {
        if (!tiene) { conocidas.Remove(id); return; }
        if (conocidas.Contains(id)) return;
        conocidas.Add(id);
        if (primeraVez) return;
        anuncio = true;
        Aviso.Mostrar("<ola>¡" + id + "!</ola>", new Color(1f, 0.85f, 0.45f), 2.8f);
        SurvivalUI.Aviso(descripcion[id], new Color(1f, 0.9f, 0.6f));
        Juice.Destello(new Color(1f, 0.85f, 0.4f, 0.3f), 0.6f);
        SonidosJuice.Tocar(SonidosJuice.Recoger, transform.position, 0.9f);
    }

    void AjustarVida()
    {
        if (vida == null) return;
        // se decide la primera vez que hace falta (despues de cargar la partida): una vida maxima
        // por encima de la normal quiere decir que el peto ya estaba aplicado al guardar
        if (!vidaIniciada) { petoAplicado = vida.MaxHealth > 250f; vidaIniciada = true; }
        if (petoAplicado == TienePeto) return;
        float base_ = vida.MaxHealth / (petoAplicado ? 1.5f : 1f);
        float max = base_ * (TienePeto ? 1.5f : 1f);
        float actual = TienePeto ? vida.CurrentHealth + (max - vida.MaxHealth) : Mathf.Min(vida.CurrentHealth, max);
        vida.SetHealth(actual, max);
        petoAplicado = TienePeto;
    }
}
