using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Todo lo que se puede tener en la mochila, con su nombre bonito, para que sirve y donde se consigue.
/// Regla del juego (la pistola de Chejov): si un objeto esta en el mundo, TIENE un uso. Cada material
/// aparece en al menos una receta, y el libro de recetas te dice donde buscar lo que te falta
/// (Alva Majo: "el jugador no es adivino").
/// </summary>
public static class Catalogo
{
    public enum Tipo { Material, Arma, Mejora, Comida, Curacion, Herramienta, Clave }

    public class Item
    {
        public string id, nombre, descripcion, donde;
        public Tipo tipo;
    }

    static readonly Dictionary<string, Item> items = new Dictionary<string, Item>(System.StringComparer.OrdinalIgnoreCase);

    static void I(string id, string nombre, Tipo tipo, string descripcion, string donde)
    {
        items[id] = new Item { id = id, nombre = nombre, tipo = tipo, descripcion = descripcion, donde = donde };
    }

    static Catalogo()
    {
        // ---- materiales del salon
        I("Palo", "Palo", Tipo.Material, "Un palito de madera. Mango para antorchas, hachas y martillos.", "Tirados por el salon y el jardin");
        I("Carbon", "Carbon", Tipo.Material, "Arde mucho tiempo. Con un palo: antorcha.", "Junto a la fogata y por el salon");
        I("Ligas", "Liga", Tipo.Material, "Elastica y fuerte: ata escudos y vendas.", "Por el suelo del salon");
        I("Borrador", "Borrador", Tipo.Material, "Goma blanda: un escudo o la cabeza de un mazo.", "Por el suelo del salon");
        I("Clips", "Clip", Tipo.Material, "Alambre doblado. Enderezado es un latigo.", "Por el suelo del salon");
        I("Chincheta", "Chincheta", Tipo.Material, "Punta afilada. Clavada en un borrador: mazo de pinchos.", "Por el suelo del salon");
        I("Chincheta_Doblada", "Chincheta doblada", Tipo.Material, "Un garfio perfecto para la punta de un latigo.", "Por el suelo del salon");
        I("broche", "Broche", Tipo.Material, "Una pinza de papel que muerde muy fuerte.", "Por el suelo del salon");
        I("Tornillo", "Tornillo", Tipo.Material, "Rosca de metal: el eje de unas tenazas.", "Por el suelo del salon");
        I("Tuerca", "Tuerca", Tipo.Material, "Pesada como un yunque. La cabeza de un martillo.", "Por el suelo del salon");
        I("Tela", "Tela", Tipo.Material, "Un retal limpio. Con una liga: venda.", "Por el suelo del salon");
        I("Lapiz", "Pedazo de lapiz", Tipo.Material, "Medio lapiz amarillo: un mango largo y recto.", "Por el suelo del salon");
        I("Navaja", "Navaja de sacapuntas", Tipo.Material, "La cuchilla de un sacapuntas roto. Corta de verdad.", "Por el suelo del salon");
        // ---- lo que sueltan los bichos
        I("Carne", "Carne cruda", Tipo.Comida, "Quita el hambre. Mejor cocinada en la fogata.", "Orugas, lombrices y cochinillas");
        I("Carne_Cocinada", "Carne cocinada", Tipo.Comida, "Llena mucho y cura bastante.", "Cocina carne en la fogata (tecla C)");
        I("Hueso", "Hueso", Tipo.Material, "Ligero y rigido: el armazon de un planeador.", "Ratas (y el Rey Rata)");
        I("Piel", "Piel", Tipo.Material, "Cuero fino para guantes y botas.", "Ratas (y el Rey Rata)");
        I("Colmillo", "Colmillo", Tipo.Material, "Afilado. En unos guantes, golpes mas fuertes.", "Hormigas y arañas");
        I("Aguijon", "Aguijon", Tipo.Material, "Lleva veneno. En unos guantes o en la punta de una lanza.", "Abejas");
        I("Alas", "Alas", Tipo.Material, "Membrana que planea. Con un hueso: planeador.", "Mariposas (y las reinas)");
        I("PataCiempies", "Pata de ciempies", Tipo.Material, "Muelles naturales: botas para saltar mas.", "Ciempies");
        I("Exoesqueleto", "Exoesqueleto", Tipo.Material, "Caparazon duro: escudos y petos.", "Escarabajos, cochinillas y la Reina Hormiga");
        I("Cuerno", "Cuerno", Tipo.Material, "Duro como piedra: peto o un martillo brutal.", "Escarabajo rinoceronte");
        // ---- objetos clave y herramientas
        I("Bateria", "Bateria verde", Tipo.Clave, "Energia para la cabina. Hacen falta 4.", "La guardan los cuatro jefes");
        I("Antorcha", "Antorcha", Tipo.Herramienta, "Alumbra y su humo calma a las abejas (tecla T).", "Crea: carbon + palo");
        I("Escudo", "Escudo", Tipo.Herramienta, "Clic derecho para bloquear. Bloqueo perfecto: sin daño.", "Crea: exoesqueleto o borrador + liga");
        I("Venda", "Venda", Tipo.Curacion, "Cura 70 de vida y quita el veneno (tecla H).", "Crea: tela + liga");
        // ---- mejoras
        I("Guantes", "Guantes de colmillo", Tipo.Mejora, "Tus golpes hacen un 50% mas de daño.", "Crea: colmillo o aguijon + piel");
        I("Botas", "Botas de ciempies", Tipo.Mejora, "Saltas un 35% mas alto.", "Crea: pata de ciempies + piel");
        I("Planeador", "Planeador", Tipo.Mejora, "Manten SALTAR en el aire para planear.", "Crea: alas + hueso");
        I("Peto", "Peto de cuerno", Tipo.Mejora, "Tu vida maxima sube un 50%.", "Crea: cuerno + exoesqueleto");
        // ---- armas (ver Armas.cs)
        foreach (var a in Armas.Todas)
            I(a.id, a.nombre, Tipo.Arma, a.descripcion, "Crea: " + a.receta);
    }

    public static Item Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Item it;
        if (items.TryGetValue(id, out it)) return it;
        if (items.TryGetValue(ControladorInventario.NormalizeItemName(id), out it)) return it;
        return null;
    }

    public static string Nombre(string id) { var it = Get(id); return it != null ? it.nombre : id; }
    public static IEnumerable<Item> Todos => items.Values;

    // ------------------------------------------------------------------ recetas
    /// <summary>Todas las fusiones del juego (se añaden a Crafteo.Recetas una sola vez).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegistrarRecetas()
    {
        Añadir("Tela", "Ligas", "Venda", 1);
        foreach (var a in Armas.Todas) Añadir(a.ingredienteA, a.ingredienteB, a.id, 1);
    }

    static void Añadir(string a, string b, string r, int n)
    {
        foreach (var x in Crafteo.Recetas) if (x.a == a && x.b == b && x.resultado == r) return;
        Crafteo.Recetas.Add(new Crafteo.Receta { a = a, b = b, resultado = r, cantidad = n });
    }
}
