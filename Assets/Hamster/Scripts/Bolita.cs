using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bolita, el hamster de la feria de ciencias (lo arma HamsterBuilder; lo dirige Hamsterlandia).
/// No tiene vida: no se le puede ganar, solo huir. Camina sobre un grafo de puntos de piso que el builder
/// genera dentro de los tubos, los conectores y las plataformas por donde cabe (no sube escaleras de
/// borradores, no pasa por grietas ni por la puertita de su casa: es demasiado gordo).
/// Modos: escondido, recorrer (ir a un nodo), perseguir, atorado en la puerta, en la rueda y "ta-da".
/// La animacion es por codigo: rebote al correr, patitas, orejas, nariz, mejillas y sombrero de copa.
/// </summary>
public class Bolita : MonoBehaviour
{
    public enum Modo { Escondido, Quieto, Recorrer, Perseguir, Atorado, Rueda, Tada, Festejo }

    [Header("Grafo (piso por donde cabe)")]
    public Vector3[] nodos;
    public int[] inicio;          // vecinos del nodo i: vec[inicio[i] .. inicio[i+1]-1]
    public int[] vec;
    public int nodoPuerta, nodoEscenario, nodoSalida;

    [Header("Cuerpo (animado por codigo)")]
    public Transform visual, cuerpo, cabeza, orejaI, orejaD, nariz, mejillaI, mejillaD, sombrero, cola;
    public Transform[] patas;     // DI, DD, TI, TD

    [Header("Sonido")]
    public AudioClip[] chillidos;
    public AudioClip chillidoEnojo, paso, atorado;

    [Header("Ajustes")]
    public float radioAtrapar = 3.2f;
    public float largoCuerpo = 8f;

    public Modo modo { get; private set; } = Modo.Escondido;
    public System.Action alAtrapar, alLlegar;
    /// <summary>Si es >= 0, al perseguir va a este nodo en vez de al punto mas cercano al jugador (p. ej. la puerta de su casa).</summary>
    [System.NonSerialized] public int nodoForzado = -1;

    // estado de movimiento
    Vector3 pos;
    int nodoActual = -1;          // ultimo nodo alcanzado
    readonly List<int> ruta = new List<int>();
    int destino = -1;
    float velocidad = 4f;
    Transform objetivo;
    float tRuta, tChillido = 3f, fasePaso, tPaso, tEspera;
    Vector3 dirMirada = Vector3.forward;
    float inclinacion;
    AudioSource fuente, fuentePasos;

    // animacion
    float t, ritmo, pararse, atoradoT, festejoT;
    Vector3 escCuerpo, posCuerpo, posSombrero, escNariz;
    Quaternion rotSombrero;
    Vector3[] posPatas;
    Transform rueda; Vector3 puntoRueda; Vector3 dirRueda;

    // dijkstra
    float[] dist; int[] previo; bool[] cerrado;
    int[] heap; int heapN;

    public Vector3 Posicion => pos;
    public bool Visible => modo != Modo.Escondido;
    public float Velocidad { get => velocidad; set => velocidad = value; }

    void Awake()
    {
        fuente = gameObject.AddComponent<AudioSource>();
        fuente.spatialBlend = 1f; fuente.rolloffMode = AudioRolloffMode.Linear; fuente.minDistance = 6f; fuente.maxDistance = 90f; fuente.playOnAwake = false;
        fuentePasos = gameObject.AddComponent<AudioSource>();
        fuentePasos.spatialBlend = 1f; fuentePasos.rolloffMode = AudioRolloffMode.Linear; fuentePasos.minDistance = 4f; fuentePasos.maxDistance = 60f; fuentePasos.playOnAwake = false;
        if (cuerpo != null) { escCuerpo = cuerpo.localScale; posCuerpo = cuerpo.localPosition; }
        if (sombrero != null) { posSombrero = sombrero.localPosition; rotSombrero = sombrero.localRotation; }
        if (nariz != null) escNariz = nariz.localScale;
        if (patas != null) { posPatas = new Vector3[patas.Length]; for (int i = 0; i < patas.Length; i++) posPatas[i] = patas[i].localPosition; }
        int n = nodos != null ? nodos.Length : 0;
        dist = new float[n]; previo = new int[n]; cerrado = new bool[n]; heap = new int[Mathf.Max(1, n * 4)];
        pos = transform.position;
    }

