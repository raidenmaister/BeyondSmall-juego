using UnityEngine;

/// <summary>
/// Repara normales nulas en mallas generadas por codigo.
/// Una normal (0,0,0) hace que el shader Lit normalice un vector cero -> NaN en el pixel;
/// el pixel sale negro y el Bloom lo esparce como un destello blanco enorme.
/// Pasa con caras de doble lado que comparten vertices (las normales se cancelan),
/// polos de esferas y triangulos degenerados. Los builders llaman a esto antes de guardar cada malla.
/// </summary>
public static class NormalesSeguras
{
    /// <summary>Sustituye normales nulas/NaN por la de alguna cara vecina valida (o arriba). Devuelve cuantas arreglo.</summary>
    public static int Sanear(Mesh m)
    {
        if (m == null) return 0;
        var n = m.normals;
        var v = m.vertices;
        if (n == null || n.Length != v.Length || v.Length == 0) return 0;

        int malas = 0;
        var mala = new bool[n.Length];
        for (int i = 0; i < n.Length; i++)
            if (!Valida(n[i])) { mala[i] = true; malas++; }
        if (malas == 0) return 0;

        // primera cara no degenerada que toca cada vertice malo
        var repuesto = new Vector3[n.Length];
        for (int s = 0; s < m.subMeshCount; s++)
        {
            if (m.GetTopology(s) != MeshTopology.Triangles) continue;
            var t = m.GetTriangles(s);
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                int a = t[k], b = t[k + 1], c = t[k + 2];
                if (!mala[a] && !mala[b] && !mala[c]) continue;
                Vector3 cara = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (cara.sqrMagnitude < 1e-12f) continue;
                cara.Normalize();
                if (mala[a] && repuesto[a] == Vector3.zero) repuesto[a] = cara;
                if (mala[b] && repuesto[b] == Vector3.zero) repuesto[b] = cara;
                if (mala[c] && repuesto[c] == Vector3.zero) repuesto[c] = cara;
            }
        }
        for (int i = 0; i < n.Length; i++)
            if (mala[i]) n[i] = repuesto[i] != Vector3.zero ? repuesto[i] : Vector3.up;
        m.normals = n;

        // las tangentes se calculan con las normales: si existian, rehacerlas
        var tg = m.tangents;
        if (tg != null && tg.Length == n.Length && m.uv != null && m.uv.Length == n.Length) m.RecalculateTangents();
        return malas;
    }

    static bool Valida(Vector3 x)
    {
        float q = x.sqrMagnitude;
        return q > 1e-6f && !float.IsNaN(q) && !float.IsInfinity(q);
    }
}
