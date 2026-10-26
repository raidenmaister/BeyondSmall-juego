using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animacion procedural de los enemigos cuyos modelos no traen esqueleto: correr, atacar y morir.
/// Los meshes ("Part_*") vienen horneados en el espacio local de este objeto: adelante = +Z, derecha = +X, arriba = +Y.
///   Undulate : cuerpo largo que ondula (oruga, lombriz, ciempies).
///   Skitter  : patas que se mueven en tripode (hormiga, escarabajo).
///   Flutter  : alas que aletean (mariposa).
///   Plates   : cuerpo hecho de placas que puede enrollarse en bola (cochinilla).
///   Animator : usa un Animator con estados Idle / Run / Attack / Death (abeja).
/// La IA solo escribe los campos de estado (move, pitch, roll...) y llama a Attack() / Die().
/// </summary>
public class CreatureVisual : MonoBehaviour
{
    public enum Mode { Undulate, Skitter, Flutter, Plates, Animator }

    public Mode mode = Mode.Skitter;

    [Header("Ritmo")]
    public float walkFreq = 2.5f;
    public float runFreq = 5.5f;

    [Header("Patas (Skitter / Plates)")]
    public float legSwing = 0.07f;
    public float legLift = 0.05f;

    [Header("Ondulacion (Undulate)")]
    public float waves = 1.5f;
    public float undAmp = 0.09f;
    public bool inchworm;

    [Header("Alas (Flutter)")]
    public float flapFreq = 12f;
    public float flapAngle = 0.8f;
    public float flapBias = 0.25f;

    // --- estado que escribe la IA ---
    [HideInInspector] public float move;     // 0 quieto .. 1 corriendo
    [HideInInspector] public float pitch;    // grados: levanta (+) o baja (-) la cabeza
    [HideInInspector] public float roll;     // grados alrededor del eje de avance (volteo al morir)
    [HideInInspector] public float sink;     // desplazamiento vertical (enterrarse)
    [HideInInspector] public float lunge;    // desplazamiento hacia delante
    [HideInInspector] public float curl;     // 0..1 enrollarse (Plates)
    [HideInInspector] public float spin;     // grados: rodar (Plates)
    [HideInInspector] public float shake;    // 0..1 temblor
    [HideInInspector] public Vector3 offset; // desplazamiento local (formacion del enjambre)
    public bool dead;

    // --- internos ---
    class PartData { public MeshFilter mf; public Mesh mesh; public Vector3[] basePos; public Vector3[] work; }
    readonly List<PartData> parts = new List<PartData>();
    Vector3 basePos;
    Quaternion baseRot;
    float phase, moveSmooth, deadTime, attackT, attackLen = 0.4f, timeOffset, flapPhase;
    float zMin, zMax, yMin, yMax, halfW, len, hgt;
    Animator animator;
    string curAnim = "";
    bool wasStatic;

    // Plates
    class Plate { public Transform t; public Vector3 restPos, ballPos; public Quaternion restRot, ballRot; public Vector3 restScale; public bool isLimb; }
    readonly List<Plate> plates = new List<Plate>();
    float ballR;

    void Awake()
    {
        basePos = transform.localPosition;
        baseRot = transform.localRotation;
        timeOffset = Random.value * 10f;
        animator = GetComponentInChildren<Animator>();

        if (mode == Mode.Plates) SetupPlates();
        else if (mode != Mode.Animator) SetupMeshes();
    }

