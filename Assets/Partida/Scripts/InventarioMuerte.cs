using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Que pasa con el inventario al morir, segun la dificultad:
///  pacifico: nada, conservas todo.
///  normal:   todo queda en una mochila donde caiste (nunca desaparece; sale en el mapa y la brujula).
///  dificil:  se lo llevan los bichos mas cercanos: las hormigas al hormiguero, las abejas al panal,
///            y los demas lo cargan a la espalda hasta que los mates.
/// Las baterias verdes nunca se pierden (asi no hay que volver a pelear con las reinas).
/// </summary>
public static class InventarioMuerte
{
    public const string Permanente = MaquinaCrecimiento.ItemBateria;

    /// <summary>Saca del inventario todo lo que se puede perder y lo devuelve agrupado por objeto.</summary>
    public static List<SlotGuardado> Vaciar()
    {
        var suma = new Dictionary<string, SlotGuardado>();
        var inv = ControladorInventario.Instance;
        var listas = new List<InventorySlotUI>();
        if (inv != null)
        {
            inv.InitializeSlotsIfNeeded();
            listas.AddRange(inv.slots);
            if (inv.craftInputSlots != null) listas.AddRange(inv.craftInputSlots);
        }
        if (QuickAccessBar.Instance != null && QuickAccessBar.Instance.pageSlots != null) listas.AddRange(QuickAccessBar.Instance.pageSlots);
        foreach (var s in listas)
        {
            if (s == null || s.IsEmpty) continue;
            if (string.Equals(s.ItemId, Permanente, System.StringComparison.OrdinalIgnoreCase)) continue;
            SlotGuardado g;
            if (!suma.TryGetValue(s.ItemId, out g))
            {
                g = new SlotGuardado { id = s.ItemId, cantidad = 0, icono = s.CurrentIcon != null ? s.CurrentIcon.name : "" };
                suma[s.ItemId] = g;
            }
            g.cantidad += s.Quantity;
            s.Clear();
        }
        if (inv != null && inv.craftResultSlot != null) inv.craftResultSlot.Clear();
        return new List<SlotGuardado>(suma.Values);
    }

    /// <summary>Aplica la regla de la dificultad. Devuelve el texto que se muestra al despertar.</summary>
    public static string AlMorir(Vector3 dondeMurio)
    {
        switch (Partida.Dif)
        {
            case Dificultad.Pacifico:
                return "Conservas todo lo que llevabas.";
            case Dificultad.Normal:
            {
                var items = Vaciar();
                if (items.Count == 0) return "No llevabas nada encima.";
                Mochila.Crear(Suelo(dondeMurio), Mochila.Tipo.Mochila, "Tu mochila", items);
                return "Tu mochila quedo donde caiste. Esta marcada en el mapa y en la brujula.";
            }
            default:
                return Robar(dondeMurio);
        }
    }

    static string Robar(Vector3 donde)
    {
        var items = Vaciar();
        if (items.Count == 0) return "No llevabas nada encima.";

        // los 3 bichos vivos mas cercanos (sin mariposas, larvas ni bichos muy lejanos)
        var cand = new List<(RatHealth rh, float d)>();
        foreach (var rh in Object.FindObjectsByType<RatHealth>(FindObjectsInactive.Exclude))
        {
            if (rh.IsDead || rh.GetComponent<LarvaAbeja>() != null) continue;
            var ai = rh.GetComponent<CreatureAI>();
            if (ai != null && ai.kind == CreatureAI.Kind.Butterfly) continue;
            if (ai == null && rh.GetComponent<SpiderAI>() == null && rh.GetComponent<RatAI>() == null && rh.GetComponent<HormigaNido>() == null &&
                rh.GetComponent<AbejaColmena>() == null && rh.GetComponent<ReinaHormiga>() == null && rh.GetComponent<ReinaAbeja>() == null) continue;
            float d = Vector3.Distance(rh.transform.position, donde);
            if (d < 90f) cand.Add((rh, d));
        }
        cand.Sort((a, b) => a.d.CompareTo(b.d));
        if (cand.Count > 3) cand.RemoveRange(3, cand.Count - 3);
        if (cand.Count == 0)
        {
            Mochila.Crear(Suelo(donde), Mochila.Tipo.Mochila, "Tu mochila", items);
            return "No habia bichos cerca: tu mochila quedo donde caiste.";
        }

        // reparto por pilas entre los ladrones
        var partes = new List<List<SlotGuardado>>();
        for (int i = 0; i < cand.Count; i++) partes.Add(new List<SlotGuardado>());
        for (int i = 0; i < items.Count; i++) partes[i % cand.Count].Add(items[i]);

        var hormigas = new List<SlotGuardado>(); var abejas = new List<SlotGuardado>();
        var otros = new List<string>();
        for (int i = 0; i < cand.Count; i++)
        {
            if (partes[i].Count == 0) continue;
            var rh = cand[i].rh;
            if (EsHormiga(rh)) hormigas.AddRange(partes[i]);
            else if (EsAbeja(rh)) abejas.AddRange(partes[i]);
            else { Ladron.Asignar(rh, partes[i]); otros.Add(rh.EnemyName); }
        }
        var sb = new StringBuilder("Los bichos se llevaron tus cosas.");
        if (hormigas.Count > 0) { Mochila.Crear(Nido(true), Mochila.Tipo.BotinHormigas, "Botin de las hormigas", hormigas); sb.Append("\nLas hormigas las llevaron al hormiguero."); }
        if (abejas.Count > 0) { Mochila.Crear(Nido(false), Mochila.Tipo.BotinAbejas, "Botin de las abejas", abejas); sb.Append("\nLas abejas las llevaron a su panal en el arbol."); }
        if (otros.Count > 0) sb.Append("\n" + string.Join(", ", otros) + (otros.Count > 1 ? " cargan" : " carga") + " el resto: atrapalos.");
        return sb.ToString();
    }

