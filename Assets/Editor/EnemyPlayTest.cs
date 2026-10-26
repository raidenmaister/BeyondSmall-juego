using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prueba automatica en modo juego: lleva al jugador junto a cada enemigo, lo deja pelear unos segundos,
/// lo mata y comprueba los objetos soltados. Resultado en Temp/pt_log.txt (Temp/pt_done.txt al terminar).
/// </summary>
[InitializeOnLoad]
public static class EnemyPlayTest
{
    const string LogPath = "Temp/pt_log.txt";
    static double next;

    static EnemyPlayTest()
    {
        EditorApplication.update += Update;
        Application.logMessageReceived += OnLog;
    }

    static void Append(string s) { File.AppendAllText(LogPath, s + "\n"); }

    static void OnLog(string msg, string stack, LogType t)
    {
        if (SessionState.GetInt("pt_stage", 0) == 0) return;
        if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert)
            Append("[" + t + "] " + msg + (t == LogType.Exception ? "\n" + stack.Split('\n').FirstOrDefault() : ""));
        else if (msg.StartsWith("[DropTable]")) Append(msg);
    }

    public static void Begin()
    {
        File.WriteAllText(LogPath, "");
        if (File.Exists("Temp/pt_done.txt")) File.Delete("Temp/pt_done.txt");
        SessionState.SetInt("pt_stage", 1);
        SessionState.SetInt("pt_i", 0);
        SessionState.SetInt("pt_sub", 0);
        SessionState.SetFloat("pt_t0", 0f);
        EditorApplication.isPlaying = true;
    }

    static void Finish(string why)
    {
        Append("FIN " + why);
        File.WriteAllText("Temp/pt_done.txt", why);
        SessionState.SetInt("pt_stage", 0);
        EditorApplication.isPlaying = false;
    }

    static void Update()
    {
        int stage = SessionState.GetInt("pt_stage", 0);
        if (stage == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.25;
        double now = EditorApplication.timeSinceStartup;

        if (stage == 1)
        {
            if (SessionState.GetFloat("pt_t0", 0f) == 0f) { SessionState.SetFloat("pt_t0", (float)now); return; }
            if (now - SessionState.GetFloat("pt_t0", 0f) < 2.5) return;
            var names = Object.FindObjectsByType<RatHealth>().Select(h => h.gameObject.name).OrderBy(n => n).ToArray();
            SessionState.SetString("pt_names", string.Join("|", names));
            Append("criaturas: " + names.Length + " -> " + string.Join(", ", names));
            SessionState.SetInt("pt_stage", 2);
            return;
        }

        if (stage == 2)
        {
            var names = SessionState.GetString("pt_names", "").Split('|');
            int i = SessionState.GetInt("pt_i", 0);
            if (i >= names.Length) { SessionState.SetInt("pt_stage", 3); return; }
            int sub = SessionState.GetInt("pt_sub", 0);
            var go = GameObject.Find(names[i]);
            var pc = Object.FindAnyObjectByType<PlayerController>();
            var ph = Object.FindAnyObjectByType<PlayerHealth>();
            if ((go == null && sub != 2) || pc == null) { Append(names[i] + ": ya no existe"); SessionState.SetInt("pt_i", i + 1); SessionState.SetInt("pt_sub", 0); return; }

            if (sub == 0)
            {   // llevar al jugador cerca
                if (ph != null) ph.SetHealth(9999f, 9999f);
                var cc = pc.GetComponent<CharacterController>();
                Vector3 tp = go.transform.position + new Vector3(0f, 0.1f, -6f);
                tp.z = Mathf.Max(tp.z, -88f);
                if (cc != null) cc.enabled = false;
                pc.transform.position = tp;
                if (cc != null) cc.enabled = true;
                SessionState.SetFloat("pt_t0", (float)now); SessionState.SetFloat("pt_hp0", ph != null ? ph.CurrentHealth : 0f);
                SessionState.SetInt("pt_sub", 1);
                return;
            }
            if (sub == 1)
            {   // dejar que pelee 4.5 s
                if (now - SessionState.GetFloat("pt_t0", 0f) < 4.5) return;
                if (names[i].EndsWith("_1") || names[i].StartsWith("Rata") || names[i] == "Spider_Enemy")
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/pt_" + names[i] + ".png"));
                float lost = SessionState.GetFloat("pt_hp0", 0f) - (ph != null ? ph.CurrentHealth : 0f);
                var h = go.GetComponent<RatHealth>();
                Append($"{names[i]}: jugador perdio {lost:F0} PV; vida criatura {(h != null ? h.CurrentHealth : -1):F0}/{(h != null ? h.MaxHealth : -1):F0}; distancia {Vector3.Distance(go.transform.position, pc.transform.position):F1}");
                SessionState.SetString("pt_pos", go.transform.position.x.ToString("F2") + "," + go.transform.position.z.ToString("F2"));
                if (h != null) h.TakeDamage(99999f);   // matarla
                SessionState.SetFloat("pt_t0", (float)now);
                SessionState.SetInt("pt_sub", 2);
                return;
            }
            if (sub == 2)
            {   // esperar muerte + drops
                if (now - SessionState.GetFloat("pt_t0", 0f) < 3.2) return;
                var drops = Object.FindObjectsByType<DropItem>().Select(d => d.gameObject).ToList();
                var meat = Object.FindObjectsByType<WorldItem>().Where(w => w.name.StartsWith("Carne_")).Select(w => w.gameObject).ToList();
                Append($"   -> tras morir: {drops.Count + meat.Count} objetos en el suelo ({string.Join(", ", drops.Concat(meat).Select(d => d.name + "@y" + d.transform.position.y.ToString("F2") + (d.GetComponentInChildren<Renderer>() != null ? " min.y=" + d.GetComponentInChildren<Renderer>().bounds.min.y.ToString("F2") : "")).Take(12))})");
                if (names[i].EndsWith("_1") && drops.Count > 0) ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/ptd_" + names[i] + ".png"));
                // limpiarlos para contar el siguiente por separado
                foreach (var d in drops.Concat(meat)) Object.Destroy(d);
                SessionState.SetInt("pt_i", i + 1); SessionState.SetInt("pt_sub", 0);
            }
            return;
        }

        if (stage == 3)
        {   // inventario: iconos y apilado
            var inv = ControladorInventario.Instance;
            if (inv == null) { Append("ControladorInventario.Instance == null"); Finish("sin inventario"); return; }
            foreach (var id in new[] { "Hueso", "Piel", "Carne", "Colmillo", "PataCiempies", "Cuerno", "Exoesqueleto", "Aguijon", "Alas" })
            {
                var icon = inv.GetIconByName(id);
                bool ok = inv.AddItem(id, null, 2);
                Append($"inventario {id}: icono={(icon != null ? icon.name : "NULL")} agregado={ok} cantidad={inv.GetItemCount(id)}");
            }
            inv.ToggleInventario();                       // abre el inventario para capturarlo
            SessionState.SetFloat("pt_t0", (float)now);
            SessionState.SetInt("pt_stage", 4);
            return;
        }

        if (stage == 4)
        {
            if (now - SessionState.GetFloat("pt_t0", 0f) < 1.5) return;
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/pt_inventario.png"));
            SessionState.SetFloat("pt_t0", (float)now);
            SessionState.SetInt("pt_stage", 5);
            return;
        }

        if (stage == 5)
        {
            if (now - SessionState.GetFloat("pt_t0", 0f) < 1.0) return;
            var inv = ControladorInventario.Instance;
            if (inv != null && inv.IsOpen) inv.ToggleInventario();
            Time.timeScale = 1f;
            Finish("ok");
        }
    }
}
