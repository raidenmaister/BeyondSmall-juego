using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramientas > Animales > Colocar por progresion: reparte los bichos de la superficie (salon y exterior)
/// en zonas de dificultad creciente, para que el jugador aprenda poco a poco:
///   Salon  : donde despiertas. Solo bichos tranquilos (se defienden si los golpeas, pegan poco) y mariposas.
///            Cochinillas junto a la fogata y los borradores: carne para cocinar y caparazon para el escudo.
///   Patio  : afuera de la puerta. Lombrices debiles (primeras peleas, dan carne), una cochinilla tranquila,
///            mariposas y una oruga en el borde (primer ataque a distancia).
///   Jardin : a media distancia. Hormigas y un ciempies en el camino al hormiguero, abejas alrededor del arbol,
///            escarabajos y orugas repartidos.
///   Lejos  : cerca de la cerca y alrededor de la cloaca. Arañas, ciempies y lo mas peligroso;
///            la rata grande se va al callejon detras del salon.
/// Reutiliza los bichos que ya hay en la escena (mismo ajuste, mismos drops); si faltan de un tipo, duplica uno.
/// Los bichos del hormiguero, la colmena y la cloaca no se tocan. Se puede volver a ejecutar sin problema.
/// </summary>
public static class ProgresionAnimales
{
    enum Zona { Salon, Patio, Jardin, Lejos }

    class Lugar
    {
        public Zona zona; public string tipo; public float x, z;
        public bool pasivo; public float danio = -1f, vida = -1f; public bool carne;
    }

    static Lugar P(Zona zona, string tipo, float x, float z) { return new Lugar { zona = zona, tipo = tipo, x = x, z = z }; }