    // ================================================================== ordenes
    public void Esconder()
    {
        modo = Modo.Escondido; ruta.Clear(); objetivo = null;
        if (visual != null) visual.gameObject.SetActive(false);
    }

    public void AparecerEn(int nodo, Vector3 mirar)
    {
        nodoActual = Mathf.Clamp(nodo, 0, nodos.Length - 1);
        pos = nodos[nodoActual];
        mirar.y = 0f; if (mirar.sqrMagnitude > 0.01f) dirMirada = mirar.normalized;
        modo = Modo.Quieto; ruta.Clear(); objetivo = null;
        if (visual != null) visual.gameObject.SetActive(true);
        Aplicar();
    }

    public void IrA(int nodo, float vel, System.Action llegar = null)
    {
        if (modo == Modo.Escondido) AparecerEn(nodoPuerta, Vector3.forward);
        velocidad = vel; destino = nodo; alLlegar = llegar; objetivo = null;
        modo = Modo.Recorrer;
        CalcularRuta(nodo);
    }

    public void Perseguir(Transform jugador, float vel)
    {
        if (modo == Modo.Escondido) AparecerEn(nodoPuerta, Vector3.forward);
        objetivo = jugador; velocidad = vel; modo = Modo.Perseguir; tRuta = 0f;
    }

    public void Quieto() { modo = Modo.Quieto; ruta.Clear(); }

    public void Tada() { modo = Modo.Tada; ruta.Clear(); pararse = 0f; }

    public void Festejar() { modo = Modo.Festejo; festejoT = 0f; ruta.Clear(); Chillar(true); }

    /// <summary>Se atora en la puerta de su casa: mete la cabeza y menea la cola.</summary>
    public void Atorar(Vector3 puerta, Vector3 haciaDentro)
    {
        modo = Modo.Atorado; ruta.Clear(); atoradoT = 0f;
        haciaDentro.y = 0f; dirMirada = haciaDentro.normalized;
        pos = puerta - dirMirada * (largoCuerpo * 0.42f);
        pos.y = puerta.y;
        if (atorado != null) fuente.PlayOneShot(atorado, 0.9f * Ajustes.VolEfectos);
    }

    /// <summary>Sale de la puerta hacia atras y sigue (lo pide el gestor al terminar el forcejeo).</summary>
    public void Desatorar()
    {
        nodoActual = nodoPuerta; pos = nodos[nodoPuerta];
        modo = Modo.Quieto;
    }

    public void EnRueda(Transform r, Vector3 punto, Vector3 dir)
    {
        rueda = r; puntoRueda = punto; dirRueda = dir;
        modo = Modo.Rueda; ruta.Clear(); objetivo = null;
        if (visual != null) visual.gameObject.SetActive(true);
        pos = punto; dirMirada = dir;
    }

    public void Chillar(bool enojado = false)
    {
        var c = enojado && chillidoEnojo != null ? chillidoEnojo : (chillidos != null && chillidos.Length > 0 ? chillidos[Random.Range(0, chillidos.Length)] : null);
        if (c == null) return;
        fuente.pitch = Random.Range(0.92f, 1.1f);
        fuente.PlayOneShot(c, (enojado ? 1f : 0.8f) * Ajustes.VolEfectos);
    }

    // ================================================================== grafo
    public int NodoCercano(Vector3 p, float pesoVertical = 2.5f)
    {
        int mejor = 0; float dMin = float.MaxValue;
        for (int i = 0; i < nodos.Length; i++)
        {
            Vector3 d = nodos[i] - p; d.y *= pesoVertical;
            float m = d.sqrMagnitude;
            if (m < dMin) { dMin = m; mejor = i; }
        }
        return mejor;
    }

    void HeapPush(int n)
    {
        if (heapN >= heap.Length) System.Array.Resize(ref heap, heap.Length * 2);
        int i = heapN++; heap[i] = n;
        while (i > 0) { int p = (i - 1) / 2; if (dist[heap[p]] <= dist[heap[i]]) break; int x = heap[p]; heap[p] = heap[i]; heap[i] = x; i = p; }
    }