    static bool EsHormiga(RatHealth rh)
    {
        if (rh.GetComponent<HormigaNido>() != null || rh.GetComponent<ReinaHormiga>() != null) return true;
        var ai = rh.GetComponent<CreatureAI>();
        return ai != null && ai.kind == CreatureAI.Kind.Ants;
    }

    static bool EsAbeja(RatHealth rh)
    {
        if (rh.GetComponent<AbejaColmena>() != null || rh.GetComponent<ReinaAbeja>() != null) return true;
        var ai = rh.GetComponent<CreatureAI>();
        return ai != null && ai.kind == CreatureAI.Kind.Bee;
    }

    /// <summary>Donde guardan el botin: la camara de larvas mas alta del hormiguero o el primer piso de cera de la colmena.</summary>
    public static Vector3 Nido(bool hormiguero)
    {
        if (hormiguero)
        {
            // en el primer tramo del tunel, ya bajo tierra (la entrada baja hacia el norte)
            var e = ZonaJuego.hormigueroEntrada;
            for (float dz = 25f; dz <= 80f; dz += 5f)
                foreach (var hit in Physics.RaycastAll(new Vector3(e.x, 20f, e.y + dz), Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.normal.y > 0.5f && hit.point.y < -10f && hit.point.y > -45f && hit.collider.GetComponentInParent<RatHealth>() == null)
                        return hit.point;
        }
        string raiz = hormiguero ? "HORMIGUERO" : "ARBOL_COLMENA";
        var r = GameObject.Find(raiz);
        Vector3 mejor = hormiguero ? new Vector3(ZonaJuego.hormigueroEntrada.x, 0f, ZonaJuego.hormigueroEntrada.y) : new Vector3(ZonaJuego.arbolCentro.x, 0f, ZonaJuego.arbolCentro.y);
        if (r != null)
        {
            float extremo = hormiguero ? float.MinValue : float.MaxValue;
            // cada larva por separado: hormiguero = la mas cercana a la superficie; colmena = la mas baja (primer piso de cera)
            foreach (Transform t in r.transform)
            {
                if (t.name != "Larvas") continue;
                foreach (Transform larva in t)
                {
                    var rl = larva.GetComponentInChildren<Renderer>();
                    if (rl == null) continue;
                    var c = rl.bounds.center;
                    if (hormiguero ? c.y > extremo : c.y < extremo) { extremo = c.y; mejor = c; }
                }
            }
        }
        return Suelo(mejor + Vector3.up * 2f, mejor.y);
    }

    static Vector3 Suelo(Vector3 p) => Suelo(p, p.y);

    static Vector3 Suelo(Vector3 p, float yRef)
    {
        Vector3 mejor = p; float dMin = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < 0.5f || hit.collider.GetComponentInParent<PlayerController>() != null || hit.collider.GetComponentInParent<RatHealth>() != null) continue;
            float d = Mathf.Abs(hit.point.y - yRef);
            if (d < dMin) { dMin = d; mejor = hit.point; }
        }
        return mejor;
    }
}