    // tipo: Butterfly, Woodlouse, Worm, Caterpillar, Ants, Beetle, Bee, Centipede, Spider, Rat
    static List<Lugar> Tabla()
    {
        return new List<Lugar>
        {
            // ---- salon: aprender a pegar, recoger y cocinar sin riesgo
            new Lugar { zona = Zona.Salon, tipo = "Woodlouse", x = -45f, z = -62f, pasivo = true, danio = 5f, vida = 60f, carne = true },   // junto a la fogata y el carbon
            new Lugar { zona = Zona.Salon, tipo = "Woodlouse", x = -48f, z = 35f, pasivo = true, danio = 5f, vida = 60f, carne = true },    // junto a borradores y ligas
            new Lugar { zona = Zona.Salon, tipo = "Beetle", x = 45f, z = 38f, pasivo = true, danio = 8f, vida = 60f },                      // junto a la puerta: aprender a esquivar
            P(Zona.Salon, "Butterfly", -15f, 45f), P(Zona.Salon, "Butterfly", 35f, -45f),

            // ---- patio: primeras peleas de verdad, pero suaves
            P(Zona.Patio, "Worm", 112f, 72f), P(Zona.Patio, "Worm", 128f, 25f), P(Zona.Patio, "Worm", 150f, 110f),
            new Lugar { zona = Zona.Patio, tipo = "Woodlouse", x = 140f, z = 60f, pasivo = true, danio = 10f },
            P(Zona.Patio, "Butterfly", 120f, 100f), P(Zona.Patio, "Butterfly", 105f, 10f),
            new Lugar { zona = Zona.Patio, tipo = "Caterpillar", x = 178f, z = 140f, danio = 30f },

            // ---- jardin: camino al hormiguero (sureste)
            P(Zona.Jardin, "Ants", 150f, -60f), P(Zona.Jardin, "Ants", 175f, -105f), P(Zona.Jardin, "Beetle", 110f, -140f),
            P(Zona.Jardin, "Worm", 90f, -150f), P(Zona.Jardin, "Ants", 260f, -120f), P(Zona.Jardin, "Ants", 150f, -190f),
            P(Zona.Jardin, "Centipede", 250f, -210f),
            // ---- jardin: este y noreste
            P(Zona.Jardin, "Ants", 190f, 60f), P(Zona.Jardin, "Beetle", 230f, 110f), P(Zona.Jardin, "Bee", 160f, 170f),
            P(Zona.Jardin, "Caterpillar", 260f, 20f), P(Zona.Jardin, "Woodlouse", 280f, -40f), P(Zona.Jardin, "Butterfly", 240f, 40f),
            // ---- jardin: alrededor del arbol de la colmena (suroeste)
            P(Zona.Jardin, "Bee", -150f, -110f), P(Zona.Jardin, "Bee", -170f, -200f), P(Zona.Jardin, "Bee", -260f, -60f),
            P(Zona.Jardin, "Bee", -300f, -150f), P(Zona.Jardin, "Bee", -210f, -40f),
            P(Zona.Jardin, "Caterpillar", -120f, -150f), P(Zona.Jardin, "Woodlouse", -110f, -130f), P(Zona.Jardin, "Butterfly", -140f, -60f),
            // ---- jardin: oeste
            P(Zona.Jardin, "Beetle", -150f, 10f), P(Zona.Jardin, "Ants", -180f, 60f), P(Zona.Jardin, "Caterpillar", -200f, 120f),
            P(Zona.Jardin, "Woodlouse", -130f, 90f), P(Zona.Jardin, "Butterfly", -150f, 150f),
            // la rata grande: en el callejon detras del salon (suelo plano junto a la pared)
            P(Zona.Jardin, "Rat", -91f, 20f),

            // ---- lejos: alrededor de la cloaca (norte) y junto a la cerca
            P(Zona.Lejos, "Spider", -20f, 270f), P(Zona.Lejos, "Spider", 130f, 265f), P(Zona.Lejos, "Centipede", -70f, 240f),
            P(Zona.Lejos, "Spider", -330f, -50f), P(Zona.Lejos, "Spider", 200f, -330f), P(Zona.Lejos, "Spider", -120f, -360f), P(Zona.Lejos, "Spider", 340f, 90f),
            P(Zona.Lejos, "Centipede", -300f, 150f), P(Zona.Lejos, "Centipede", 300f, 200f), P(Zona.Lejos, "Centipede", 330f, -150f), P(Zona.Lejos, "Centipede", -200f, 300f),
            P(Zona.Lejos, "Ants", -250f, 250f), P(Zona.Lejos, "Ants", 300f, -260f),
            P(Zona.Lejos, "Beetle", -300f, -200f), P(Zona.Lejos, "Beetle", 100f, -330f),
            P(Zona.Lejos, "Bee", -80f, -330f), P(Zona.Lejos, "Caterpillar", 230f, 300f), P(Zona.Lejos, "Caterpillar", -330f, 20f),
            P(Zona.Lejos, "Worm", -60f, 300f), P(Zona.Lejos, "Worm", -280f, -280f), P(Zona.Lejos, "Worm", 290f, 250f),
            P(Zona.Lejos, "Butterfly", -350f, 100f), P(Zona.Lejos, "Butterfly", 180f, -320f), P(Zona.Lejos, "Butterfly", -120f, 330f), P(Zona.Lejos, "Butterfly", 20f, 320f),
        };
    }

    static string Tipo(GameObject go)
    {
        var ai = go.GetComponent<CreatureAI>(); if (ai != null) return ai.kind.ToString();
        if (go.GetComponent<SpiderAI>() != null) return "Spider";
        if (go.GetComponent<RatAI>() != null) return "Rat";
        return null;
    }

