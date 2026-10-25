using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adapta la UI del inventario a la nueva imagen horizontal (Cuaderno.png con la sección "CREAR ARMA"),
/// crea los slots de crafteo y registra el icono de la chincheta.
/// </summary>
public static class InventarioSetup
{
    const string PinIconPath = "Assets/iconos/Chincheta.png";
    const string LogPath = "Temp/inventario_setup.txt";

    // Posiciones (en píxeles de la imagen Cuaderno.png nueva, 1532x1080) de los centros de los 3 slots
    const float ImgW = 1532f, ImgH = 1080f;
    static readonly Vector2[] SlotCentersPx = { new Vector2(1261, 330), new Vector2(1261, 546), new Vector2(1261, 762) };

    [MenuItem("Tools/Inventario/Configurar Crafteo Horizontal")]
    public static void Run()
    {
        var log = new StringBuilder();
        try { RunInternal(log); }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        File.WriteAllText(LogPath, log.ToString());
        Debug.Log("[InventarioSetup] " + log);
    }

    static void RunInternal(StringBuilder log)
    {
        AssetDatabase.Refresh();

        // 1) Icono de la chincheta como Sprite
        var ti = AssetImporter.GetAtPath(PinIconPath) as TextureImporter;
        if (ti != null && ti.textureType != TextureImporterType.Sprite)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
        var pinSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PinIconPath);
        log.AppendLine("Sprite chincheta: " + (pinSprite != null ? "ok" : "NO ENCONTRADO"));

        // 2) Localizar el inventario en la escena
        var bg = Resources.FindObjectsOfTypeAll<RectTransform>()
            .FirstOrDefault(t => t.gameObject.scene.IsValid() && t.name == "InventoryBackground");
        if (bg == null) { log.AppendLine("No se encontro InventoryBackground"); return; }

        var page = bg.Find("LeftPage_Inventory") as RectTransform;
        var right = bg.Find("MiddlePage_Equipment") as RectTransform;
        var grid = page != null ? page.Find("GridContainer") as RectTransform : null;
        if (page == null || right == null || grid == null) { log.AppendLine("Faltan paginas/grid"); return; }

        var pageImg = page.GetComponent<Image>();
        var sprite = pageImg.sprite;
        log.AppendLine($"Sprite pagina: {sprite.name} {sprite.rect.width}x{sprite.rect.height}");
        if (Mathf.Abs(sprite.rect.width - ImgW) > 1f)
        {
            log.AppendLine("La imagen no es la horizontal esperada (1532 de ancho). Abortado.");
            return;
        }

        Undo.RecordObject(bg, "Inventario horizontal");

        // 3) Guardar la posicion del grid respecto al borde izquierdo de la pagina
        float oldW = page.rect.width;
        Vector3[] gc = new Vector3[4];
        grid.GetWorldCorners(gc);
        Vector2 gMin = page.InverseTransformPoint(gc[0]);
        Vector2 gMax = page.InverseTransformPoint(gc[2]);
        float offsetFromLeft = gMin.x - (-oldW / 2f);
        Vector2 gSize = gMax - gMin;
        float gCenterY = (gMin.y + gMax.y) / 2f;

        // 4) Nuevo tamano de la pagina (misma altura, proporcion de la imagen)
        float h = page.rect.height;
        float newW = h * ImgW / ImgH;
        float k = h / ImgH;                                    // unidades UI por pixel de imagen
        page.sizeDelta = new Vector2(newW, h);
        log.AppendLine($"Pagina: {oldW:F1} -> {newW:F1} x {h:F1}");

