using UnityEngine;

/// <summary>
/// Puntos de reaparicion (Alva Majo sobre las vidas: los juegos pasaron de "repite todo" a checkpoints
/// para que solo repitas el ultimo trozo). Antes al morir siempre despertabas en la cabina, lejos de todo,
/// y habia que volver a cruzar el mundo. Ahora despiertas junto a la entrada de la zona donde caiste
/// (hormiguero, arbol de la colmena, alcantarilla o la fogata) si ya la habias descubierto.
/// Hamsterlandia no: ahi dejarse atrapar por Bolita es una salida y se despierta en la cabina.
/// </summary>
public static class Refugios
{
    public static bool Elegir(Vector3 muerte, out Vector3 pos, out Quaternion rot, out string nombre)
    {
        pos = Vector3.zero; rot = Quaternion.identity; nombre = null;
        var mapa = MapaMundo.Instancia;
        var maq = MaquinaCrecimiento.Instancia;
        if (mapa == null || maq == null) return false;
        Vector3 cabina = maq.transform.position;

        // 1) zona cerrada en la que estaba el jugador
        MapaMundo.TipoLugar? zona = null;
        if (Hormiguero.Instancia != null && Hormiguero.Instancia.JugadorDentro) zona = MapaMundo.TipoLugar.Hormiguero;
        else if (Cloaca.Instancia != null && Cloaca.Instancia.JugadorDentro) zona = MapaMundo.TipoLugar.Cloaca;
        else if (Colmena.Instancia != null && Colmena.Instancia.JugadorDentro) zona = MapaMundo.TipoLugar.Arbol;
        var hl = Hamsterlandia.Instancia;
        if (hl != null && hl.zona.Contains(muerte)) return false;

        MapaMundo.Lugar mejor = null;
        float dMejor = Vector2.Distance(muerte.ToXZ(), cabina.ToXZ());
        foreach (var l in mapa.lugares)
        {
            if (!l.descubierto) continue;
            bool valido = l.tipo == MapaMundo.TipoLugar.Hormiguero || l.tipo == MapaMundo.TipoLugar.Cloaca
                          || l.tipo == MapaMundo.TipoLugar.Arbol || l.tipo == MapaMundo.TipoLugar.Fogata;
            if (!valido) continue;
            if (zona.HasValue)
            {
                if (l.tipo == zona.Value) { mejor = l; break; }
                continue;
            }
            float d = Vector2.Distance(muerte.ToXZ(), l.pos.ToXZ());
            if (d < dMejor) { dMejor = d; mejor = l; }
        }
        if (mejor == null) return false;

        // un poco hacia la cabina desde la entrada, sobre suelo firme
        Vector3 hacia = cabina - mejor.pos; hacia.y = 0f;
        Vector3 dir = hacia.sqrMagnitude > 1f ? hacia.normalized : Vector3.forward;
        float separacion = mejor.tipo == MapaMundo.TipoLugar.Arbol ? 26f : mejor.tipo == MapaMundo.TipoLugar.Fogata ? 5f : 10f;
        for (float extra = 0f; extra <= 24f; extra += 6f)
        {
            Vector3 p = mejor.pos + dir * (separacion + extra);
            // el suelo mas bajo cerca de la altura de la entrada: ni ramas ni copas del arbol arriba,
            // ni los tuneles del hormiguero o la cloaca abajo
            var hits = Physics.RaycastAll(new Vector3(p.x, mejor.pos.y + 60f, p.z), Vector3.down, 140f, ~0, QueryTriggerInteraction.Ignore);
            bool hay = false; RaycastHit hit = default;
            foreach (var hh in hits)
            {
                if (hh.normal.y < 0.7f || hh.point.y < mejor.pos.y - 8f) continue;
                if (!hay || hh.point.y < hit.point.y) { hit = hh; hay = true; }
            }
            if (hay)
            {
                // que no haya nada encima (dentro de una raiz, debajo de una hoja...)
                if (Physics.CheckSphere(hit.point + Vector3.up * 1.1f, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
                pos = hit.point + Vector3.up * 1.05f;
                rot = Quaternion.LookRotation(-dir, Vector3.up);   // mirando hacia la entrada
                nombre = mejor.nombre;
                return true;
            }
        }
        return false;
    }
}
