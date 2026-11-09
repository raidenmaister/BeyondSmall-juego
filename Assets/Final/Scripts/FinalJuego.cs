using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// El final de Beyond Small. Con las 4 baterias puestas la cabina queda encendida; al entrar en ella empieza
/// la cinematica: la maquina se sobrecarga, chispas verdes suben por la cabina, el jugador mira hacia arriba,
/// un destello blanco y... vuelve a su tamaño. Amanece; el personaje, ya grande, mira el salon que antes era
/// enorme. Fundido a negro y CreditosFinal (estadisticas, fotos y creditos) con musica propia.
/// Mantener ENTER salta la cinematica. Lo crea GestorPartida.
/// </summary>
public class FinalJuego : MonoBehaviour
{
    public static bool Activo { get; private set; }
    public const float EscalaGrande = 14f;      // el jugador mide 2 u (~11 cm): x14 son ~1.6 m

    MaquinaCrecimiento maq;
    PlayerController pc;
    Transform jugador, cabeza;
    Camera cam;
    CinemachineBrain brain;
    MenuRecursos rec;
    AudioSource fx, musica;
    Image blanco, negro;
    TextMeshProUGUI pensamiento, ayuda;
    ParticleSystem chispas;
    bool avisado, empezado, saltar;
    float inclinarCabeza, tSaltar;

    void OnDestroy() { if (Activo) Activo = false; }

    void Start()
    {
        pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        if (jugador != null) foreach (var t in jugador.GetComponentsInChildren<Transform>()) if (t.name == "mixamorig:Head") cabeza = t;
        rec = Resources.Load<MenuRecursos>("MenuRecursos");
    }

    void Update()
    {
        if (empezado)
        {
            // mantener ENTER para saltar la cinematica
            bool tecla = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
            tSaltar = tecla ? tSaltar + Time.unscaledDeltaTime : 0f;
            if (ayuda != null) ayuda.alpha = Mathf.MoveTowards(ayuda.alpha, tecla ? 0.9f : 0.35f, Time.unscaledDeltaTime * 2f);
            if (tSaltar > 1f && !saltar) saltar = true;
            return;
        }
        maq = MaquinaCrecimiento.Instancia;
        if (maq == null || jugador == null || !maq.Encendida) return;
        if (!avisado) { avisado = true; StartCoroutine(Avisar()); }
        if (GestorPartida.Muerto || IntroDespertar.Activa || Time.timeScale <= 0f || MapaUI.Abierto) return;
        Vector3 d = jugador.position - maq.transform.position;
        float alto = d.y; d.y = 0f;
        if (d.magnitude < maq.radioInterior - 2f && alto > -1f && alto < 15f) Empezar();
    }

    IEnumerator Avisar()
    {
        yield return new WaitForSeconds(2.5f);
        if (!empezado) Aviso.Mostrar("La cabina tiene energia\n<size=60%>Entra en ella...</size>", new Color(0.4f, 1f, 0.55f), 5f);
    }

    public void Empezar()
    {
        if (empezado) return;
        empezado = true;
        if (GestorPartida.Instancia != null) GestorPartida.Instancia.Guardar(true);    // se puede volver a ver
        Activo = true;
        StartCoroutine(Cinematica());
    }

