using UnityEngine;

/// <summary>Destellos suaves sobre un objeto recogible para que se vea bien en el suelo.</summary>
public class ItemSparkle : MonoBehaviour
{
    public Color color = new Color(1f, 0.95f, 0.55f, 1f);
    static Texture2D softTex;

    void Start()
    {
        if (transform.Find("Sparkle") != null) return;
        if (softTex == null)
        {
            softTex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    softTex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            softTex.Apply();
        }

        var rends = GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(transform.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);

        var go = new GameObject("Sparkle");
        go.transform.SetParent(transform, false);
        go.transform.position = b.center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.loop = true; main.startLifetime = 1.6f; main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startColor = color; main.maxParticles = 12;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 4f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = Mathf.Max(b.extents.x, b.extents.z, 0.3f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = 0.4f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = softTex;
        pr.sharedMaterial = mat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }
}
