using UnityEngine;

/// <summary>
/// Impide que el jugador atraviese a los bichos.
/// Los bichos usan colliders "trigger" (se mueven por transform y un collider solido
/// empujaria al CharacterController a traves del piso), asi que aqui resolvemos el
/// solapamiento a mano: cada frame se buscan los colliders de criaturas vivas que
/// tocan la capsula del jugador y se le saca hacia afuera solo en horizontal.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CreatureBlocker : MonoBehaviour
{
    [Tooltip("Separacion extra entre el jugador y el bicho")]
    public float margen = 0.08f;
    [Tooltip("Empuje maximo por frame (evita teletransportes si un bicho grande aparece encima)")]
    public float empujeMaximo = 0.6f;

    CharacterController cc;
    readonly Collider[] buffer = new Collider[32];

    void Awake() { cc = GetComponent<CharacterController>(); }

    void LateUpdate()
    {
        if (cc == null || !cc.enabled) return;

        float radio = cc.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) + margen;
        Vector3 centro = transform.TransformPoint(cc.center);
        float mitad = Mathf.Max(0f, cc.height * transform.lossyScale.y * 0.5f - radio);
        Vector3 arriba = centro + Vector3.up * mitad, abajo = centro - Vector3.up * mitad;

        int n = Physics.OverlapCapsuleNonAlloc(arriba, abajo, radio, buffer, ~0, QueryTriggerInteraction.Collide);
        Vector3 empuje = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            var col = buffer[i];
            if (col == null || col.transform.IsChildOf(transform)) continue;
            var vida = col.GetComponentInParent<RatHealth>();
            if (vida == null || vida.IsDead) continue;
            var ai = vida.GetComponent<CreatureAI>();
            if (ai != null && (ai.IsDead || ai.IsHidden)) continue;

            // punto del bicho mas cercano al eje del jugador, a la altura correspondiente
            Vector3 eje = new Vector3(centro.x, Mathf.Clamp(col.bounds.center.y, abajo.y, arriba.y), centro.z);
            Vector3 cercano = SoportaClosestPoint(col) ? col.ClosestPoint(eje) : col.bounds.ClosestPoint(eje);
            Vector3 d = eje - cercano; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.0001f)
            {
                // el jugador quedo dentro del bicho: salir alejandose de su centro
                d = eje - col.bounds.center; d.y = 0f;
                if (d.sqrMagnitude < 0.0001f) d = -transform.forward;
                empuje += d.normalized * radio;
            }
            else if (dist < radio)
            {
                empuje += d / dist * (radio - dist);
            }
        }

        if (empuje.sqrMagnitude > 0f)
        {
            empuje = Vector3.ClampMagnitude(empuje, empujeMaximo);
            cc.Move(empuje);   // Move respeta paredes y suelo
        }
    }

    static bool SoportaClosestPoint(Collider c)
    {
        return c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider m && m.convex);
    }
}
