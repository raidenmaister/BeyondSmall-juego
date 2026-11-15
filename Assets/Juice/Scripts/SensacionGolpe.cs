using UnityEngine;

/// <summary>
/// Lo que se siente al pegar (Guinxu: "la camara tiembla para que se note la fuerza de nuestro ataque";
/// Alva Majo: "si le haces daño tiene que estar claro, y si no le haces daño tambien").
///   - golpe normal: congelado de unos frames, chispas, temblor flojo y el bicho se aplasta
///   - golpe cargado: todo mas fuerte
///   - golpe que no hace nada (armadura, enterrado...): chispas grises, sonido metalico y aviso
///   - golpe que mata: congelado mas largo y temblor
/// </summary>
public static class SensacionGolpe
{
    static float ultimoAvisoInmune = -99f;

    public static void AlGolpear(PlayerController pc, RatHealth objetivo, float danioHecho, bool cargado)
    {
        if (objetivo == null) return;
        Vector3 punto = PuntoDeImpacto(pc, objetivo);

        if (danioHecho <= 0f)
        {
            // no le ha hecho nada: que se vea y se oiga claramente
            Juice.Chispas(punto, new Color(0.75f, 0.78f, 0.82f), 10, 3f);
            SonidosJuice.Tocar(SonidosJuice.Rebote, punto, 0.7f);
            Juice.Congelar(0.03f);
            if (Time.time - ultimoAvisoInmune > 4f)
            {
                ultimoAvisoInmune = Time.time;
                SurvivalUI.Aviso("¡No le hace daño ahora! Busca su punto debil", new Color(0.8f, 0.85f, 0.95f));
            }
            return;
        }

        bool mata = objetivo.IsDead;
        bool jefe = objetivo == RatHealthBarUI.jefe;
        Color chispa = mata ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.95f, 0.75f);
        Juice.Chispas(punto, chispa, cargado ? 22 : 12, cargado ? 6f : 4f);
        Juice.Congelar(mata ? 0.12f : (cargado ? 0.09f : 0.045f));
        Juice.Sacudir(mata ? (jefe ? 0.75f : 0.32f) : (cargado ? 0.3f : 0.13f));
        if (!jefe) Juice.Aplastar(objetivo.transform, cargado ? 0.28f : 0.16f);
        if (cargado) SonidosJuice.Tocar(SonidosJuice.GolpeFuerte, punto, 0.8f);
        if (mata && jefe) Juice.CamaraLenta(0.25f, 1.6f);

        NumerosDanio.Mostrar(punto + Vector3.up * 0.4f, danioHecho, cargado);
    }

    static Vector3 PuntoDeImpacto(PlayerController pc, RatHealth objetivo)
    {
        Vector3 centro = objetivo.transform.position + Vector3.up * 0.5f;
        var col = objetivo.GetComponentInChildren<Collider>();
        if (col != null && col.enabled)
        {
            Vector3 mano = pc.transform.position + Vector3.up * 1.1f + pc.transform.forward * 0.6f;
            centro = col.ClosestPoint(mano);
        }
        return centro;
    }
}
