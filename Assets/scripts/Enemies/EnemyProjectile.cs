using UnityEngine;

/// <summary>Proyectil lento y esquivable (acido de la oruga, telarana de la arana...).</summary>
public class EnemyProjectile : MonoBehaviour
{
    public Vector3 velocity;
    public float damage = 10f;
    public float slowMultiplier = 1f;
    public float slowSeconds = 0f;
    public float hitRadius = 1.0f;
    public float life = 4f;

    Transform player;
    PlayerHealth playerHealth;

    public static EnemyProjectile Spawn(Vector3 from, Vector3 target, float speed, float damage, Color color,
        float slowMultiplier = 1f, float slowSeconds = 0f, float size = 0.6f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "EnemyProjectile";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = from;
        go.transform.localScale = Vector3.one * size;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 1.6f); }
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.35f; trail.startWidth = size * 0.8f; trail.endWidth = 0f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = color; trail.endColor = new Color(color.r, color.g, color.b, 0f);

        var p = go.AddComponent<EnemyProjectile>();
        p.velocity = (target - from).normalized * speed;
        p.damage = damage; p.slowMultiplier = slowMultiplier; p.slowSeconds = slowSeconds;
        return p;
    }

    void Start()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null) { player = pc.transform; playerHealth = pc.GetComponent<PlayerHealth>(); }
    }

    void Update()
    {
        transform.position += velocity * Time.deltaTime;
        life -= Time.deltaTime;
        if (life <= 0f || transform.position.y <= 0.1f) { Splat(); return; }

        if (player != null)
        {
            Vector3 chest = player.position + Vector3.up * 1.0f;
            if ((chest - transform.position).sqrMagnitude <= hitRadius * hitRadius)
            {
                if (playerHealth != null && damage > 0f) playerHealth.TakeDamage(damage);
                if (slowSeconds > 0f) StatusEffects.Slow(slowMultiplier, slowSeconds);
                Destroy(gameObject);
            }
        }
    }

    void Splat()
    {
        // mancha en el suelo que se desvanece
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = new Vector3(transform.position.x, 0.17f, transform.position.z);
        go.transform.localScale = new Vector3(transform.localScale.x * 2.2f, 0.01f, transform.localScale.x * 2.2f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = GetComponent<Renderer>().sharedMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(go, 2.5f);
        Destroy(gameObject);
    }
}