    void SetupMeshes()
    {
        bool first = true;
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.name.StartsWith("Part")) continue;
            var inst = Instantiate(mf.sharedMesh);
            inst.name = mf.sharedMesh.name + "_inst";
            mf.sharedMesh = inst;
            var pd = new PartData { mf = mf, mesh = inst, basePos = inst.vertices };
            pd.work = (Vector3[])pd.basePos.Clone();
            parts.Add(pd);
            foreach (var v in pd.basePos)
            {
                if (first) { zMin = zMax = v.z; yMin = yMax = v.y; halfW = Mathf.Abs(v.x); first = false; }
                zMin = Mathf.Min(zMin, v.z); zMax = Mathf.Max(zMax, v.z);
                yMin = Mathf.Min(yMin, v.y); yMax = Mathf.Max(yMax, v.y);
                halfW = Mathf.Max(halfW, Mathf.Abs(v.x));
            }
        }
        len = Mathf.Max(0.01f, zMax - zMin);
        hgt = Mathf.Max(0.01f, yMax - yMin);
        halfW = Mathf.Max(0.01f, halfW);
    }

    void SetupPlates()
    {
        var head = transform.Find("Plate0");
        int n = 0;
        while (transform.Find("Plate" + n) != null) n++;
        // radio de la bola a partir del ancho del cuerpo
        float width = 1f;
        var mid = transform.Find("Plate" + (n / 2));
        if (mid != null) width = mid.localScale.x;
        ballR = width * 0.55f;
        for (int i = 0; i < n; i++)
        {
            var t = transform.Find("Plate" + i);
            float u = n > 1 ? i / (float)(n - 1) : 0f;
            float phi = Mathf.PI * 0.5f - u * Mathf.PI * 2f * 0.93f;
            var p = new Plate { t = t, restPos = t.localPosition, restRot = t.localRotation, restScale = t.localScale };
            p.ballPos = new Vector3(0f, ballR, 0f) + new Vector3(0f, Mathf.Sin(phi), Mathf.Cos(phi)) * ballR;
            p.ballRot = Quaternion.Euler(Mathf.Rad2Deg * (Mathf.PI * 0.5f - phi), 0f, 0f);
            plates.Add(p);
        }
        foreach (Transform c in transform)
        {
            if (c.name.StartsWith("Leg") || c.name.StartsWith("Ant") || c.name.StartsWith("Eye"))
                plates.Add(new Plate { t = c, restPos = c.localPosition, restRot = c.localRotation, restScale = c.localScale, isLimb = true });
        }
    }

    // ------------------------------------------------------------------ API para la IA
    public void Attack(float duration = 0.4f) { attackT = 1f; attackLen = Mathf.Max(0.05f, duration); }

    public void Play(string state)
    {
        if (mode != Mode.Animator || animator == null || curAnim == state) return;
        curAnim = state;
        animator.CrossFadeInFixedTime(state, 0.1f);
    }

    public void Die() { dead = true; }

    // ------------------------------------------------------------------
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dead) deadTime += dt;
        if (attackT > 0f) attackT = Mathf.Max(0f, attackT - dt / attackLen);
        moveSmooth = Mathf.MoveTowards(moveSmooth, dead ? 0f : move, dt * 6f);
        float f = Mathf.Lerp(walkFreq * 0.4f, runFreq, moveSmooth);
        if (!dead || deadTime < 1.2f) phase += dt * f;
        float t = Time.time + timeOffset;

        // ----- pose del cuerpo completo
        float bob = mode == Mode.Skitter || mode == Mode.Plates ? Mathf.Abs(Mathf.Sin(phase * Mathf.PI)) * 0.03f * moveSmooth * (len > 0 ? len : 1f) : 0f;
        float atk = Mathf.Sin(attackT * Mathf.PI); // 0 -> 1 -> 0
        float dieRoll = dead ? Mathf.SmoothStep(0f, 180f, Mathf.Clamp01(deadTime / 0.45f)) : 0f;
        Vector3 shk = shake > 0f ? new Vector3(Mathf.Sin(t * 70f), 0f, Mathf.Cos(t * 63f)) * 0.06f * shake : Vector3.zero;
        transform.localPosition = basePos + offset + new Vector3(0f, bob + sink, lunge + atk * 0.35f) + shk;
        transform.localRotation = baseRot * Quaternion.Euler(-(pitch + atk * 12f), 0f, roll + dieRoll);

        switch (mode)
        {
            case Mode.Undulate: DeformUndulate(t); break;
            case Mode.Skitter: DeformSkitter(t); break;
            case Mode.Flutter: DeformFlutter(t); break;
            case Mode.Plates: UpdatePlates(t); break;
            case Mode.Animator: break;
        }
    }

    float DeathFade { get { return dead ? Mathf.Clamp01(1f - deadTime / 1.2f) : 1f; } }
    float DeathCurl { get { return dead ? Mathf.Clamp01(deadTime / 0.6f) : 0f; } }

    void Commit(PartData p)
    {
        p.mesh.vertices = p.work;
        p.mesh.RecalculateBounds();
    }

    void DeformUndulate(float t)
    {
        float amp = Mathf.Lerp(0.2f, 1f, moveSmooth) * DeathFade + (attackT > 0f ? 0.3f * attackT : 0f);
        float curlD = DeathCurl;
        foreach (var p in parts)
        {
            var b = p.basePos; var w = p.work;
            for (int i = 0; i < b.Length; i++)
            {
                var v = b[i];
                float tt = (v.z - zMin) / len;            // 0 cola .. 1 cabeza
                float lat = Mathf.Clamp01(Mathf.Abs(v.x) / halfW);
                float h = (v.y - yMin) / hgt;
                float s = Mathf.Sin(6.2832f * (waves * tt - phase));
                v.x += s * undAmp * len * Mathf.Lerp(1f, 0.35f, tt) * amp;
                if (inchworm) v.y += Mathf.Max(0f, Mathf.Sin(6.2832f * (tt - phase * 0.5f))) * 0.10f * len * amp * Mathf.Clamp01(h + 0.3f);
                // patitas: pequeno barrido adelante/atras en los vertices laterales y bajos
                float legMask = Mathf.Clamp01((lat - 0.35f) / 0.4f) * Mathf.Clamp01((0.6f - h) / 0.4f);
                if (legMask > 0f) v.z += Mathf.Sin(6.2832f * (phase * 2f - tt * waves * 3f)) * 0.012f * len * legMask * Mathf.Max(0.3f, moveSmooth) * DeathFade;
                // al morir se enrosca
                if (curlD > 0f) v.x += (tt - 0.5f) * (tt - 0.5f) * 1.6f * len * 0.5f * curlD;
                w[i] = v;
            }
            Commit(p);
        }
    }

    void DeformSkitter(float t)
    {
        float amp = Mathf.Lerp(0.15f, 1f, moveSmooth) * DeathFade;
        float curlD = DeathCurl;
        bool still = moveSmooth < 0.02f && !dead && attackT <= 0f;
        if (still && wasStatic) return;
        wasStatic = still;
        foreach (var p in parts)
        {
            var b = p.basePos; var w = p.work;
            for (int i = 0; i < b.Length; i++)
            {
                var v = b[i];
                float tt = (v.z - zMin) / len;
                float lat = v.x / halfW;
                float h = (v.y - yMin) / hgt;
                float legMask = Mathf.Clamp01((Mathf.Abs(lat) - 0.3f) / 0.4f) * Mathf.Clamp01((0.6f - h) / 0.4f);
                if (legMask > 0f)
                {
                    int group = Mathf.Min(2, Mathf.FloorToInt(tt * 3f));
                    float ph = Mathf.PI * (((group + (lat > 0f ? 1 : 0)) & 1));
                    float s = Mathf.Sin(6.2832f * phase + ph);
                    v.z += s * legSwing * len * legMask * amp;
                    v.y += Mathf.Max(0f, Mathf.Cos(6.2832f * phase + ph)) * legLift * len * legMask * amp;
                    if (curlD > 0f) { v.x *= Mathf.Lerp(1f, 0.55f, curlD); v.y += hgt * 0.35f * curlD * legMask; }
                }
                else if (tt > 0.8f && h > 0.35f && Mathf.Abs(lat) > 0.05f && Mathf.Abs(lat) < 0.5f)
                {
                    // antenas
                    v.x += Mathf.Sin(t * 7f + lat * 5f) * 0.012f * len * (0.3f + moveSmooth);
                }
                w[i] = v;
            }
            Commit(p);
        }
    }

    void DeformFlutter(float t)
    {
        float freq = Mathf.Lerp(flapFreq * 0.55f, flapFreq, moveSmooth);
        if (!dead) flapPhase += Time.deltaTime * freq;
        float theta = dead ? Mathf.Lerp(flapBias + 0.6f, 1.3f, DeathCurl) : flapBias + Mathf.Sin(flapPhase) * flapAngle;
        foreach (var p in parts)
        {
            var b = p.basePos; var w = p.work;
            for (int i = 0; i < b.Length; i++)
            {
                var v = b[i];
                float lat = Mathf.Abs(v.x) / halfW;
                float mask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lat - 0.08f) / 0.25f));
                if (mask > 0f)
                {
                    float a = Mathf.Sign(v.x) * theta * mask;
                    float cy = yMin + hgt * 0.4f;
                    float x = v.x, y = v.y - cy;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    v.x = x * c - y * s;
                    v.y = x * s + y * c + cy;
                }
                w[i] = v;
            }
            Commit(p);
        }
    }

    void UpdatePlates(float t)
    {
        curl = Mathf.Clamp01(curl);
        float cb = Mathf.SmoothStep(0f, 1f, curl);
        Quaternion qs = Quaternion.Euler(spin, 0f, 0f);
        Vector3 center = new Vector3(0f, ballR, 0f);
        int n = 0;
        foreach (var p in plates)
        {
            if (p.t == null) continue;
            if (!p.isLimb)
            {
                Vector3 rest = p.restPos + new Vector3(0f, Mathf.Sin(phase * 6.2832f + n * 0.6f) * 0.015f * moveSmooth, 0f);
                Vector3 ball = center + qs * (p.ballPos - center);
                p.t.localPosition = Vector3.Lerp(rest, ball, cb);
                p.t.localRotation = Quaternion.Slerp(p.restRot, qs * p.ballRot, cb);
                n++;
            }
            else
            {
                // patas y antenas: se guardan al enrollarse; las patas se mueven al caminar
                float side = p.restPos.x >= 0f ? 1f : -1f;
                float ph = Mathf.PI * ((Mathf.RoundToInt(p.restPos.z * 3f) + (side > 0 ? 1 : 0)) & 1);
                float swing = Mathf.Sin(6.2832f * phase + ph) * 28f * moveSmooth * DeathFade;
                p.t.localPosition = p.restPos;
                p.t.localRotation = p.restRot * Quaternion.Euler(swing, 0f, dead ? 40f * DeathCurl * side : 0f);
                p.t.localScale = p.restScale * (1f - cb);
            }
        }
    }
}
