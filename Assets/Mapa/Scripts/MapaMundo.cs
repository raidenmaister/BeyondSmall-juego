using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// Datos del mapa del cuaderno: que zonas exploro el jugador (se van "dibujando" a su alrededor),
/// los lugares emblematicos (aparecen al descubrirlos) y las chinchetas que pone el jugador.
/// Lo crea GestorPartida; MapaUI (tecla M) y BrujulaUI (arriba de la pantalla) lo muestran.
/// Se guarda con la partida (DatosPartida.mapaNiebla / chinchetas).
/// </summary>
public class MapaMundo : MonoBehaviour
{
    public static MapaMundo Instancia { get; private set; }

    /// <summary>Zona del mundo que cubre el mapa (centro x,z y lado en unidades). La cerca mide 420 de radio.</summary>
    public static readonly Vector2 Centro = new Vector2(0f, -6f);
    public const float Tamano = 900f;
    public const int ResNiebla = 256;
    public const int MaxChinchetas = 9;

    public static readonly Color[] ColoresChincheta =
    {
        new Color(0.86f, 0.18f, 0.16f), new Color(0.2f, 0.45f, 0.9f), new Color(0.2f, 0.7f, 0.3f),
        new Color(0.95f, 0.75f, 0.1f), new Color(0.6f, 0.3f, 0.85f), new Color(0.95f, 0.5f, 0.15f),
        new Color(0.1f, 0.75f, 0.75f), new Color(0.9f, 0.35f, 0.6f), new Color(0.35f, 0.25f, 0.2f),
    };

    public enum TipoLugar { Cabina, Salon, Fogata, Puerta, Hormiguero, Arbol, Cerca }

    public class Lugar
    {
        public string nombre;
        public TipoLugar tipo;
        public Vector3 pos;
        public bool descubierto;
        public bool siempre;          // se conoce desde el principio
    }

    public class Chincheta { public Vector2 xz; public int color; }

    public readonly List<Lugar> lugares = new List<Lugar>();
    public readonly List<Chincheta> chinchetas = new List<Chincheta>();
    public Texture2D TexNiebla { get; private set; }
    public System.Action Cambio;     // chinchetas cambiaron (la brujula se actualiza)

    byte[] niebla;
    bool sucia, avisar;
    float timer;
    Transform jugador;

    // ================================================================== ciclo
    void Awake()
    {
        Instancia = this;
        niebla = new byte[ResNiebla * ResNiebla];
        TexNiebla = new Texture2D(ResNiebla, ResNiebla, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "NieblaMapa" };
        Subir();
    }