    [MenuItem("Herramientas/Animales/Colocar por progresion")]
    public static void Colocar()
    {
        var log = new System.Text.StringBuilder();
        var escena = EditorSceneManager.GetActiveScene();

        // bichos de la superficie (no los de las mazmorras ni las plantillas)
        var pools = new Dictionary<string, List<GameObject>>();
        var offsets = new Dictionary<GameObject, float>();
        foreach (var rh in Object.FindObjectsByType<RatHealth>(FindObjectsInactive.Include))
        {
            var go = rh.gameObject;
            if (go.scene != escena) continue;
            if (go.GetComponentInParent<Hormiguero>(true) != null || go.GetComponentInParent<Colmena>(true) != null || go.GetComponentInParent<Cloaca>(true) != null) continue;
            if (go.name.StartsWith("Plantilla")) continue;
            string t = Tipo(go);
            if (t == null) continue;
            if (!pools.TryGetValue(t, out var l)) { l = new List<GameObject>(); pools[t] = l; }
            l.Add(go);
            // altura sobre su suelo (las abejas vuelan desde el suelo; la rata y la araña tienen su propio alto)
            Vector3 p = go.transform.position;
            offsets[go] = p.y - ZonaJuego.Suelo(p, ZonaJuego.EnEdificio(p));
        }
        foreach (var kv in pools) log.AppendLine(kv.Key + ": " + kv.Value.Count + " en la escena");

        var raiz = GameObject.Find("ANIMALES");
        if (raiz == null) raiz = new GameObject("ANIMALES");
        var padres = new Dictionary<Zona, Transform>();
        foreach (Zona z in System.Enum.GetValues(typeof(Zona)))
        {
            var t = raiz.transform.Find(z.ToString());
            if (t == null) { t = new GameObject(z.ToString()).transform; t.SetParent(raiz.transform, false); }
            padres[z] = t;
        }

        var usados = new HashSet<GameObject>();
        var rnd = new System.Random(77);
        var contadores = new Dictionary<string, int>();
        foreach (var lugar in Tabla())
        {
            if (!pools.TryGetValue(lugar.tipo, out var pool) || pool.Count == 0) { log.AppendLine("no hay ningun " + lugar.tipo); continue; }
            GameObject go = pool.FirstOrDefault(g => g != null && !usados.Contains(g));
            float off;
            if (go == null)
            {   // faltan de este tipo: se duplica uno ya colocado
                var modelo = pool.First(g => g != null);
                go = Object.Instantiate(modelo);
                off = offsets[modelo];
                log.AppendLine("duplicado un " + lugar.tipo);
            }
            else off = offsets[go];
            usados.Add(go);

            string clave = lugar.zona + "_" + lugar.tipo;
            contadores.TryGetValue(clave, out int n); contadores[clave] = n + 1;
            Undo.RegisterFullObjectHierarchyUndo(go, "Colocar animales");
            go.transform.SetParent(padres[lugar.zona], true);
            go.name = clave + "_" + (n + 1);
            go.SetActive(true);

            bool enSalon = lugar.zona == Zona.Salon;
            Vector3 pos = new Vector3(lugar.x, 0f, lugar.z);
            pos = ZonaJuego.Limitar(pos, enSalon, 6f);
            pos.y = ZonaJuego.Suelo(pos, enSalon) + off;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));

            Ajustar(go, lugar);
        }

        // los que sobran se quitan (el salon ya no esta lleno de bichos)
        int quitados = 0;
        foreach (var kv in pools)
            foreach (var go in kv.Value)
                if (go != null && !usados.Contains(go)) { Undo.DestroyObjectImmediate(go); quitados++; }
        log.AppendLine("quitados: " + quitados);

        // el contenedor viejo del exterior queda vacio
        var viejo = GameObject.Find("EXTERIOR/Bichos");
        if (viejo != null && viejo.transform.childCount == 0) Undo.DestroyObjectImmediate(viejo);

        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        Debug.Log("[Animales] colocados " + usados.Count + " bichos por zonas de progresion\n" + log);
    }

    // valores normales (EnemiesBuilder) de los tipos que el salon y el patio suavizan: asi se puede volver a ejecutar
    static readonly Dictionary<string, (float danio, float vida)> Normal = new Dictionary<string, (float, float)>
    {
        { "Woodlouse", (15f, 100f) }, { "Beetle", (40f, 80f) }, { "Caterpillar", (50f, 120f) },
    };

    static void Ajustar(GameObject go, Lugar lugar)
    {
        Normal.TryGetValue(lugar.tipo, out var normal);
        var ai = go.GetComponent<CreatureAI>();
        if (ai != null)
        {
            ai.pasivo = lugar.pasivo;
            if (lugar.danio >= 0f) ai.attackDamage = lugar.danio;
            else if (normal.danio > 0f) ai.attackDamage = normal.danio;
            EditorUtility.SetDirty(ai);
        }
        var rh = go.GetComponent<RatHealth>();
        float vida = lugar.vida > 0f ? lugar.vida : normal.vida;
        if (rh != null && vida > 0f)
        {
            var so = new SerializedObject(rh);
            so.FindProperty("maxHealth").floatValue = vida;
            so.FindProperty("currentHealth").floatValue = vida;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        // carne segura en el salon (para aprender a cocinar en la fogata); solo ahi
        var dt = go.GetComponent<DropTable>();
        if (dt != null && lugar.tipo == "Woodlouse")
        {
            dt.entries.RemoveAll(e => e.itemId == "Carne");
            if (lugar.carne) dt.entries.Add(new DropEntry { itemId = "Carne", amount = 1, chance = 100f });
            EditorUtility.SetDirty(dt);
        }
    }
}