    // ================================================================== cinematica
    IEnumerator Cinematica()
    {
        Preparar();
        Vector3 C = maq.transform.position;
        Quaternion R = maq.transform.rotation;
        Vector3 L(float x, float y, float z) => C + R * new Vector3(x, y, z);

        // el jugador al centro de la cabina, mirando a la puerta
        Colocar(L(0f, 1.05f, -1.5f), R, 1f);
        if (maq.sonidoEncender != null) fx.PlayOneShot(maq.sonidoEncender, 0.9f * Ajustes.VolEfectos);

        // 1. afuera, de frente a la puerta: la camara se acerca y la cabina empieza a sobrecargarse
        yield return Plano(6f, t =>
        {
            maq.sobrecarga = Mathf.Lerp(0f, 0.35f, t);
            Mirar(Vector3.Lerp(L(0f, 5f, 48f), L(0f, 7f, 30f), Suave(t)), L(0f, 16f, 0f), 0.02f * t);
        });
        if (saltar) { yield return Saltar(); yield break; }

        // 2. adentro: el pequeño mira hacia arriba, suben chispas verdes
        if (maq.sonidoInsertar != null) fx.PlayOneShot(maq.sonidoInsertar, 0.9f * Ajustes.VolEfectos);
        CrearChispas(C, R);
        yield return Plano(6f, t =>
        {
            maq.sobrecarga = Mathf.Lerp(0.35f, 0.8f, t);
            inclinarCabeza = Mathf.Lerp(0f, -35f, Suave(Mathf.Clamp01(t * 1.5f)));      // levanta la mirada
            Vector3 cabezaPos = jugador.position + Vector3.up * 1.8f;
            Mirar(Vector3.Lerp(L(4.5f, 1.6f, 4f), L(5.5f, 2.2f, 5.5f), t), Vector3.Lerp(cabezaPos, L(0f, 30f, 0f), Suave(Mathf.Clamp01((t - 0.35f) / 0.65f))), 0.04f + 0.06f * t);
        });
        if (saltar) { yield return Saltar(); yield break; }

        // 3. afuera en diagonal: crece. Destello blanco en el pico del sonido
        var crecer = Resources.Load<AudioClip>("Final_Crecer");
        if (crecer != null) fx.PlayOneShot(crecer, 1f * Ajustes.VolEfectos);
        Vector3 pie = jugador.position;
        yield return Plano(6.5f, t =>
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.93f, t));
            s = s * s * (3f - 2f * s);
            float k = Mathf.Lerp(1f, EscalaGrande, s) * (1f + 0.04f * Mathf.Sin(t * 60f) * s * (1f - s) * 4f);
            Colocar(pie, R, k);
            inclinarCabeza = Mathf.Lerp(-35f, 0f, s);
            maq.sobrecarga = Mathf.Lerp(0.8f, 1f, t);
            if (chispas != null) { var em = chispas.emission; em.rateOverTime = Mathf.Lerp(90f, 260f, t); }
            Mirar(Vector3.Lerp(L(26f, 9f, 36f), L(32f, 16f, 44f), Suave(t)), L(0f, Mathf.Lerp(6f, 18f, s), 0f), 0.05f + 0.25f * t * t);
            blanco.color = new Color(1f, 1f, 1f, Mathf.Clamp01((t - 0.62f) / 0.33f));
        });
        blanco.color = Color.white;
        if (saltar) { yield return Saltar(); yield break; }

        // 4. en blanco: amanece, la maquina descansa y el jugador, grande, sale frente a la cabina
        maq.sobrecarga = 0f;
        if (chispas != null) { chispas.Stop(); Destroy(chispas.gameObject, 4f); }
        var dn = DayNightCycle.Instance;
        if (dn != null) { dn.hora = 7.4f; dn.pausado = true; dn.AplicarAhora(); }
        inclinarCabeza = 0f;
        Vector3 fuera = L(0f, 0f, 30f); fuera.y = ZonaJuego.alturaPiso;
        Colocar(fuera, R, EscalaGrande);
        var clip = Resources.Load<AudioClip>("Final_Musica");
        if (clip != null) { musica.clip = clip; musica.volume = 0.8f * Ajustes.VolMusica; musica.Play(); }
        yield return new WaitForSecondsRealtime(1.2f);

        // 5. se aclara: abajo, junto a sus pies, mirando hacia arriba... y la camara sube a su hombro
        Vector3 F = R * Vector3.forward, Dch = R * Vector3.right;
        float alto = 2.16f * EscalaGrande;
        StartCoroutine(Pensar(2.5f, "...ahora todo se ve tan pequeño."));
        StartCoroutine(Pensar(8.5f, "Fue una gran aventura."));
        yield return Plano(14f, t =>
        {
            blanco.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(t / 0.22f));
            inclinarCabeza = Mathf.Lerp(0f, 18f, Suave(Mathf.Clamp01((t - 0.45f) / 0.4f)));     // mira hacia abajo, al salon
            float u = Suave(Mathf.Clamp01((t - 0.12f) / 0.8f));
            Vector3 a = fuera + F * 26f + Dch * 12f + Vector3.up * 2.5f;                          // a ras del suelo, frente a el
            Vector3 b = fuera - F * 14f + Dch * 10f + Vector3.up * (alto * 1.15f);                // sobre su hombro
            Vector3 p = Vector3.Lerp(a, b, u) + (Dch * 14f + Vector3.up * 4f) * Mathf.Sin(u * Mathf.PI);   // lo rodea al subir
            Vector3 ob = Vector3.Lerp(fuera + Vector3.up * alto * 0.75f, fuera + F * 70f + Vector3.up * 2f, Suave(Mathf.Clamp01((t - 0.3f) / 0.7f)));
            Mirar(p, ob, 0f);
        });

        // 6. fundido a negro y creditos
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 3f) { negro.color = new Color(0, 0, 0, t); yield return null; }
        negro.color = Color.black;
        IniciarCreditos();
    }

    IEnumerator Saltar()
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.6f) { negro.color = new Color(0, 0, 0, t); yield return null; }
        negro.color = Color.black;
        if (maq != null) maq.sobrecarga = 0f;
        if (musica.clip == null) { var clip = Resources.Load<AudioClip>("Final_Musica"); if (clip != null) { musica.clip = clip; musica.volume = 0.8f * Ajustes.VolMusica; musica.time = 18f; musica.Play(); } }
        IniciarCreditos();
    }

    void IniciarCreditos()
    {
        if (ayuda != null) ayuda.gameObject.SetActive(false);
        var c = new GameObject("CreditosFinal").AddComponent<CreditosFinal>();
        c.musica = musica;
        musica.transform.SetParent(c.transform, true);
    }

    // ================================================================== piezas
    void Preparar()
    {
        Controles.Bloqueado = true;
        cam = Camera.main;
        brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        if (brain != null) brain.enabled = false;
        if (pc != null) { pc.enabled = false; var an = pc.GetComponentInChildren<Animator>(); if (an != null) { an.SetFloat("Speed", 0f); an.SetBool("IsCrouching", false); } }
        Antorcha.Apagar();
        var hud = GameObject.Find("PlayerHUD_Canvas"); if (hud != null) hud.SetActive(false);
        if (FloorItemPromptManager.Instance != null) FloorItemPromptManager.Instance.gameObject.SetActive(false);
        foreach (var a in FindObjectsByType<Aviso>(FindObjectsInactive.Exclude)) Destroy(a.gameObject);
        // cualquier otra interfaz en pantalla (avisos de estado, barras, brujula...)
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && !c.transform.IsChildOf(transform)) c.enabled = false;

        var go = new GameObject("CinematicaFinal");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 980;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        // franjas de cine
        foreach (var arriba in new[] { true, false })
        {
            var f = Img(go.transform, "Franja", Color.black); var r = f.rectTransform;
            r.anchorMin = new Vector2(0, arriba ? 1 : 0); r.anchorMax = new Vector2(1, arriba ? 1 : 0); r.pivot = new Vector2(0.5f, arriba ? 1 : 0); r.sizeDelta = new Vector2(0, 110);
        }
        blanco = Img(go.transform, "Blanco", new Color(1, 1, 1, 0)); Estirar(blanco.rectTransform);
        negro = Img(go.transform, "Negro", new Color(0, 0, 0, 0)); Estirar(negro.rectTransform);
        pensamiento = Txt(go.transform, "Pensamiento", "", 40, rec != null ? rec.fuenteTexto : null);
        var rp = pensamiento.rectTransform; rp.anchorMin = new Vector2(0, 0); rp.anchorMax = new Vector2(1, 0); rp.pivot = new Vector2(0.5f, 0); rp.anchoredPosition = new Vector2(0, 150); rp.sizeDelta = new Vector2(-200, 60);
        pensamiento.fontStyle = FontStyles.Italic; pensamiento.alpha = 0f;
        ayuda = Txt(go.transform, "Ayuda", "Manten ENTER para saltar", 20, rec != null ? rec.fuenteTexto : null);
        var ra = ayuda.rectTransform; ra.anchorMin = ra.anchorMax = new Vector2(1, 0); ra.pivot = new Vector2(1, 0); ra.anchoredPosition = new Vector2(-40, 40); ra.sizeDelta = new Vector2(500, 30);
        ayuda.alignment = TextAlignmentOptions.BottomRight; ayuda.alpha = 0.35f;

        var fa = new GameObject("AudioFinal"); fa.transform.SetParent(transform, false);
        fx = fa.AddComponent<AudioSource>(); fx.playOnAwake = false; fx.spatialBlend = 0f; fx.ignoreListenerPause = true;
        var ma = new GameObject("MusicaFinal");
        musica = ma.AddComponent<AudioSource>(); musica.playOnAwake = false; musica.spatialBlend = 0f; musica.ignoreListenerPause = true; musica.loop = false;
    }

    void Colocar(Vector3 pos, Quaternion rot, float escala)
    {
        var cc = jugador.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        jugador.SetPositionAndRotation(pos, rot);
        jugador.localScale = Vector3.one * escala;
    }

    void Mirar(Vector3 pos, Vector3 objetivo, float temblor)
    {
        if (cam == null) return;
        Vector3 sacudida = temblor > 0f ? new Vector3(Mathf.PerlinNoise(Time.time * 23f, 1f) - 0.5f, Mathf.PerlinNoise(2f, Time.time * 21f) - 0.5f, 0f) * temblor * 2f : Vector3.zero;
        cam.transform.SetPositionAndRotation(pos + cam.transform.rotation * sacudida, Quaternion.LookRotation((objetivo - pos).normalized, Vector3.up));
    }

    IEnumerator Plano(float dur, System.Action<float> f)
    {
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (saltar) yield break;
            f(t / dur);
            yield return null;
        }
        f(1f);
    }

    static float Suave(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    IEnumerator Pensar(float espera, string s)
    {
        yield return new WaitForSecondsRealtime(espera);
        pensamiento.text = s;
        for (float t = 0f; t < 4.2f; t += Time.unscaledDeltaTime)
        {
            pensamiento.alpha = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((4.2f - t) / 1f);
            yield return null;
        }
        pensamiento.alpha = 0f;
    }

    void LateUpdate()
    {
        // inclinar la cabeza (despues de la animacion)
        if (cabeza != null && Mathf.Abs(inclinarCabeza) > 0.01f && jugador != null)
            cabeza.rotation = Quaternion.AngleAxis(inclinarCabeza, jugador.right) * cabeza.rotation;
    }

    void CrearChispas(Vector3 c, Quaternion r)
    {
        // las chispas de la fogata, recoloreadas de verde y subiendo por toda la cabina
        var fog = GameObject.Find("Fogata");
        var src = fog != null ? fog.transform.Find("Chispas") : null;
        if (src == null) return;
        var go = Instantiate(src.gameObject);
        go.name = "ChispasCabina";
        go.transform.SetPositionAndRotation(c + Vector3.up * 1.2f, Quaternion.Euler(-90f, 0f, 0f));
        go.transform.localScale = Vector3.one;
        chispas = go.GetComponent<ParticleSystem>();
        chispas.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = chispas.main;
        main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.2f); main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f); main.scalingMode = ParticleSystemScalingMode.Local;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 1f, 0.55f), new Color(0.8f, 1f, 0.85f));
        main.maxParticles = 2000; main.gravityModifier = -0.05f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var sh = chispas.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 10.5f; sh.radiusThickness = 1f;
        var em = chispas.emission; em.enabled = true; em.rateOverTime = 90f;
        chispas.Play();
    }

    static Image Img(Transform p, string n, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false);
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static TextMeshProUGUI Txt(Transform p, string n, string s, float tam, TMP_FontAsset f)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false);
        var t = go.AddComponent<TextMeshProUGUI>(); if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.color = Color.white;
        return t;
    }
}
