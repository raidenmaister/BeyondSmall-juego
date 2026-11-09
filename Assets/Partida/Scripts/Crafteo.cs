using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recetas de la pagina "CREAR ARMA" del inventario: se arrastran dos objetos a las casillas de entrada,
/// el resultado aparece en la casilla de abajo y con un clic se crea (gasta una unidad de cada entrada).
/// </summary>
public static class Crafteo
{
    public struct Receta { public string a, b, resultado; public int cantidad; }

    public static readonly List<Receta> Recetas = new List<Receta>
    {
        new Receta { a = "Carbon", b = "Palo", resultado = "Antorcha", cantidad = 1 },
        new Receta { a = "Exoesqueleto", b = "Ligas", resultado = "Escudo", cantidad = 1 },   // caparazon de escarabajo atado con ligas
        new Receta { a = "Borrador", b = "Ligas", resultado = "Escudo", cantidad = 1 },
    };

    static string mostrado;

    /// <summary>Que receta forman las casillas de entrada (null si ninguna).</summary>
    public static Receta? Buscar(ControladorInventario inv)
    {
        if (inv == null || inv.craftInputSlots == null || inv.craftInputSlots.Count < 2) return null;
        var s0 = inv.craftInputSlots[0]; var s1 = inv.craftInputSlots[1];
        if (s0 == null || s1 == null || s0.IsEmpty || s1.IsEmpty) return null;
        foreach (var r in Recetas)
        {
            if ((Igual(s0.ItemId, r.a) && Igual(s1.ItemId, r.b)) || (Igual(s0.ItemId, r.b) && Igual(s1.ItemId, r.a))) return r;
        }
        return null;
    }

    static bool Igual(string x, string y) => string.Equals(x, y, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Muestra (o quita) el resultado en la casilla de salida. Se llama cada cuadro con el inventario abierto.</summary>
    public static void Actualizar(ControladorInventario inv)
    {
        if (inv == null || inv.craftResultSlot == null) return;
        var r = Buscar(inv);
        string quiero = r.HasValue ? r.Value.resultado : null;
        if (quiero == mostrado && (quiero == null) == inv.craftResultSlot.IsEmpty) return;
        mostrado = quiero;
        if (quiero == null) inv.craftResultSlot.Clear();
        else inv.craftResultSlot.SetItem(quiero, inv.GetIconByName(quiero), r.Value.cantidad);
    }

    /// <summary>Clic en la casilla de resultado: crea el objeto y gasta los ingredientes.</summary>
    public static bool Craftear(ControladorInventario inv)
    {
        var r = Buscar(inv);
        if (!r.HasValue) return false;
        var rec = r.Value;
        if (!inv.AddItem(rec.resultado, inv.GetIconByName(rec.resultado), rec.cantidad))
        {
            SurvivalUI.Aviso("No hay espacio en la mochila", new Color(1f, 0.6f, 0.4f));
            return false;
        }
        foreach (var s in inv.craftInputSlots) if (s != null && !s.IsEmpty) s.SetQuantity(s.Quantity - 1);
        mostrado = null;
        Actualizar(inv);
        Estadisticas.Creado(rec.resultado);
        SurvivalUI.Aviso("¡Creaste: " + rec.resultado + "!", new Color(1f, 0.75f, 0.35f));
        return true;
    }
}