        // 5) Grid: conserva su distancia al borde izquierdo
        grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 0.5f);
        grid.pivot = new Vector2(0.5f, 0.5f);
        grid.sizeDelta = gSize;
        float newMinX = -newW / 2f + offsetFromLeft;
        grid.anchoredPosition = new Vector2(newMinX + gSize.x / 2f, gCenterY);

        // 6) Colocar las dos paginas (horizontal) y ajustar escala para que quepan
        float overlap = 25f;
        float rightW = right.rect.width;
        float total = newW + rightW - overlap;
        page.anchoredPosition = new Vector2(-total / 2f + newW / 2f, page.anchoredPosition.y);
        right.anchoredPosition = new Vector2(total / 2f - rightW / 2f, right.anchoredPosition.y);
        bg.localScale = new Vector3(1.95f, 1.95f, 1f);
        log.AppendLine($"Total pagina doble: {total:F1}, escala 1.95");

        // 7) Slots de crafteo (clones de un slot normal)
        var template = grid.GetComponentsInChildren<InventorySlotUI>(true).FirstOrDefault();
        if (template == null) { log.AppendLine("No hay slot plantilla"); return; }

        foreach (var old in page.GetComponentsInChildren<InventorySlotUI>(true).Where(s => s.name.StartsWith("Craft")).ToList())
            Object.DestroyImmediate(old.gameObject);

        string[] names = { "CraftSlot_1", "CraftSlot_2", "CraftResult" };
        var created = new InventorySlotUI[3];
        for (int i = 0; i < 3; i++)
        {
            var go = Object.Instantiate(template.gameObject, page);
            go.name = names[i];
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(67f, 67f);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = new Vector2((SlotCentersPx[i].x - ImgW / 2f) * k, (ImgH / 2f - SlotCentersPx[i].y) * k);

            var img = go.GetComponent<Image>();
            if (img != null) { img.color = new Color(1f, 1f, 1f, 0f); img.raycastTarget = true; }

            var slot = go.GetComponent<InventorySlotUI>();
            slot.SetKind(i < 2 ? InventorySlotUI.SlotKind.CraftInput : InventorySlotUI.SlotKind.CraftResult);
            slot.Clear();
            created[i] = slot;
        }

        // 8) Registrar en ControladorInventario
        var ctrl = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (ctrl != null)
        {
            var so = new SerializedObject(ctrl);
            var inputs = so.FindProperty("craftInputSlots");
            inputs.arraySize = 2;
            inputs.GetArrayElementAtIndex(0).objectReferenceValue = created[0];
            inputs.GetArrayElementAtIndex(1).objectReferenceValue = created[1];
            so.FindProperty("craftResultSlot").objectReferenceValue = created[2];

            var db = so.FindProperty("iconDatabase");
            bool has = false;
            for (int i = 0; i < db.arraySize; i++)
                if (db.GetArrayElementAtIndex(i).objectReferenceValue != null && db.GetArrayElementAtIndex(i).objectReferenceValue.name == "Chincheta") has = true;
            if (!has && pinSprite != null)
            {
                db.arraySize++;
                db.GetArrayElementAtIndex(db.arraySize - 1).objectReferenceValue = pinSprite;
                log.AppendLine("Chincheta agregada a iconDatabase");
            }
            so.ApplyModifiedProperties();
            log.AppendLine("ControladorInventario configurado. Iconos en BD: " + db.arraySize);
        }
        else log.AppendLine("No se encontro ControladorInventario");

        EditorSceneManager.MarkSceneDirty(page.gameObject.scene);
        EditorSceneManager.SaveScene(page.gameObject.scene);
        log.AppendLine("Escena guardada.");
    }

    // ---------- Verificacion: nombre del objeto del suelo -> id -> icono ----------
    [MenuItem("Tools/Inventario/Verificar iconos")]
    public static void VerifyIcons()
    {
        var sb = new StringBuilder();
        var ctrl = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        sb.AppendLine("Iconos en BD: " + string.Join(", ", ctrl.iconDatabase.Where(s => s != null).Select(s => s.name)));
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            string n = r.name;
            if (!(n.StartsWith("clip_") || n.StartsWith("Eraser_") || n.StartsWith("black_generic_hair_tie_") || n.StartsWith("plaggy_cc0-pin-569_"))) continue;
            string id = ControladorInventario.NormalizeItemName(n);
            var icon = ctrl.GetIconByName(id);
            string prefix = n.Substring(0, n.LastIndexOf('_'));
            if (seen.Add(prefix))
                sb.AppendLine($"{n}  ->  id='{id}'  icono='{(icon != null ? icon.name : "NINGUNO")}'");
        }
        File.WriteAllText("Temp/inventario_iconos.txt", sb.ToString());
        Debug.Log("[InventarioSetup] " + sb);
    }

    // ---------- Pruebas en Play ----------
    [MenuItem("Tools/Inventario/Dar items de prueba")]
    public static void GiveTestItems()
    {
        if (!Application.isPlaying || ControladorInventario.Instance == null) { Debug.LogWarning("Solo en Play"); return; }
        var c = ControladorInventario.Instance;
        c.AddItem("Clips", null, 2);
        c.AddItem("Borrador", null, 1);
        c.AddItem("Ligas", null, 3);
        c.AddItem("Chincheta", null, 2);
        c.AddItem("broche", null, 1);
        Debug.Log("[InventarioSetup] items de prueba entregados");
    }

    [MenuItem("Tools/Inventario/Abrir o cerrar inventario")]
    public static void ToggleInv()
    {
        if (Application.isPlaying && ControladorInventario.Instance != null) ControladorInventario.Instance.ToggleInventario();
    }
}