    int HeapPop()
    {
        int r = heap[0]; heap[0] = heap[--heapN];
        int i = 0;
        while (true)
        {
            int a = i * 2 + 1, b = a + 1, m = i;
            if (a < heapN && dist[heap[a]] < dist[heap[m]]) m = a;
            if (b < heapN && dist[heap[b]] < dist[heap[m]]) m = b;
            if (m == i) break;
            int x = heap[m]; heap[m] = heap[i]; heap[i] = x; i = m;
        }
        return r;
    }

    /// <summary>Dijkstra desde el nodo actual. Si meta = -1, busca el nodo alcanzable mas cercano a 'punto'.</summary>
    void Dijkstra(int desde, int meta, Vector3 punto, out int llegada)
    {
        int n = nodos.Length;
        for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; previo[i] = -1; cerrado[i] = false; }
        heapN = 0; dist[desde] = 0f; HeapPush(desde);
        llegada = desde; float mejor = float.MaxValue;
        while (heapN > 0)
        {
            int u = HeapPop();
            if (cerrado[u]) continue;
            cerrado[u] = true;
            if (meta >= 0) { if (u == meta) { llegada = u; return; } }
            else
            {
                Vector3 d = nodos[u] - punto; d.y *= 2.5f;
                float m = d.sqrMagnitude;
                if (m < mejor) { mejor = m; llegada = u; }
            }
            for (int k = inicio[u]; k < inicio[u + 1]; k++)
            {
                int v = vec[k];
                if (cerrado[v]) continue;
                float c = dist[u] + Vector3.Distance(nodos[u], nodos[v]);
                if (c < dist[v]) { dist[v] = c; previo[v] = u; HeapPush(v); }
            }
        }
        if (meta >= 0) llegada = -1;
    }

    void Reconstruir(int llegada)
    {
        ruta.Clear();
        for (int v = llegada; v >= 0 && v != nodoActual; v = previo[v]) ruta.Add(v);
        ruta.Reverse();
    }

    void CalcularRuta(int meta)
    {
        if (nodoActual < 0) nodoActual = NodoCercano(pos);
        int llegada;
        Dijkstra(nodoActual, meta, Vector3.zero, out llegada);
        if (llegada < 0) { ruta.Clear(); return; }
        Reconstruir(llegada);
    }

    /// <summary>Distancia por el grafo hasta el jugador (se usa para la "liga elastica" de velocidad).</summary>
    public float DistanciaRuta
    {
        get
        {
            float d = 0f; Vector3 a = pos;
            foreach (var r in ruta) { d += Vector3.Distance(a, nodos[r]); a = nodos[r]; }
            return d;
        }
    }

    // ================================================================== bucle
    void Update()
    {
        float dt = Time.deltaTime;
        t += dt;
        float avance = 0f;
        switch (modo)
        {
            case Modo.Escondido: return;
            case Modo.Recorrer:
                avance = Caminar(dt);
                if (ruta.Count == 0) { modo = Modo.Quieto; var a = alLlegar; alLlegar = null; a?.Invoke(); }
                break;
            case Modo.Perseguir:
                avance = Cazar(dt);
                break;
            case Modo.Atorado:
                atoradoT += dt;
                break;
            case Modo.Rueda:
                avance = 5.5f * dt;
                pos = puntoRueda; dirMirada = dirRueda;
                break;
            case Modo.Tada:
                pararse = Mathf.MoveTowards(pararse, 1f, dt * 2.2f);
                break;
            case Modo.Festejo:
                festejoT += dt;
                break;
        }
        if (modo != Modo.Tada && modo != Modo.Festejo) pararse = Mathf.MoveTowards(pararse, 0f, dt * 3f);

        // chillidos de vez en cuando
        tChillido -= dt;
        if (tChillido <= 0f)
        {
            tChillido = modo == Modo.Perseguir ? Random.Range(1.6f, 3.4f) : Random.Range(4f, 9f);
            if (modo != Modo.Rueda || Random.value < 0.4f) Chillar(modo == Modo.Perseguir && Random.value < 0.35f);
        }
        // pasos (golpecitos en el plastico)
        if (avance > 0f && paso != null)
        {
            tPaso -= avance;
            if (tPaso <= 0f) { tPaso = 1.6f; fuentePasos.pitch = Random.Range(0.85f, 1.15f); fuentePasos.PlayOneShot(paso, 0.55f * Ajustes.VolEfectos); }
        }
        ritmo = Mathf.MoveTowards(ritmo, avance > 0.0001f ? 1f : 0f, dt * 5f);
        fasePaso += avance * 1.6f;
        Aplicar();
    }

    float Caminar(float dt)
    {
        if (ruta.Count == 0) return 0f;
        float resto = velocidad * dt, total = 0f;
        while (resto > 0f && ruta.Count > 0)
        {
            Vector3 meta = nodos[ruta[0]];
            Vector3 d = meta - pos;
            float largo = d.magnitude;
            // de subida va mas lento, de bajada un poco mas rapido
            float pend = largo > 0.01f ? d.y / largo : 0f;
            float k = Mathf.Clamp(1f - pend * 0.6f, 0.65f, 1.2f);
            float paso = resto * k;
            if (largo <= paso)
            {
                pos = meta; nodoActual = ruta[0]; ruta.RemoveAt(0);
                resto -= largo / k; total += largo;
            }
            else
            {
                pos += d / largo * paso; total += paso; resto = 0f;
            }
            Vector3 h = d; h.y = 0f;
            if (h.sqrMagnitude > 0.0004f) dirMirada = Vector3.Slerp(dirMirada, h.normalized, Mathf.Clamp01(dt * 10f));
            inclinacion = Mathf.Lerp(inclinacion, Mathf.Clamp(-pend * 45f, -28f, 28f), Mathf.Clamp01(dt * 6f));
        }
        return total;
    }

    float Cazar(float dt)
    {
        if (objetivo == null) { modo = Modo.Quieto; return 0f; }
        Vector3 p = objetivo.position;
        tRuta -= dt;
        if (tRuta <= 0f || ruta.Count == 0)
        {
            tRuta = 0.3f;
            if (nodoActual < 0) nodoActual = NodoCercano(pos);
            // si va a medio tramo, recalcula desde el nodo al que se dirige
            int desde = ruta.Count > 0 ? ruta[0] : nodoActual;
            int guardado = nodoActual; nodoActual = desde;
            int llegada;
            Dijkstra(desde, nodoForzado, p, out llegada);
            if (llegada < 0) Dijkstra(desde, -1, p, out llegada);
            Reconstruir(llegada);
            if (desde != guardado) ruta.Insert(0, desde);
            nodoActual = guardado;
        }
        float avance = Caminar(dt);
        // tramo final: va derecho hacia el jugador si ya esta a su lado (mismo piso)
        Vector3 al = p - pos;
        if (nodoForzado < 0 && ruta.Count <= 1 && Mathf.Abs(al.y) < 2.5f && al.magnitude < 6f)
        {
            Vector3 h = al; h.y = 0f;
            float m = h.magnitude;
            if (m > 0.3f)
            {
                float paso = Mathf.Min(m, velocidad * 0.9f * dt);
                pos += h / m * paso; avance += paso;
                dirMirada = Vector3.Slerp(dirMirada, h / m, Mathf.Clamp01(dt * 10f));
            }
        }
        // atrapado: la boca llega al jugador
        Vector3 boca = pos + dirMirada * (largoCuerpo * 0.35f);
        Vector3 db = p - boca; float dy = Mathf.Abs(db.y + 1f); db.y = 0f;
        Vector3 dc = p - pos; dc.y = 0f;
        if (dy < 3f && (db.magnitude < radioAtrapar * 0.55f || dc.magnitude < radioAtrapar))
        {
            modo = Modo.Quieto; ruta.Clear();
            alAtrapar?.Invoke();
        }
        return avance;
    }

    // ================================================================== animacion
    void Aplicar()
    {
        transform.position = pos;
        if (dirMirada.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dirMirada, Vector3.up) * Quaternion.Euler(inclinacion, 0f, 0f);
        if (visual == null) return;

        float ciclo = fasePaso;
        float rebote = Mathf.Abs(Mathf.Sin(ciclo)) * 0.45f * ritmo;
        float resp = 1f + 0.025f * Mathf.Sin(t * 3.1f);
        // pararse en dos patas (ta-da): gira sobre la cola
        float tada = Mathf.SmoothStep(0f, 1f, pararse);
        float meneo = 0f, estira = 0f;
        if (modo == Modo.Atorado) { meneo = Mathf.Sin(atoradoT * 17f) * 12f; estira = 0.12f + 0.06f * Mathf.Sin(atoradoT * 9f); }
        if (modo == Modo.Festejo) { meneo = Mathf.Sin(festejoT * 12f) * 8f; rebote += Mathf.Abs(Mathf.Sin(festejoT * 8f)) * 0.8f; }

        visual.localPosition = new Vector3(0f, rebote, 0f);
        visual.localRotation = Quaternion.Euler(-70f * tada, 0f, meneo);
        if (tada > 0.001f) visual.localPosition += new Vector3(0f, 2.2f * tada, -largoCuerpo * 0.3f * tada);

        if (cuerpo != null)
        {
            float sq = Mathf.Sin(ciclo * 2f) * 0.07f * ritmo;
            cuerpo.localScale = new Vector3(escCuerpo.x * resp * (1f - sq * 0.5f - estira * 0.6f), escCuerpo.y * resp * (1f - sq), escCuerpo.z * (1f + sq + estira));
            cuerpo.localPosition = posCuerpo;
        }
        if (cabeza != null)
        {
            float mira = modo == Modo.Quieto ? Mathf.Sin(t * 0.9f) * 18f : 0f;
            cabeza.localRotation = Quaternion.Euler(Mathf.Sin(ciclo) * 4f * ritmo + 25f * tada, mira, 0f);
        }
        if (orejaI != null) orejaI.localRotation = Quaternion.Euler(-10f + Mathf.Sin(t * 7f + 1f) * 6f * (0.3f + ritmo), 0f, 15f);
        if (orejaD != null) orejaD.localRotation = Quaternion.Euler(-10f + Mathf.Sin(t * 7.4f) * 6f * (0.3f + ritmo), 0f, -15f);
        if (nariz != null) { float k = 1f + 0.18f * Mathf.Max(0f, Mathf.Sin(t * 13f)); nariz.localScale = Vector3.Scale(escNariz, new Vector3(k, 1f / k, k)); }
        float cachete = 1f + (modo == Modo.Perseguir ? 0.12f : 0.04f) * Mathf.Sin(t * 5f) + (modo == Modo.Atorado ? 0.25f : 0f);
        if (mejillaI != null) mejillaI.localScale = Vector3.one * cachete;
        if (mejillaD != null) mejillaD.localScale = Vector3.one * cachete;
        if (cola != null) cola.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * (modo == Modo.Atorado ? 22f : 6f)) * (modo == Modo.Atorado ? 35f : 10f), 0f);
        if (sombrero != null)
        {
            float salto = modo == Modo.Tada ? Mathf.Max(0f, Mathf.Sin(t * 6f)) * 0.5f * tada : 0f;
            sombrero.localPosition = posSombrero + Vector3.up * (rebote * 0.4f + salto);
            sombrero.localRotation = rotSombrero * Quaternion.Euler(Mathf.Sin(ciclo + 0.6f) * 8f * ritmo, 0f, Mathf.Sin(t * 2f) * 4f + (modo == Modo.Tada ? -20f * tada : 0f));
        }
        if (patas != null && posPatas != null)
            for (int i = 0; i < patas.Length; i++)
            {
                // galope: las delanteras juntas, las traseras juntas, desfasadas
                float f = ciclo + (i < 2 ? 0f : Mathf.PI) + (i % 2) * 0.35f;
                float adelante = Mathf.Sin(f) * 0.9f * ritmo;
                float arriba = Mathf.Max(0f, Mathf.Cos(f)) * 0.5f * ritmo;
                if (modo == Modo.Tada && i < 2) { adelante = 0.6f; arriba = 1.2f + Mathf.Sin(t * 9f + i) * 0.3f; }
                patas[i].localPosition = posPatas[i] + new Vector3(0f, arriba, adelante);
            }
    }

    void OnDrawGizmosSelected()
    {
        if (nodos == null || inicio == null || vec == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        for (int i = 0; i < nodos.Length; i++)
            for (int k = inicio[i]; k < inicio[i + 1]; k++)
                if (vec[k] > i) Gizmos.DrawLine(nodos[i] + Vector3.up * 0.3f, nodos[vec[k]] + Vector3.up * 0.3f);
    }
}
