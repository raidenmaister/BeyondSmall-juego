using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramientas > Cabina > Construir: genera la cabina gigante del centro del salon (MAQUINA),
/// su consola con la bahia de 4 baterias verdes, coloca al jugador dentro (ahi despierta tras la intro)
/// y deja dos baterias: en la camara de la Reina Hormiga y en la cupula de la Reina Abeja.
/// Borra y recrea MAQUINA, BATERIA_* y Assets/Cabina/Generado. Las mallas son de codigo (torno, toros, tubos).
/// Escala: el jugador mide 2 unidades; 1 m real ~ 18 unidades, asi que la cabina (~2.3 m) cabe a alguien grande.
/// </summary>
public static class CabinaBuilder
{
    const string Raiz = "Assets/Cabina/";
    const string Gen = Raiz + "Generado/";
    const string Mat = Raiz + "Materiales/";

    // medidas de la cabina (locales; la puerta mira a +Z)
    const float RVidrio = 13f, YPiso = 1f, YTecho = 37f;
    const float AngPuerta = 36f;                 // la abertura va de -36 a +36 grados
    static readonly float[] AngPostes = { 36f, 93f, 150f, 210f, 267f, 324f };
    static readonly Vector3 PosConsola = new Vector3(27f, 0f, 12f);
    const float YawConsola = -28f;

    static Material mMetal, mMetalClaro, mPanel, mVidrio, mEmisivo, mHaz, mCable, mPeligro, mPiso, mPantalla, mBatVerde, mBatBrillo, mBatMetal, mGoma;
    static Mesh bateriaCuerpo, bateriaTapas, bateriaFranjas;
    static Sprite iconoBateria;
    static readonly List<Renderer> emisivos = new List<Renderer>();
    static readonly List<Renderer> franjas = new List<Renderer>();
    static readonly List<Light> luces = new List<Light>();
    static readonly List<Transform> anillos = new List<Transform>();

