using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DropEntry
{
    [Tooltip("Id del item (nombre del prefab en Resources/Drops y del icono)")]
    public string itemId = "Hueso";
    public int amount = 1;
    [Range(0f, 100f)] public float chance = 100f;
    [Tooltip("Entradas con el mismo grupo son variantes de cantidad del mismo item: se hace una sola tirada.")]
    public string group = "";
}

/// <summary>
/// Tabla de objetos que suelta un enemigo al morir.
/// Grupo cuyas probabilidades suman 100 o menos: tirada unica (cada variante excluye a las otras).
/// Grupo que suma mas de 100 (p. ej. 75/50/25): "al menos N": la cantidad mas alta cuya probabilidad cubre la tirada.
/// Entradas sin grupo: tirada independiente.
/// </summary>
public class DropTable : MonoBehaviour
{
    public List<DropEntry> entries = new List<DropEntry>();

    public void Drop(Vector3 center)
    {
        var results = new List<KeyValuePair<string, int>>();

        var grouped = new Dictionary<string, List<DropEntry>>();
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.group))
            {
                if (Random.value * 100f < e.chance) results.Add(new KeyValuePair<string, int>(e.itemId, e.amount));
            }
            else
            {
                string key = e.itemId + "|" + e.group;
                List<DropEntry> l;
                if (!grouped.TryGetValue(key, out l)) { l = new List<DropEntry>(); grouped[key] = l; }
                l.Add(e);
            }
        }

        foreach (var kv in grouped)
        {
            var list = kv.Value;
            float sum = 0f;
            foreach (var e in list) sum += e.chance;
            float roll = Random.value * 100f;
            if (sum <= 100.01f)
            {
                float acc = 0f;
                foreach (var e in list)
                {
                    acc += e.chance;
                    if (roll < acc) { results.Add(new KeyValuePair<string, int>(e.itemId, e.amount)); break; }
                }
            }
            else
            {
                list.Sort((a, b) => b.amount.CompareTo(a.amount));
                foreach (var e in list)
                {
                    if (roll < e.chance) { results.Add(new KeyValuePair<string, int>(e.itemId, e.amount)); break; }
                }
            }
        }

        Vector3 floor = center;
        RaycastHit hit;
        if (Physics.Raycast(center + Vector3.up * 1f, Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore)) floor = hit.point;

        int spawned = 0, total = 0;
        foreach (var r in results) total += r.Value;
        var summary = new List<string>();
        foreach (var r in results)
        {
            summary.Add(r.Value + " " + r.Key);
            var prefab = Resources.Load<GameObject>("Drops/" + r.Key);
            if (prefab == null) { Debug.LogWarning("[DropTable] No existe el prefab Resources/Drops/" + r.Key); continue; }
            for (int i = 0; i < r.Value; i++)
            {
                float ang = (spawned / Mathf.Max(1f, total)) * 360f + Random.Range(-20f, 20f);
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                float ring = total > 1 ? Random.Range(0.8f, 1.8f) : 0f;
                Vector3 pos = floor + dir * ring + Vector3.up * 0.7f;
                var go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                go.name = "Drop_" + r.Key;
                var wi = go.GetComponent<WorldItem>();
                if (wi != null) { wi.itemId = r.Key; wi.quantity = 1; }
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce((dir + Vector3.up * 1.4f).normalized * Random.Range(1.0f, 1.8f), ForceMode.Impulse);
                if (FloorItemPromptManager.Instance != null) FloorItemPromptManager.Instance.RegisterTrackedItem(go);
                spawned++;
            }
        }
        Debug.Log("[DropTable] " + name + ": " + spawned + " objetos soltados (" + string.Join(", ", summary) + ")");
    }
}
