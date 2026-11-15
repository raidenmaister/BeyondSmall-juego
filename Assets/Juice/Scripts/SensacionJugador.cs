using UnityEngine;

/// <summary>
/// "Juice" del propio jugador (Guinxu: Jak and Daxter, Hollow Knight, Flatworld):
///   - squash &amp; stretch: se estira al saltar y caer, se aplasta como gelatina al aterrizar
///   - polvo y sonido al aterrizar (mas cuanto mas fuerte cae)
///   - al recibir un golpe: temblor, destello rojo, congelado de unos frames y sonido
///   - al morir: temblor fuerte, destello rojo y camara lenta para ver de donde vino el golpe
/// Se añade solo al jugador al cargar la escena.
/// </summary>
[DefaultExecutionOrder(200)]   // despues del Animator, que pone la pose de los huesos
public class SensacionJugador : MonoBehaviour
{
    PlayerController pc;
    PlayerHealth vida;
    Transform caderas;
    Vector3 escalaCaderas;
    float alturaCaderas = 1f;

    // deformacion: + aplastado, - estirado (muelle)
    float deform, velDeform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        var p = Object.FindAnyObjectByType<PlayerController>();
        if (p != null && p.GetComponent<SensacionJugador>() == null) p.gameObject.AddComponent<SensacionJugador>();
    }

    void Start()
    {
        pc = GetComponent<PlayerController>();
        vida = GetComponent<PlayerHealth>();
        var anim = GetComponent<Animator>();
        if (anim != null && anim.isHuman) caderas = anim.GetBoneTransform(HumanBodyBones.Hips);
        if (caderas == null) caderas = transform.Find("mixamorig:Hips");
        if (caderas != null)
        {
            escalaCaderas = caderas.localScale;
            alturaCaderas = Mathf.Max(0.3f, caderas.position.y - transform.position.y);
        }
        if (pc != null) { pc.AlAterrizar += Aterrizar; pc.AlSaltar += Saltar; }
        if (vida != null) { vida.AlRecibirGolpe += Golpeado; vida.OnDeath += Muerto; }
    }

    void OnDestroy()
    {
        if (pc != null) { pc.AlAterrizar -= Aterrizar; pc.AlSaltar -= Saltar; }
        if (vida != null) { vida.AlRecibirGolpe -= Golpeado; vida.OnDeath -= Muerto; }
    }

    void Saltar()
    {
        deform = -0.16f; velDeform = 0f;      // se estira al despegar
    }

    void Aterrizar(float velocidad)
    {
        float k = Mathf.InverseLerp(3f, 16f, velocidad);
        deform = Mathf.Lerp(0.08f, 0.3f, k); velDeform = 0f;
        Juice.Polvo(transform.position, Mathf.Lerp(0.6f, 1.4f, k), Mathf.RoundToInt(Mathf.Lerp(5f, 16f, k)));
        SonidosJuice.Tocar(SonidosJuice.Aterrizar, transform.position, Mathf.Lerp(0.35f, 0.9f, k));
        if (k > 0.6f) Juice.Sacudir(0.12f * k);
    }

    void Golpeado(float danio)
    {
        if (vida != null && vida.IsDead) return;
        float k = Mathf.Clamp01(danio / 40f);
        Juice.Sacudir(Mathf.Lerp(0.22f, 0.5f, k));
        Juice.Destello(new Color(0.85f, 0.05f, 0.02f, Mathf.Lerp(0.35f, 0.6f, k)), 0.45f);
        Juice.Congelar(Mathf.Lerp(0.05f, 0.1f, k));
        Juice.Chispas(transform.position + Vector3.up * 1.1f, new Color(1f, 0.2f, 0.15f), 10, 3.5f);
        SonidosJuice.Tocar(SonidosJuice.Dolor, transform.position, 0.9f);
        deform = 0.14f; velDeform = 0f;
    }

    void Muerto()
    {
        // Guinxu: tres efectos a la vez para que sepas exactamente que te ha matado
        Juice.Sacudir(0.85f);
        Juice.Destello(new Color(0.9f, 0f, 0f, 0.75f), 1.1f);
        Juice.CamaraLenta(0.2f, 1.6f);
        SonidosJuice.Tocar(SonidosJuice.Dolor, transform.position, 1f);
    }

    void LateUpdate()
    {
        if (caderas == null) return;
        // estirarse mientras cae rapido
        if (pc != null && !pc.IsGrounded && pc.VelocidadVertical.y < -6f && deform > -0.1f)
            deform = Mathf.MoveTowards(deform, -0.1f, Time.deltaTime * 0.6f);

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        velDeform += (-240f * deform - 15f * velDeform) * dt;
        deform += velDeform * dt;
        if (Mathf.Abs(deform) < 0.001f && Mathf.Abs(velDeform) < 0.01f) { deform = 0f; velDeform = 0f; }

        float sy = 1f - deform, sxz = 1f + deform * 0.55f;
        caderas.localScale = new Vector3(escalaCaderas.x * sxz, escalaCaderas.y * sy, escalaCaderas.z * sxz);
        // las piernas cuelgan de la cadera: bajarla para que los pies sigan en el suelo
        if (deform > 0f) caderas.position -= Vector3.up * alturaCaderas * (1f - sy);
    }
}