    void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
        if (TexNiebla != null) Destroy(TexNiebla);
    }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        CrearLugares();
        gameObject.AddComponent<MapaUI>();
        gameObject.AddComponent<BrujulaUI>();
        // los lugares ya descubiertos al empezar o al cargar no se anuncian
        Invoke(nameof(EmpezarAvisos), 3f);
    }

    void EmpezarAvisos() { RevisarLugares(); avisar = true; }

    void CrearLugares()
    {
        lugares.Clear();
        if (MaquinaCrecimiento.Instancia != null)
            lugares.Add(new Lugar { nombre = "La cabina", tipo = TipoLugar.Cabina, pos = MaquinaCrecimiento.Instancia.transform.position, siempre = true, descubierto = true });
        var room = GameObject.Find("class")?.transform.Find("Room")?.GetComponent<Renderer>();
        if (room != null)
        {
            var b = room.bounds;
            lugares.Add(new Lugar { nombre = "El salon abandonado", tipo = TipoLugar.Salon, pos = new Vector3(b.center.x, 0f, b.max.z - 22f), siempre = true, descubierto = true });   // ahi despiertas
        }
        var fog = GameObject.Find("Fogata");
        if (fog != null) lugares.Add(new Lugar { nombre = "La fogata", tipo = TipoLugar.Fogata, pos = fog.transform.position });
        // la salida: entre las dos puertas del salon
        var d1 = GameObject.Find("class/Door_1"); var d2 = GameObject.Find("class/Door_2");
        if (d1 != null && d2 != null)
        {
            var p = (d1.GetComponent<Renderer>().bounds.center + d2.GetComponent<Renderer>().bounds.center) * 0.5f;
            lugares.Add(new Lugar { nombre = "La salida", tipo = TipoLugar.Puerta, pos = new Vector3(p.x + 4f, 0f, p.z) });
        }
        var h = ZonaJuego.hormigueroEntrada;
        lugares.Add(new Lugar { nombre = "El hormiguero", tipo = TipoLugar.Hormiguero, pos = new Vector3(h.x, ZonaJuego.Altura(h.x, h.y), h.y) });
        var a = ZonaJuego.arbolCentro;
        lugares.Add(new Lugar { nombre = "El arbol de la colmena", tipo = TipoLugar.Arbol, pos = new Vector3(a.x, ZonaJuego.Altura(a.x, a.y), a.y) });
        // la cerca: se marca el punto que el jugador descubra primero (norte, sur, este u oeste)
        foreach (var dir in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
        {
            Vector2 q = ZonaJuego.centro.ToXZ() + dir * (ZonaJuego.radioCerca - 14f);
            lugares.Add(new Lugar { nombre = "La cerca", tipo = TipoLugar.Cerca, pos = new Vector3(q.x, 0f, q.y) });
        }
    }

    void Update()
    {
        if (jugador == null) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer = 0.2f;
            Explorar();
            RevisarLugares();
        }
        if (sucia) { Subir(); sucia = false; }
    }

    // ================================================================== exploracion
    void Explorar()
    {
        if (Hormiguero.Instancia != null && Hormiguero.Instancia.JugadorDentro) return;   // bajo tierra no se ve la superficie
        Vector3 p = jugador.position;
        float radio = ZonaJuego.EnEdificio(p) ? 30f : 46f;
        Revelar(new Vector2(p.x, p.z), radio);
    }

    /// <summary>Marca como explorado un circulo (borde suave).</summary>
    public void Revelar(Vector2 xz, float radio)
    {
        float celda = Tamano / ResNiebla;
        Vector2 c = AUV(xz) * ResNiebla;
        int r = Mathf.CeilToInt(radio / celda) + 1;
        int x0 = Mathf.Max(0, (int)c.x - r), x1 = Mathf.Min(ResNiebla - 1, (int)c.x + r);
        int y0 = Mathf.Max(0, (int)c.y - r), y1 = Mathf.Min(ResNiebla - 1, (int)c.y + r);
        float rc = radio / celda;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float v = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rc, rc * 0.55f, d));
                byte b = (byte)(v * 255f);
                int i = y * ResNiebla + x;
                if (b > niebla[i]) { niebla[i] = b; sucia = true; }
            }
    }

    public float Explorado(Vector2 xz)
    {
        Vector2 c = AUV(xz) * ResNiebla;
        int x = Mathf.Clamp((int)c.x, 0, ResNiebla - 1), y = Mathf.Clamp((int)c.y, 0, ResNiebla - 1);
        return niebla[y * ResNiebla + x] / 255f;
    }

    void Subir()
    {
        TexNiebla.LoadRawTextureData(niebla);
        TexNiebla.Apply(false);
    }

    /// <summary>Revisa ya que lugares se descubrieron (el mapa lo llama al abrirse, con el juego en pausa).</summary>
    public void Revisar() { RevisarLugares(); }

    void RevisarLugares()
    {
        bool cercaVista = false;
        foreach (var l in lugares)
        {
            if (l.descubierto) { if (l.tipo == TipoLugar.Cerca) cercaVista = true; continue; }
            if (l.tipo == TipoLugar.Cerca && cercaVista) continue;     // solo una etiqueta de la cerca
            Vector2 xz = new Vector2(l.pos.x, l.pos.z);
            bool cerca = jugador != null && Vector2.Distance(xz, new Vector2(jugador.position.x, jugador.position.z)) < 40f;
            if (Explorado(xz) > 0.6f || cerca)
            {
                l.descubierto = true;
                if (l.tipo == TipoLugar.Cerca) cercaVista = true;
                if (avisar) SurvivalUI.Aviso("Nuevo lugar en el mapa: " + l.nombre, new Color(0.95f, 0.85f, 0.6f));
            }
        }
    }

    // ================================================================== coordenadas
    /// <summary>Mundo (x,z) a coordenadas 0..1 del mapa (v hacia el norte, +Z).</summary>
    public static Vector2 AUV(Vector2 xz) => new Vector2((xz.x - Centro.x) / Tamano + 0.5f, (xz.y - Centro.y) / Tamano + 0.5f);
    public static Vector2 AMundo(Vector2 uv) => new Vector2((uv.x - 0.5f) * Tamano + Centro.x, (uv.y - 0.5f) * Tamano + Centro.y);

    // ================================================================== chinchetas
    public bool PonerChincheta(Vector2 xz)
    {
        if (chinchetas.Count >= MaxChinchetas) return false;
        // primer color libre
        int color = 0;
        for (int c = 0; c < ColoresChincheta.Length; c++) if (!chinchetas.Exists(x => x.color == c)) { color = c; break; }
        chinchetas.Add(new Chincheta { xz = xz, color = color });
        Cambio?.Invoke();
        return true;
    }

    public void QuitarChincheta(Chincheta c)
    {
        if (chinchetas.Remove(c)) Cambio?.Invoke();
    }

    // ================================================================== guardado
    public void Exportar(DatosPartida d)
    {
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(niebla, 0, niebla.Length);
            d.mapaNiebla = System.Convert.ToBase64String(ms.ToArray());
        }
        d.chinchetas.Clear();
        foreach (var c in chinchetas) d.chinchetas.Add(new ChinchetaGuardada { x = c.xz.x, z = c.xz.y, color = c.color });
    }

    public void Importar(DatosPartida d)
    {
        if (!string.IsNullOrEmpty(d.mapaNiebla))
        {
            try
            {
                using (var ms = new MemoryStream(System.Convert.FromBase64String(d.mapaNiebla)))
                using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                {
                    int leido = 0, n;
                    while (leido < niebla.Length && (n = gz.Read(niebla, leido, niebla.Length - leido)) > 0) leido += n;
                }
                Subir();
            }
            catch (System.Exception e) { Debug.LogWarning("[Mapa] no se pudo leer lo explorado: " + e.Message); }
        }
        chinchetas.Clear();
        if (d.chinchetas != null)
            foreach (var c in d.chinchetas) chinchetas.Add(new Chincheta { xz = new Vector2(c.x, c.z), color = Mathf.Clamp(c.color, 0, ColoresChincheta.Length - 1) });
        // los lugares se vuelven a evaluar con lo explorado (sin anunciarlos)
        foreach (var l in lugares) if (!l.siempre) l.descubierto = false;
        bool av = avisar; avisar = false; RevisarLugares(); avisar = av;
        Cambio?.Invoke();
    }
}

static class ExtVector3Mapa
{
    public static Vector2 ToXZ(this Vector3 v) => new Vector2(v.x, v.z);
}
