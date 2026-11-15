using UnityEngine;

/// <summary>
/// Le dice al shader del follaje (Exterior/Follaje, ExteriorComun.hlsl) donde esta el jugador: las plantas que quedan
/// entre la camara y el se vuelven invisibles para no taparlo, y las demas se dibujan enteras (sin tramado).
/// Se crea solo al arrancar; en escenas sin jugador (menu) el shader vuelve al fundido por distancia.
/// </summary>
public class FollajeJugador : MonoBehaviour
{
    static readonly int Id = Shader.PropertyToID("_BS_Jugador");
    PlayerController jugador;
    CharacterController cc;
    float buscar;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Crear()
    {
        Shader.SetGlobalVector(Id, Vector4.zero);
        if (FindAnyObjectByType<FollajeJugador>() != null) return;
        var go = new GameObject("FollajeJugador");
        DontDestroyOnLoad(go);
        go.AddComponent<FollajeJugador>();
    }

    void LateUpdate()
    {
        if (jugador == null)
        {
            buscar -= Time.unscaledDeltaTime;
            if (buscar <= 0f) { buscar = 1f; jugador = FindAnyObjectByType<PlayerController>(); cc = jugador != null ? jugador.GetComponent<CharacterController>() : null; }
        }
        // en las cinematicas (final) no hay que despejar nada
        if (jugador == null || !jugador.isActiveAndEnabled || FinalJuego.Activo || FotografoFinal.Trabajando) { Shader.SetGlobalVector(Id, Vector4.zero); return; }
        float alto = cc != null ? cc.height * cc.transform.lossyScale.y : 1.8f;
        Vector3 p = jugador.transform.position + Vector3.up * alto * 0.6f;
        Shader.SetGlobalVector(Id, new Vector4(p.x, p.y, p.z, 1f));
    }

    void OnDestroy() { Shader.SetGlobalVector(Id, Vector4.zero); }
}
