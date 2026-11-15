using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Ajustes de camara de los videos:
///   - Alva Majo (truco 3): la camara no debe seguir al milimetro ni dar tirones; tiene que mirar un poco
///     hacia donde vas (lookahead con suavizado 6) y no volverse loca al saltar (ignorar la Y del lookahead,
///     y mas amortiguacion vertical que horizontal).
///   - Guinxu: inercia al frenar (la camara sigue avanzando un poco y frena poco a poco).
///   - Juice extra: al correr el campo de vision se abre un poco y se nota la velocidad.
/// Se aplica sola sobre la FreeLook Camera al cargar la escena de juego.
/// </summary>
public class CamaraViva : MonoBehaviour
{
    CinemachineCamera cam;
    PlayerController pc;
    float fovBase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        var of = Object.FindAnyObjectByType<CinemachineOrbitalFollow>();
        if (of == null || Object.FindAnyObjectByType<PlayerController>() == null) return;
        if (of.GetComponent<CamaraViva>() == null) of.gameObject.AddComponent<CamaraViva>();
    }

    void Start()
    {
        cam = GetComponent<CinemachineCamera>();
        pc = Object.FindAnyObjectByType<PlayerController>();
        if (cam != null) fovBase = cam.Lens.FieldOfView;

        var of = GetComponent<CinemachineOrbitalFollow>();
        if (of != null)
        {
            // inercia: algo mas de retraso en horizontal y bastante en vertical (saltos sin tirones)
            var tr = of.TrackerSettings;
            tr.PositionDamping = new Vector3(0.6f, 1.4f, 0.6f);
            of.TrackerSettings = tr;
        }
        var rc = GetComponent<CinemachineRotationComposer>();
        if (rc != null)
        {
            var la = rc.Lookahead;
            la.Enabled = true;
            la.Time = 0.3f;
            la.Smoothing = 6f;
            la.IgnoreY = true;
            rc.Lookahead = la;
            rc.Damping = new Vector2(0.6f, 0.8f);
        }
    }

    void LateUpdate()
    {
        if (cam == null || pc == null || Time.timeScale <= 0f) return;
        float objetivo = fovBase + (pc.IsRunning ? 6f : 0f) * Mathf.Min(1f, Ajustes.Temblor + 0.3f);
        var lens = cam.Lens;
        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, objetivo, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3.5f));
        cam.Lens = lens;
    }
}