    [MenuItem("Herramientas/Cabina/Construir")]
    public static void Construir()
    {
        var log = new System.Text.StringBuilder();
        emisivos.Clear(); franjas.Clear(); luces.Clear(); anillos.Clear();
        contador = 0;
        if (AssetDatabase.IsValidFolder(Gen.TrimEnd('/'))) AssetDatabase.DeleteAsset(Gen.TrimEnd('/'));
        foreach (var c in new[] { "Generado", "Materiales", "Sprites", "Texturas" })
            if (!AssetDatabase.IsValidFolder(Raiz + c)) AssetDatabase.CreateFolder(Raiz.TrimEnd('/'), c);

        var vieja = GameObject.Find("MAQUINA"); if (vieja != null) Object.DestroyImmediate(vieja);
        foreach (var wi in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
            if (wi != null && wi.itemId == MaquinaCrecimiento.ItemBateria) Object.DestroyImmediate(wi.gameObject);

        // centro del salon (el suelo del aula)
        var room = GameObject.Find("class")?.transform.Find("Room")?.GetComponent<Renderer>();
        Vector3 centro = room != null ? new Vector3(room.bounds.center.x, 0f, room.bounds.center.z) : new Vector3(0.6f, 0f, -5.7f);
        // la puerta mira hacia la fogata: es lo primero que se ve al despertar
        float yaw = 0f;
        var fog = GameObject.Find("Fogata");
        if (fog != null) { Vector3 d = fog.transform.position - centro; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        CrearMateriales();
        CrearMallasBateria();
        iconoBateria = IconoBateria();

        var raiz = new GameObject("MAQUINA");
        raiz.transform.SetPositionAndRotation(centro, rot);
        var cabina = new GameObject("Cabina").transform; cabina.SetParent(raiz.transform, false);
        ConstruirCabina(cabina);
        var consola = new GameObject("Consola").transform; consola.SetParent(raiz.transform, false);
        consola.localPosition = PosConsola; consola.localRotation = Quaternion.Euler(0f, YawConsola, 0f);
        var maq = raiz.AddComponent<MaquinaCrecimiento>();
        ConstruirConsola(consola, maq);
        ConstruirCables(raiz.transform);

        maq.emisivos = emisivos.ToArray();
        maq.franjas = franjas.ToArray();
        maq.luces = luces.ToArray();
        maq.anillosGiratorios = anillos.ToArray();
        var intro = Object.FindAnyObjectByType<IntroDespertar>(FindObjectsInactive.Include);
        maq.zumbido = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Menu/Audio/Intro_Zumbido.mp3");
        maq.sonidoEncender = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Menu/Audio/Intro_Encendido.mp3");
        maq.sonidoInsertar = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Menu/Audio/Intro_Carga.mp3");
        if (maq.pantalla != null && intro != null && intro.fuente != null) maq.pantalla.font = intro.fuente;

        foreach (var r in raiz.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = r.sharedMaterial == mVidrio || r.sharedMaterial == mHaz ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;

        Despejar(raiz.transform, centro, log);
        ColocarJugador(raiz.transform, log);
        ColocarBaterias(log);
        RegistrarIcono();

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[CabinaBuilder] listo. Centro " + centro + " yaw " + yaw.ToString("0") + "\n" + log);
    }

    // ------------------------------------------------------------------ cabina
    static void ConstruirCabina(Transform t)
    {
        // plataforma: falda en rampa (el jugador diminuto sube caminando) con franjas de peligro y tapa de metal
        Hijo(t, "Falda", Torno("Falda", new[] { V(21f, 0f), V(17.8f, 0.85f) }, 96, 0f, 360f, 6f), mPeligro, true);
        Hijo(t, "Borde", Torno("Borde", new[] { V(17.8f, 0.85f), V(17.8f, 0.85f), V(17.4f, 1f), V(17.4f, 1f), V(13.6f, 1f) }, 96, 0f, 360f, 4f), mMetal, true);
        Hijo(t, "Piso", Disco("Piso", 13.6f, YPiso, 64, true, false), mPiso, true);
        emisivos.Add(Rend(Hijo(t, "AnilloPiso", Anillo("AnilloPiso", 11.2f, 11.9f, YPiso + 0.02f, 72, true), mEmisivo, false)));
        emisivos.Add(Rend(Hijo(t, "AnilloCentro", Anillo("AnilloCentro", 2.4f, 2.9f, YPiso + 0.02f, 40, true), mEmisivo, false)));
        for (int i = 0; i < 12; i++)
        {
            // rayas radiales que unen los dos anillos
            float a = i * 30f + 15f;
            var g = Hijo(t, "Raya_" + i, Caja(new Vector3(0.18f, 0.02f, 8.2f)), mEmisivo, false);
            g.transform.localPosition = Polar(a, 7.05f, YPiso + 0.02f); g.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            emisivos.Add(Rend(g));
        }

        // cristal (con la abertura de la puerta) y la puerta corrida a un lado
        Hijo(t, "Vidrio", Torno("Vidrio", new[] { V(RVidrio, YPiso), V(RVidrio, YTecho) }, 72, AngPuerta, 360f - AngPuerta, 8f), mVidrio, true);
        Hijo(t, "Puerta", Torno("Puerta", new[] { V(14.9f, YPiso + 0.3f), V(14.9f, YTecho - 0.6f) }, 24, AngPuerta + 3f, AngPuerta + 52f, 8f), mVidrio, true);
        foreach (float a in new[] { AngPuerta + 3f, AngPuerta + 52f })
        {
            var marco = Hijo(t, "MarcoPuerta", Caja(new Vector3(0.6f, YTecho - YPiso - 0.9f, 0.6f)), mMetal, false);
            marco.transform.localPosition = Polar(a, 14.9f, (YPiso + YTecho) * 0.5f - 0.15f); marco.transform.localRotation = Quaternion.Euler(0f, a, 0f);
        }
        Hijo(t, "RielPuerta", Toro("RielPuerta", 14.9f, 0.32f, 40, 8, AngPuerta - 6f, AngPuerta + 60f), mMetalClaro, false).transform.localPosition = Vector3.up * (YTecho - 0.2f);

        // postes con franjas de energia
        foreach (float a in AngPostes)
        {
            var p = Hijo(t, "Poste", Caja(new Vector3(1.8f, YTecho - YPiso + 1.6f, 1.6f)), mPanel, true);
            p.transform.localPosition = Polar(a, 13.4f, (YPiso + YTecho) * 0.5f + 0.3f); p.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            var base0 = Hijo(t, "PiePoste", Caja(new Vector3(2.4f, 1.4f, 2.4f)), mMetal, false);
            base0.transform.localPosition = Polar(a, 13.4f, YPiso + 0.7f); base0.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            var f = Hijo(t, "Franja", Caja(new Vector3(0.55f, 30f, 0.12f)), mEmisivo, false);
            f.transform.localPosition = Polar(a, 14.25f, 19f); f.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            franjas.Add(Rend(f));
            var fi = Hijo(t, "FranjaInterior", Caja(new Vector3(0.4f, 30f, 0.1f)), mEmisivo, false);
            fi.transform.localPosition = Polar(a, 12.55f, 19f); fi.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            emisivos.Add(Rend(fi));
        }
        // aros (los de abajo y en medio dejan libre la puerta; el de arriba es el dintel)
        Hijo(t, "AroBajo", Toro("AroBajo", 13.4f, 0.55f, 72, 10, AngPuerta, 360f - AngPuerta), mMetal, false).transform.localPosition = Vector3.up * (YPiso + 0.45f);
        Hijo(t, "AroMedio", Toro("AroMedio", 13.45f, 0.45f, 72, 10, AngPuerta, 360f - AngPuerta), mMetalClaro, false).transform.localPosition = Vector3.up * 19f;
        Hijo(t, "AroAlto", Toro("AroAlto", 13.6f, 0.9f, 96, 12, 0f, 360f), mMetal, false).transform.localPosition = Vector3.up * (YTecho + 0.4f);

        // techo: tapa con cono, emisor hacia abajo y bobinas que giran
        Hijo(t, "Tapa", Torno("Tapa", new[] { V(9.6f, YTecho - 0.1f), V(15.2f, YTecho - 0.1f), V(15.2f, YTecho - 0.1f), V(15.2f, YTecho + 1.6f), V(15.2f, YTecho + 1.6f), V(14.6f, YTecho + 2.2f), V(10f, YTecho + 4f), V(10f, YTecho + 4f), V(6.8f, YTecho + 4.4f), V(0f, YTecho + 4.4f) }, 96, 0f, 360f, 6f), mPanel, true);
        emisivos.Add(Rend(Hijo(t, "Emisor", Disco("Emisor", 9f, YTecho - 0.15f, 64, false, true), mEmisivo, false)));
        Hijo(t, "AroEmisor", Toro("AroEmisor", 9.3f, 0.4f, 64, 8, 0f, 360f), mMetalClaro, false).transform.localPosition = Vector3.up * (YTecho - 0.2f);
        float[] rb = { 5.6f, 4.3f, 3.0f }, yb = { YTecho + 5.0f, YTecho + 5.9f, YTecho + 6.7f }, gb = { 0.5f, 0.45f, 0.38f };
        for (int i = 0; i < 3; i++)
        {
            var piv = new GameObject("Bobina_" + i).transform; piv.SetParent(t, false); piv.localPosition = Vector3.up * yb[i];
            Hijo(piv, "Aro", Toro("Bobina_" + i, rb[i], gb[i], 48, 8, 0f, 360f), mMetalClaro, false);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f + i * 30f;
                var n = Hijo(piv, "Nodo", Caja(new Vector3(1.3f, gb[i] * 3f, 1.3f)), mMetal, false);
                n.transform.localPosition = Polar(a, rb[i], 0f); n.transform.localRotation = Quaternion.Euler(0f, a, 0f);
                var l = Hijo(piv, "Luz", Caja(new Vector3(0.5f, 0.3f, 1.35f)), mEmisivo, false);
                l.transform.localPosition = Polar(a, rb[i], gb[i] * 1.5f + 0.1f); l.transform.localRotation = Quaternion.Euler(0f, a, 0f);
                emisivos.Add(Rend(l));
            }
            anillos.Add(piv);
        }
        Hijo(t, "Mastil", Torno("Mastil", new[] { V(1.2f, YTecho + 4.3f), V(1.2f, YTecho + 7.4f), V(1.2f, YTecho + 7.4f), V(0.5f, YTecho + 7.8f), V(0f, YTecho + 7.8f) }, 20, 0f, 360f, 2f), mMetal, false);

        // haz de energia (solo se ve encendida)
        var haz = Hijo(t, "Haz", Torno("Haz", new[] { V(8.6f, YPiso + 0.1f), V(8.6f, YTecho - 0.3f) }, 48, 0f, 360f, 10f), mHaz, false);
        haz.GetComponent<MeshRenderer>().enabled = false;
        hazRenderer = Rend(haz);

        // luces: una adentro, una en el emisor
        luces.Add(Luz(t, "LuzInterior", new Vector3(0f, 26f, 0f), 46f, 260f));
        luces.Add(Luz(t, "LuzPiso", new Vector3(0f, 4f, 0f), 22f, 40f));
    }
    static Renderer hazRenderer;

    // ------------------------------------------------------------------ consola y bahia
    static void ConstruirConsola(Transform t, MaquinaCrecimiento maq)
    {
        maq.haz = hazRenderer;
        Hijo(t, "Mueble", Caja(new Vector3(14f, 15f, 9f)), mPanel, true).transform.localPosition = new Vector3(0f, 7.9f, 0f);
        Hijo(t, "Zocalo", Caja(new Vector3(14.6f, 0.8f, 9.6f)), mMetal, true).transform.localPosition = new Vector3(0f, 0.4f, 0f);
        var mesa = Hijo(t, "Mesa", Caja(new Vector3(15f, 1.2f, 10f)), mMetal, true);
        mesa.transform.localPosition = new Vector3(0f, 15.9f, 0.6f); mesa.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
        // botones y palancas sobre la mesa
        var rnd = new System.Random(7);
        for (int i = 0; i < 10; i++)
        {
            var b = Hijo(mesa.transform, "Boton", Torno("Boton", new[] { V(0.45f, 0f), V(0.45f, 0.35f), V(0.45f, 0.35f), V(0f, 0.35f) }, 14, 0f, 360f, 1f), i % 3 == 0 ? mEmisivo : mMetalClaro, false);
            b.transform.localPosition = new Vector3(-5.5f + (i % 5) * 2.6f, 0.6f, -2.5f + (i / 5) * 2.2f + (float)rnd.NextDouble() * 0.3f);
            if (i % 3 == 0) emisivos.Add(Rend(b));
        }
        for (int i = 0; i < 2; i++)
        {
            var pal = new GameObject("Palanca").transform; pal.SetParent(mesa.transform, false);
            pal.localPosition = new Vector3(3.5f + i * 2.2f, 0.6f, 2.6f); pal.localRotation = Quaternion.Euler(-25f + i * 40f, 0f, 0f);
            Hijo(pal, "Vara", Caja(new Vector3(0.3f, 3f, 0.3f)), mMetalClaro, false).transform.localPosition = Vector3.up * 1.5f;
            Hijo(pal, "Pomo", Torno("Pomo", Esfera(0.5f, 8), 12, 0f, 360f, 1f), mPeligro, false).transform.localPosition = Vector3.up * 3.1f;
        }
        // pantalla
        Hijo(t, "Monitor", Caja(new Vector3(12f, 8f, 1.4f)), mMetal, true).transform.localPosition = new Vector3(0f, 21.6f, -3.6f);
        var vidrioPant = Hijo(t, "Pantalla", Caja(new Vector3(11f, 7f, 0.1f)), mPantalla, false); vidrioPant.transform.localPosition = new Vector3(0f, 21.6f, -2.86f);
        var txt = new GameObject("Texto").AddComponent<TextMeshPro>();
        txt.transform.SetParent(t, false); txt.transform.localPosition = new Vector3(0f, 21.6f, -2.78f); txt.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        txt.rectTransform.sizeDelta = new Vector2(10.5f, 6.5f);
        txt.fontSize = 22f; txt.alignment = TextAlignmentOptions.Center; txt.fontStyle = FontStyles.Bold; txt.text = "ENERGIA\n0/4";
        maq.pantalla = txt;
        luces.Add(Luz(t, "LuzPantalla", new Vector3(0f, 21f, 2.5f), 22f, 18f));

        // bahia de baterias, al ras del suelo para el jugador diminuto
        var bahia = Hijo(t, "Bahia", Caja(new Vector3(13f, 3.2f, 3.6f)), mMetal, true);
        bahia.transform.localPosition = new Vector3(0f, 1.6f, 4.5f + 1.8f);
        Hijo(t, "TapaBahia", Caja(new Vector3(13.4f, 0.3f, 3.9f)), mPeligro, false).transform.localPosition = new Vector3(0f, 3.35f, 6.3f);
        float frente = 4.5f + 3.6f;
        maq.bateriasRanura = new GameObject[MaquinaCrecimiento.Necesarias];
        maq.ledsRanura = new Renderer[MaquinaCrecimiento.Necesarias];
        for (int i = 0; i < MaquinaCrecimiento.Necesarias; i++)
        {
            float x = 4.2f - i * 2.8f;     // de izquierda a derecha vista de frente
            var aro = Hijo(t, "Ranura_" + i, Toro("Ranura", 0.62f, 0.16f, 20, 8, 0f, 360f), mMetalClaro, false);
            aro.transform.localPosition = new Vector3(x, 1.45f, frente + 0.02f); aro.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var hueco = Hijo(t, "Hueco_" + i, Disco("Hueco", 0.6f, 0f, 20, true, false), mGoma, false);
            hueco.transform.localPosition = new Vector3(x, 1.45f, frente + 0.01f); hueco.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var led = Hijo(t, "Led_" + i, Caja(new Vector3(0.9f, 0.35f, 0.15f)), mEmisivo, false);
            led.transform.localPosition = new Vector3(x, 2.65f, frente + 0.06f);
            maq.ledsRanura[i] = Rend(led);
            var bat = ModeloBateria("BateriaRanura_" + i, t);
            bat.transform.localPosition = new Vector3(x, 1.45f, frente - 0.35f); bat.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bat.SetActive(false);
            maq.bateriasRanura[i] = bat;
        }
        var cartel = new GameObject("Cartel").AddComponent<TextMeshPro>();
        cartel.transform.SetParent(t, false); cartel.transform.localPosition = new Vector3(0f, 4.9f, 4.53f); cartel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        cartel.rectTransform.sizeDelta = new Vector2(12f, 1.6f); cartel.fontSize = 9f; cartel.alignment = TextAlignmentOptions.Center; cartel.fontStyle = FontStyles.Bold;
        cartel.text = "BATERIAS"; cartel.color = new Color(0.95f, 0.75f, 0.15f);
        var intro = Object.FindAnyObjectByType<IntroDespertar>(FindObjectsInactive.Include);
        if (intro != null && intro.fuente != null) cartel.font = intro.fuente;

        var punto = new GameObject("PuntoBahia").transform; punto.SetParent(t, false); punto.localPosition = new Vector3(0f, 0f, frente + 1.5f);
        maq.bahia = punto;
    }

    static void ConstruirCables(Transform raiz)
    {
        // de la parte trasera de la consola a la falda de la plataforma, por el suelo (radio < paso del jugador)
        Vector3 c = PosConsola; Quaternion q = Quaternion.Euler(0f, YawConsola, 0f);
        float[] ang = { 58f, 68f, 78f };
        for (int i = 0; i < 3; i++)
        {
            float r = 0.24f + i * 0.03f;
            Vector3 a = c + q * new Vector3(-7.4f, 6f + i * 2f, -2f + i * 1.8f);
            Vector3 b = c + q * new Vector3(-8.6f, r, -2.5f + i * 2.2f);
            Vector3 m = Vector3.Lerp(b, Polar(ang[i], 21f, r), 0.5f) + new Vector3(Mathf.Sin(i * 2.1f) * 1.5f, 0f, Mathf.Cos(i * 1.3f) * 1.5f);
            Vector3 e = Polar(ang[i], 20.4f, r + 0.1f);
            Vector3 f = Polar(ang[i], 18.2f, 0.75f);
            Hijo(raiz, "Cable_" + i, Tubo("Cable_" + i, new[] { a, a + Vector3.down * 2f, b, m, e, f }, r, 8), mCable, false);
        }
    }

    // ------------------------------------------------------------------ escena
    static void Despejar(Transform maq, Vector3 centro, System.Text.StringBuilder log)
    {
        // escombros del salon que quedarian dentro de la plataforma o de la consola
        Vector3 cons = maq.TransformPoint(PosConsola);
        var decay = GameObject.Find("DECAY_ROOT");
        if (decay != null)
            foreach (var grupo in new[] { "Planks", "Furniture", "Vegetation", "Trash" })
            {
                var g = decay.transform.Find(grupo); if (g == null) continue;
                foreach (Transform hijo in g)
                {
                    if (!hijo.gameObject.activeSelf) continue;
                    var rs = hijo.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                    Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (b.min.y > 30f) continue;   // plantas del techo
                    if (CirculoCaja(centro, 24f, b) || CirculoCaja(cons, 13f, b)) { hijo.gameObject.SetActive(false); log.AppendLine("oculto " + grupo + "/" + hijo.name); }
                }
            }
        // objetos sueltos (items, bichos) que quedarian debajo: se empujan afuera
        var excluir = new HashSet<string> { "MAQUINA", "class", "DECAY_ROOT", "EXTERIOR", "HORMIGUERO", "ARBOL_COLMENA", "Jugador", "Fogata", "CicloDiaNoche", "FloorItemPromptManager", "InventoryManager", "PlayerHUD_Canvas", "Main Camera", "FreeLook Camera" };
        foreach (var go in maq.gameObject.scene.GetRootGameObjects())
        {
            if (excluir.Contains(go.name)) continue;
            var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.magnitude > 12f || b.center.y > 20f) continue;
            foreach (var (p, radio) in new[] { (centro, 24f), (cons, 13f) })
            {
                Vector3 d = b.center - p; d.y = 0f;
                if (d.magnitude >= radio) continue;
                Vector3 dir = d.sqrMagnitude < 0.01f ? Vector3.forward : d.normalized;
                Vector3 mover = dir * (radio + 3f - d.magnitude);
                Undo.RecordObject(go.transform, "Despejar");
                go.transform.position += mover;
                log.AppendLine("movido " + go.name + " " + mover.magnitude.ToString("0.0"));
            }
        }
    }

    static bool CirculoCaja(Vector3 c, float r, Bounds b)
    {
        float x = Mathf.Clamp(c.x, b.min.x, b.max.x), z = Mathf.Clamp(c.z, b.min.z, b.max.z);
        return (x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < r * r;
    }

    static void ColocarJugador(Transform maq, System.Text.StringBuilder log)
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc == null) { log.AppendLine("sin jugador"); return; }
        // despierta al fondo de la cabina, mirando a la puerta
        Vector3 pos = maq.TransformPoint(new Vector3(0f, YPiso + 0.05f, -4f));
        Quaternion rot = maq.rotation;
        Undo.RecordObject(pc.transform, "Jugador");
        pc.transform.SetPositionAndRotation(pos, rot);
        float yaw = rot.eulerAngles.y; if (yaw > 180f) yaw -= 360f;
        var fl = GameObject.Find("FreeLook Camera");
        if (fl != null)
        {
            Undo.RecordObject(fl.transform, "Camara");
            fl.transform.SetPositionAndRotation(pos - rot * Vector3.forward * 2.7f + Vector3.up * 1.75f, rot);
            foreach (var c in fl.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "CinemachineOrbitalFollow") continue;
                var so = new SerializedObject(c);
                so.FindProperty("HorizontalAxis.Value").floatValue = yaw;
                so.FindProperty("HorizontalAxis.Center").floatValue = yaw;
                so.ApplyModifiedProperties();
            }
        }
        var cam = Camera.main;
        if (cam != null) { Undo.RecordObject(cam.transform, "Camara"); cam.transform.SetPositionAndRotation(pos - rot * Vector3.forward * 2.7f + Vector3.up * 1.75f, rot); }
        log.AppendLine("jugador en " + pos + " yaw " + yaw.ToString("0"));
    }

    static void ColocarBaterias(System.Text.StringBuilder log)
    {
        // 1) camara de la Reina Hormiga: al pie del hongo gigante, detras de ella
        var rh = Object.FindAnyObjectByType<ReinaHormiga>(FindObjectsInactive.Include);
        if (rh != null)
        {
            Vector3 h = rh.hogar;
            Vector3 atras = -rh.transform.forward; atras.y = 0f; atras.Normalize();
            Vector3 lado = Vector3.Cross(Vector3.up, atras);
            Vector3 p = h - lado * 9f - atras * 3f;   // a un costado, fuera del monton de hojas del hongo
            CrearBateria("BATERIA_Hormiguero", Suelo(p, h.y), log);
        }
        else log.AppendLine("no hay ReinaHormiga");
        // 2) cupula de la Reina Abeja: en el piso de cera, al fondo (lejos de la rampa de entrada)
        var ra = Object.FindAnyObjectByType<ReinaAbeja>(FindObjectsInactive.Include);
        if (ra != null)
        {
            Vector3 c = ra.centroCamara;
            float rad = ra.radioCamara * 0.55f;
            var cand = new List<Vector3>();
            Vector3 entrada = c; float yMin = float.MaxValue;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 s = Suelo(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad, c.y);
                cand.Add(s);
                if (s.y < yMin) { yMin = s.y; entrada = s; }      // donde el piso se hunde esta la rampa
            }
            Vector3 mejor = c; float lejos = -1f;
            foreach (var s in cand)
            {
                if (Mathf.Abs(s.y - c.y) > 0.4f) continue;           // solo piso plano de cera
                float d = (s - entrada).sqrMagnitude;
                if (d > lejos) { lejos = d; mejor = s; }
            }
            CrearBateria("BATERIA_Colmena", mejor, log);
        }
        else log.AppendLine("no hay ReinaAbeja");
    }

    // el suelo mas cercano a la altura de referencia (evita el sombrero del hongo y techos)
    static Vector3 Suelo(Vector3 p, float yRef)
    {
        var desde = new Vector3(p.x, yRef + 30f, p.z);
        Vector3 mejor = new Vector3(p.x, yRef, p.z); float dMin = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(desde, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<ReinaHormiga>() != null || hit.collider.GetComponentInParent<ReinaAbeja>() != null) continue;
            if (hit.normal.y < 0.6f) continue;
            float d = Mathf.Abs(hit.point.y - yRef);
            if (d < dMin) { dMin = d; mejor = hit.point; }
        }
        return mejor;
    }

    static void CrearBateria(string nombre, Vector3 pos, System.Text.StringBuilder log)
    {
        var go = new GameObject(nombre);
        go.transform.position = pos + Vector3.up * 0.05f;
        var vis = ModeloBateria("Visual", go.transform);
        var wi = go.AddComponent<WorldItem>();
        wi.itemId = MaquinaCrecimiento.ItemBateria; wi.displayName = "Bateria verde"; wi.itemIcon = iconoBateria; wi.quantity = 1;
        var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0f, 0.85f, 0f); bc.size = new Vector3(1.2f, 1.9f, 1.2f);
        var sp = go.AddComponent<ItemSparkle>(); sp.color = new Color(0.5f, 1f, 0.6f, 1f);
        var l = new GameObject("Luz").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        l.type = LightType.Point; l.color = new Color(0.35f, 1f, 0.45f); l.range = 12f; l.intensity = 14f; l.shadows = LightShadows.None;
        var bv = go.AddComponent<BateriaVerde>(); bv.visual = vis.transform; bv.luz = l;
        log.AppendLine(nombre + " en " + go.transform.position);
    }

    static void RegistrarIcono()
    {
        var inv = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv == null || iconoBateria == null) return;
        if (inv.iconDatabase == null) inv.iconDatabase = new List<Sprite>();
        inv.iconDatabase.RemoveAll(s => s == null || s.name == "Bateria");
        inv.iconDatabase.Add(iconoBateria);
        EditorUtility.SetDirty(inv);
    }

    // ------------------------------------------------------------------ bateria
    static void CrearMallasBateria()
    {
        bateriaCuerpo = Guardar(Torno("BateriaCuerpo", new[] { V(0.42f, 0.08f), V(0.42f, 1.42f) }, 24, 0f, 360f, 1f), "BateriaCuerpo");
        bateriaTapas = Guardar(Combinar("BateriaTapas",
            Torno("Abajo", new[] { V(0f, 0f), V(0.4f, 0f), V(0.4f, 0f), V(0.435f, 0.03f), V(0.435f, 0.12f) }, 24, 0f, 360f, 1f),
            Torno("Arriba", new[] { V(0.435f, 1.38f), V(0.435f, 1.47f), V(0.435f, 1.47f), V(0.4f, 1.5f), V(0.17f, 1.5f), V(0.17f, 1.5f), V(0.17f, 1.62f), V(0.17f, 1.62f), V(0.13f, 1.65f), V(0f, 1.65f) }, 24, 0f, 360f, 1f)), "BateriaTapas");
        bateriaFranjas = Guardar(Combinar("BateriaFranjas",
            Torno("F1", new[] { V(0.428f, 0.5f), V(0.428f, 0.62f) }, 24, 0f, 360f, 1f),
            Torno("F2", new[] { V(0.428f, 1.0f), V(0.428f, 1.12f) }, 24, 0f, 360f, 1f),
            Torno("Rayo", new[] { V(0.43f, 0.68f), V(0.43f, 0.94f) }, 24, -14f, 14f, 1f)), "BateriaFranjas");
    }

    static GameObject ModeloBateria(string nombre, Transform padre)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        Hijo(go.transform, "Cuerpo", bateriaCuerpo, mBatVerde, false);
        Hijo(go.transform, "Tapas", bateriaTapas, mBatMetal, false);
        Hijo(go.transform, "Franjas", bateriaFranjas, mBatBrillo, false);
        return go;
    }

    static Sprite IconoBateria()
    {
        string path = Raiz + "Sprites/Bateria.png";
        int s = 128; var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = x / (float)s, v = y / (float)s; Color c = Color.clear;
                float cx = Mathf.Abs(u - 0.5f);
                // cuerpo con esquinas redondeadas
                if (cx < 0.2f && v > 0.1f && v < 0.82f)
                {
                    float sombra = 1f - cx / 0.2f * 0.45f;
                    c = new Color(0.14f, 0.62f, 0.25f) * sombra; c.a = 1f;
                    if ((v > 0.28f && v < 0.33f) || (v > 0.62f && v < 0.67f)) c = new Color(0.55f, 1f, 0.6f);
                }
                if (cx < 0.205f && ((v > 0.07f && v < 0.12f) || (v > 0.8f && v < 0.85f))) c = new Color(0.75f, 0.77f, 0.8f);
                if (cx < 0.08f && v >= 0.85f && v < 0.92f) c = new Color(0.75f, 0.77f, 0.8f);
                // rayo
                float ry = (v - 0.36f) / 0.24f;
                if (ry > 0f && ry < 1f)
                {
                    float xr = ry < 0.5f ? Mathf.Lerp(0.56f, 0.44f, ry * 2f) : Mathf.Lerp(0.56f, 0.44f, (ry - 0.5f) * 2f);
                    if (Mathf.Abs(u - xr) < 0.045f) c = new Color(1f, 1f, 0.75f);
                }
                if (c.a > 0f && (cx > 0.19f && cx < 0.205f && v > 0.1f && v < 0.85f)) c = new Color(0.05f, 0.2f, 0.08f);
                px[y * s + x] = c;
            }
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------------ materiales
    static void CrearMateriales()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        mMetal = CrearMat("Metal", lit, new Color(0.12f, 0.13f, 0.15f), 0.85f, 0.55f);
        mMetalClaro = CrearMat("MetalClaro", lit, new Color(0.62f, 0.65f, 0.68f), 0.9f, 0.72f);
        mPanel = CrearMat("Panel", lit, new Color(0.8f, 0.82f, 0.84f), 0.15f, 0.55f);
        mCable = CrearMat("Cable", lit, new Color(0.05f, 0.05f, 0.06f), 0f, 0.35f);
        mGoma = CrearMat("Hueco", lit, new Color(0.02f, 0.02f, 0.02f), 0f, 0.1f);
        mBatMetal = CrearMat("BateriaMetal", lit, new Color(0.78f, 0.8f, 0.82f), 1f, 0.8f);
        mBatVerde = CrearMat("BateriaVerde", lit, new Color(0.1f, 0.55f, 0.2f), 0.2f, 0.7f, new Color(0.06f, 0.75f, 0.2f));
        mBatBrillo = CrearMat("BateriaBrillo", lit, new Color(0.5f, 1f, 0.6f), 0f, 0.6f, new Color(1.2f, 5f, 1.6f));
        mEmisivo = CrearMat("Energia", lit, new Color(0.04f, 0.08f, 0.05f), 0f, 0.8f, new Color(0.3f, 1f, 0.45f));
        mPantalla = CrearMat("Pantalla", lit, new Color(0.01f, 0.03f, 0.02f), 0f, 0.95f, new Color(0.01f, 0.06f, 0.03f));
        mPeligro = CrearMat("Peligro", lit, Color.white, 0.2f, 0.4f);
        mPeligro.SetTexture("_BaseMap", TexturaPeligro());
        mPiso = CrearMat("PisoCabina", lit, Color.white, 0.75f, 0.6f);
        mPiso.SetTexture("_BaseMap", TexturaPiso());

        mVidrio = CrearMat("Vidrio", lit, new Color(0.62f, 0.85f, 0.88f, 0.13f), 0f, 0.96f);
        Transparente(mVidrio);
        mVidrio.SetFloat("_Cull", 0f);
        mHaz = CrearMat("Haz", Shader.Find("Universal Render Pipeline/Unlit"), new Color(0.3f, 1f, 0.45f, 0.15f), 0f, 0f);
        Transparente(mHaz);
        mHaz.SetFloat("_Cull", 0f);
        mHaz.SetFloat("_DstBlend", 1f);     // aditivo
    }

    static Material CrearMat(string n, Shader sh, Color c, float metal, float suave, Color emision = default)
    {
        string path = Mat + n + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", suave);
        if (emision.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emision);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Transparente(Material m)
    {
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", 10f);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = 3000;
    }

    static Texture2D TexturaPeligro()
    {
        string path = Raiz + "Texturas/Peligro.png";
        int s = 256; var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool amarillo = ((x + y) / 64) % 2 == 0;
                float sucio = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.25f;
                Color c = amarillo ? new Color(0.92f, 0.7f, 0.08f) : new Color(0.06f, 0.06f, 0.06f);
                t.SetPixel(x, y, Color.Lerp(c, new Color(0.25f, 0.22f, 0.18f), sucio));
            }
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D TexturaPiso()
    {
        // placa metalica circular: surcos concentricos, rejilla y tornillos (UV planar: centro en 0.5,0.5)
        string path = Raiz + "Texturas/PisoCabina.png";
        int s = 1024; var t = new Texture2D(s, s, TextureFormat.RGBA32, true);
        var px = new Color[s * s];
        float[] surcos = { 0.25f, 0.5f, 0.72f, 0.94f };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = x / (float)s * 2f - 1f, v = y / (float)s * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ruido = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.08f + Mathf.PerlinNoise(x * 0.2f, y * 0.2f) * 0.03f;
                float g = 0.36f + ruido;
                // rejilla de placas
                float gu = Mathf.Abs(Mathf.Repeat(u * 6f, 1f) - 0.5f), gv = Mathf.Abs(Mathf.Repeat(v * 6f, 1f) - 0.5f);
                if (gu > 0.485f || gv > 0.485f) g *= 0.55f;
                // surcos concentricos
                foreach (float rr in surcos) if (Mathf.Abs(r - rr) < 0.006f) g *= 0.35f;
                // tornillos en el anillo exterior
                float ang = Mathf.Atan2(v, u);
                float sec = Mathf.Repeat(ang / (Mathf.PI * 2f) * 24f, 1f) - 0.5f;
                if (Mathf.Abs(r - 0.97f) < 0.012f && Mathf.Abs(sec) < 0.03f) g = 0.7f;
                px[y * s + x] = new Color(g * 0.95f, g, g * 1.03f, 1f);
            }
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ mallas
    static Vector2 V(float r, float y) => new Vector2(r, y);
    static Vector3 Polar(float angGrados, float r, float y) { float a = angGrados * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }

    static int contador;
    static Mesh Guardar(Mesh m, string nombre)
    {
        string path = Gen + nombre + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Hijo(Transform padre, string nombre, Mesh malla, Material mat, bool colision)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        if (!AssetDatabase.Contains(malla)) Guardar(malla, malla.name + "_" + (contador++));
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (colision) go.AddComponent<MeshCollider>().sharedMesh = malla;
        return go;
    }

    static Renderer Rend(GameObject g) => g.GetComponent<MeshRenderer>();

    static Light Luz(Transform padre, string n, Vector3 pos, float rango, float intensidad)
    {
        var l = new GameObject(n).AddComponent<Light>();
        l.transform.SetParent(padre, false); l.transform.localPosition = pos;
        l.type = LightType.Point; l.range = rango; l.intensity = intensidad; l.shadows = LightShadows.None;
        l.color = new Color(0.3f, 1f, 0.45f);
        return l;
    }

    /// <summary>
    /// Torno: gira un perfil (r, y) entre dos angulos (grados desde +Z hacia +X).
    /// La normal queda a la derecha del recorrido del perfil: (dy, -dr). Repetir un punto = arista dura.
    /// </summary>
    static Mesh Torno(string nombre, Vector2[] perfil, int lados, float a0, float a1, float escalaUV)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        int cols = Mathf.Max(2, Mathf.CeilToInt(lados * Mathf.Abs(a1 - a0) / 360f) + 1);
        float[] largo = new float[perfil.Length];
        for (int i = 1; i < perfil.Length; i++) largo[i] = largo[i - 1] + Vector2.Distance(perfil[i], perfil[i - 1]);
        float rMax = 0f; foreach (var p in perfil) rMax = Mathf.Max(rMax, p.x);
        for (int i = 0; i < perfil.Length; i++)
            for (int k = 0; k < cols; k++)
            {
                float a = Mathf.Lerp(a0, a1, k / (cols - 1f)) * Mathf.Deg2Rad;
                v.Add(new Vector3(Mathf.Sin(a) * perfil[i].x, perfil[i].y, Mathf.Cos(a) * perfil[i].x));
                uv.Add(new Vector2((a - a0 * Mathf.Deg2Rad) * rMax / escalaUV, largo[i] / escalaUV));
            }
        for (int i = 0; i < perfil.Length - 1; i++)
        {
            if ((perfil[i + 1] - perfil[i]).sqrMagnitude < 1e-8f) continue;   // arista dura
            for (int k = 0; k < cols - 1; k++)
            {
                int a = i * cols + k, b = a + 1, c = a + cols, d = c + 1;
                t.AddRange(new[] { a, b, c, b, d, c });
            }
        }
        var m = new Mesh { name = nombre };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals();
        // costura de una vuelta completa: promediar normales
        if (Mathf.Abs(Mathf.Abs(a1 - a0) - 360f) < 0.01f)
        {
            var n = m.normals;
            for (int i = 0; i < perfil.Length; i++)
            {
                int a = i * cols, b = i * cols + cols - 1;
                Vector3 p = (n[a] + n[b]).normalized; n[a] = n[b] = p;
            }
            m.normals = n;
        }
        m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    static Vector2[] Esfera(float r, int pasos)
    {
        // perfil de media esfera de abajo hacia arriba (normal hacia afuera)
        var p = new Vector2[pasos + 1];
        for (int i = 0; i <= pasos; i++) { float a = -Mathf.PI * 0.5f + Mathf.PI * i / pasos; p[i] = new Vector2(Mathf.Cos(a) * r + 1e-4f, Mathf.Sin(a) * r); }
        return p;
    }

    static Mesh Toro(string nombre, float R, float r, int lados, int seccion, float a0, float a1)
    {
        var p = new Vector2[seccion + 1];
        for (int i = 0; i <= seccion; i++) { float a = i * Mathf.PI * 2f / seccion; p[i] = new Vector2(R + Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
        return Torno(nombre, p, lados, a0, a1, 2f);
    }

    static Mesh Disco(string nombre, float r, float y, int lados, bool arriba, bool abajo)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        v.Add(new Vector3(0f, y, 0f)); uv.Add(new Vector2(0.5f, 0.5f));
        for (int k = 0; k <= lados; k++)
        {
            float a = k * Mathf.PI * 2f / lados;
            v.Add(new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r));
            uv.Add(new Vector2(0.5f + Mathf.Sin(a) * 0.5f, 0.5f + Mathf.Cos(a) * 0.5f));
        }
        for (int k = 1; k <= lados; k++)
        {
            if (arriba) t.AddRange(new[] { 0, k, k + 1 });
            if (abajo) t.AddRange(new[] { 0, k + 1, k });
        }
        var m = new Mesh { name = nombre }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    static Mesh Anillo(string nombre, float r0, float r1, float y, int lados, bool arriba)
    {
        return Torno(nombre, arriba ? new[] { V(r1, y), V(r0, y) } : new[] { V(r0, y), V(r1, y) }, lados, 0f, 360f, 1f);
    }

    static Mesh Caja(Vector3 tam)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(go);
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], tam);
        m.vertices = v; m.RecalculateBounds(); m.name = "Caja";
        return m;
    }

    static Mesh Tubo(string nombre, Vector3[] ctrl, float r, int lados)
    {
        // Catmull-Rom por los puntos de control
        var pts = new List<Vector3>();
        for (int i = 0; i < ctrl.Length - 1; i++)
        {
            Vector3 p0 = ctrl[Mathf.Max(0, i - 1)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(ctrl.Length - 1, i + 2)];
            for (int s = 0; s < 10; s++)
            {
                float u = s / 10f;
                pts.Add(0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u));
            }
        }
        pts.Add(ctrl[ctrl.Length - 1]);
        var v = new List<Vector3>(); var t = new List<int>();
        Vector3 normalAnt = Vector3.up;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 dir = (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
            Vector3 n = Vector3.Cross(dir, Vector3.Cross(normalAnt, dir)).normalized;
            if (n.sqrMagnitude < 0.01f) n = Vector3.Cross(dir, Vector3.right).normalized;
            normalAnt = n;
            Vector3 b = Vector3.Cross(dir, n);
            for (int k = 0; k < lados; k++) { float a = k * Mathf.PI * 2f / lados; v.Add(pts[i] + (n * Mathf.Cos(a) + b * Mathf.Sin(a)) * r); }
        }
        for (int i = 0; i < pts.Count - 1; i++)
            for (int k = 0; k < lados; k++)
            {
                int a = i * lados + k, b2 = i * lados + (k + 1) % lados, c = a + lados, d = b2 + lados;
                t.AddRange(new[] { a, c, b2, b2, c, d });
            }
        var m = new Mesh { name = nombre }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        // si quedo al reves (normales hacia adentro), voltear
        var nn = m.normals; if (nn.Length > 0 && Vector3.Dot(nn[0], v[0] - pts[0]) < 0f) { for (int i = 0; i < t.Count; i += 3) { int x = t[i]; t[i] = t[i + 1]; t[i + 1] = x; } m.SetTriangles(t, 0); m.RecalculateNormals(); }
        return m;
    }

    static Mesh Combinar(string nombre, params Mesh[] mallas)
    {
        var ci = new CombineInstance[mallas.Length];
        for (int i = 0; i < mallas.Length; i++) ci[i] = new CombineInstance { mesh = mallas[i], transform = Matrix4x4.identity };
        var m = new Mesh { name = nombre }; m.CombineMeshes(ci, true, true);
        return m;
    }
}
