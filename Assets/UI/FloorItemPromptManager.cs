using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FloorItemPromptManager : MonoBehaviour
{
    [System.Serializable]
    public class TrackedItem
    {
        public GameObject gameObject;
        public Renderer[] renderers;
        public Vector3 lastVisualCenter;
        public float topY;
        public bool isNear;

        public void UpdateBounds()
        {
            if (renderers != null && renderers.Length > 0 && renderers[0] != null)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null) b.Encapsulate(renderers[i].bounds);
                }
                lastVisualCenter = b.center;
                topY = b.max.y;
            }
            else if (gameObject != null)
            {
                lastVisualCenter = gameObject.transform.position;
                topY = gameObject.transform.position.y + 0.3f;
            }
        }
    }

    private class PromptInstance
    {
        public GameObject root;
        public CanvasGroup canvasGroup;
        public RectTransform rectTransform;
        public TrackedItem currentItem;
        public float currentAlpha;
        public float targetAlpha;
        public float baseWorldY;
    }

    [Header("Referencias")]
    [Tooltip("Transform del jugador. Si no se asigna, se busca automáticamente.")]
    public Transform player;

    [Tooltip("Cámara a la que mirará la letra E. Si está vacía, usa Camera.main.")]
    public Camera targetCamera;

    [Header("Configuración de Proximidad")]
    [Tooltip("Distancia en metros para que aparezca la letra E arriba del objeto.")]
    [Range(1f, 10f)]
    public float detectionDistance = 3.0f;

    [Tooltip("Margen adicional para evitar parpadeos al alejarse.")]
    public float hideBuffer = 0.3f;

    [Tooltip("Altura en metros por encima del objeto a la que flota la letra E.")]
    public float floatHeightOffset = 0.45f;

    [Header("Animación")]
    public float fadeSpeed = 10f;
    public float bobbingSpeed = 4f;
    public float bobbingAmount = 0.04f;

    [Header("Apariencia del Prompt")]
    [Tooltip("Sprite del fondo del prompt (si está vacío, se usa uno circular suave).")]
    public Sprite promptBackgroundSprite;

    [Header("Objetos Tirados en el Piso")]
    [Tooltip("Lista de objetos monitoreados (se detectan automáticamente al inicio si está vacía).")]
    public List<GameObject> floorObjects = new List<GameObject>();

    private List<TrackedItem> trackedItems = new List<TrackedItem>();
    private List<PromptInstance> promptPool = new List<PromptInstance>();
    private static Sprite defaultCircleSprite;

    public int TrackedCount => trackedItems.Count;
    public int ActivePromptCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < promptPool.Count; i++)
            {
                if (promptPool[i].root != null && promptPool[i].root.activeSelf && promptPool[i].targetAlpha > 0f) c++;
            }
            return c;
        }
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        FindPlayerIfNeeded();
        if (targetCamera == null) targetCamera = Camera.main;

        if (trackedItems == null || trackedItems.Count == 0)
        {
            InitializeTrackedItems();
        }

        if (promptPool == null || promptPool.Count == 0)
        {
            InitializePromptPool(poolSize: 8);
        }
    }

    private void Update()
    {
        EnsureInitialized();
        if (player == null) return;

        Vector3 playerPos = player.position;

        // 1. Evaluar proximidad con cada objeto del piso
        for (int i = 0; i < trackedItems.Count; i++)
        {
            var item = trackedItems[i];
            if (item.gameObject == null || !item.gameObject.activeInHierarchy)
            {
                item.isNear = false;
                continue;
            }

            Vector3 itemPos = item.lastVisualCenter;
            itemPos.y = playerPos.y; // Medir distancia en plano horizontal
            float dist = Vector3.Distance(playerPos, itemPos);

            if (!item.isNear)
            {
                if (dist <= detectionDistance)
                {
                    item.isNear = true;
                    item.UpdateBounds();
                }
            }
            else
            {
                if (dist > (detectionDistance + hideBuffer))
                {
                    item.isNear = false;
                }
            }
        }

        // 2. Asignar prompts de la piscina a los objetos cercanos
        UpdatePromptAssignments();

        // 3. Animar, desvanecer y orientar los prompts activos
        UpdatePromptVisuals(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        // Orientar (billboard) los prompts hacia la cámara
        Quaternion camRot = targetCamera.transform.rotation;
        for (int i = 0; i < promptPool.Count; i++)
        {
            var p = promptPool[i];
            if (p.root.activeSelf)
            {
                p.root.transform.rotation = camRot;
            }
        }
    }

    public void InitializeTrackedItems()
    {
        trackedItems.Clear();

        // Si no se asignaron manualmente en el inspector, buscar los 49 objetos del piso
        if (floorObjects == null || floorObjects.Count == 0)
        {
            floorObjects = new List<GameObject>();
            var roots = gameObject.scene.GetRootGameObjects();
            foreach (var r in roots)
            {
                string n = r.name;
                if (n.StartsWith("clip_") || n.StartsWith("Eraser_") || n.StartsWith("black_generic_hair_tie_") || n.StartsWith("plaggy_cc0-pin-569_"))
                {
                    floorObjects.Add(r);
                }
            }
        }

        foreach (var go in floorObjects)
        {
            if (go == null) continue;
            var item = new TrackedItem
            {
                gameObject = go,
                renderers = go.GetComponentsInChildren<Renderer>()
            };
            item.UpdateBounds();
            trackedItems.Add(item);
        }
    }

    private void InitializePromptPool(int poolSize)
    {
        for (int i = 0; i < poolSize; i++)
        {
            var prompt = CreatePromptGameObject(i);
            promptPool.Add(prompt);
        }
    }

    private PromptInstance CreatePromptGameObject(int index)
    {
        var go = new GameObject(string.Format("ItemPrompt_E_{0}", index), typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 50;

        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(80f, 80f);
        rt.localScale = new Vector3(0.007f, 0.007f, 0.007f); // Tamaño ~0.56m en espacio de mundo

        // Fondo oscuro circular/redondeado
        var bgGO = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        bgGO.transform.SetParent(go.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        var bgImg = bgGO.GetComponent<UnityEngine.UI.Image>();
        Sprite spriteToUse = promptBackgroundSprite != null ? promptBackgroundSprite : GetDefaultCircleSprite();
        bgImg.sprite = spriteToUse;
        bgImg.color = new Color(0.08f, 0.10f, 0.15f, 0.92f); // Azul oscuro casi negro

        // Borde fino brillante
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        borderGO.transform.SetParent(go.transform, false);
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.sizeDelta = new Vector2(-4f, -4f);

        var borderImg = borderGO.GetComponent<UnityEngine.UI.Image>();
        borderImg.sprite = spriteToUse;
        borderImg.color = new Color(0.2f, 0.75f, 1.0f, 0.5f); // Borde azul cian suave

        // Letra "E"
        var textGO = new GameObject("Letter_E", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;

        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = "E";
        tmp.fontSize = 46f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        go.SetActive(false);

        return new PromptInstance
        {
            root = go,
            canvasGroup = cg,
            rectTransform = rt,
            currentItem = null,
            currentAlpha = 0f,
            targetAlpha = 0f,
            baseWorldY = 0f
        };
    }

    private static Sprite GetDefaultCircleSprite()
    {
        if (defaultCircleSprite != null) return defaultCircleSprite;

        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = (size - 2) * 0.5f;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        defaultCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return defaultCircleSprite;
    }

    private void UpdatePromptAssignments()
    {
        // 1. Desasignar prompts cuyos objetos ya no están cerca
        for (int i = 0; i < promptPool.Count; i++)
        {
            var p = promptPool[i];
            if (p.currentItem != null)
            {
                if (!p.currentItem.isNear || p.currentItem.gameObject == null || !p.currentItem.gameObject.activeInHierarchy)
                {
                    p.targetAlpha = 0f;
                    // Cuando se desvanece por completo, se libera el item
                    if (p.currentAlpha <= 0.01f)
                    {
                        p.currentItem = null;
                        p.root.SetActive(false);
                    }
                }
            }
        }

        // 2. Asignar prompts disponibles a los objetos cercanos que aún no tengan uno
        for (int i = 0; i < trackedItems.Count; i++)
        {
            var item = trackedItems[i];
            if (item.isNear && item.gameObject != null && item.gameObject.activeInHierarchy)
            {
                bool alreadyAssigned = false;
                for (int j = 0; j < promptPool.Count; j++)
                {
                    if (promptPool[j].currentItem == item)
                    {
                        alreadyAssigned = true;
                        promptPool[j].targetAlpha = 1f;
                        break;
                    }
                }

                if (!alreadyAssigned)
                {
                    // Buscar un prompt libre en el pool
                    var freePrompt = GetFreePrompt();
                    if (freePrompt != null)
                    {
                        freePrompt.currentItem = item;
                        freePrompt.targetAlpha = 1f;
                        freePrompt.baseWorldY = item.topY + floatHeightOffset;
                        freePrompt.root.transform.position = new Vector3(item.lastVisualCenter.x, freePrompt.baseWorldY, item.lastVisualCenter.z);
                        freePrompt.root.SetActive(true);
                    }
                }
            }
        }
    }

    private PromptInstance GetFreePrompt()
    {
        for (int i = 0; i < promptPool.Count; i++)
        {
            if (promptPool[i].currentItem == null && promptPool[i].targetAlpha <= 0f)
            {
                return promptPool[i];
            }
        }
        return null;
    }

    private void UpdatePromptVisuals(float deltaTime)
    {
        float bobOffset = Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmount;

        for (int i = 0; i < promptPool.Count; i++)
        {
            var p = promptPool[i];
            if (!p.root.activeSelf) continue;

            // Fade in / Fade out
            p.currentAlpha = Mathf.MoveTowards(p.currentAlpha, p.targetAlpha, fadeSpeed * deltaTime);
            if (p.canvasGroup != null)
            {
                p.canvasGroup.alpha = p.currentAlpha;
            }

            // Actualizar posición flotante sobre el objeto
            if (p.currentItem != null)
            {
                float targetY = p.currentItem.topY + floatHeightOffset + bobOffset;
                p.root.transform.position = new Vector3(p.currentItem.lastVisualCenter.x, targetY, p.currentItem.lastVisualCenter.z);
            }

            // Si terminó de desvanecerse y no tiene targetAlpha
            if (p.currentAlpha <= 0.001f && p.targetAlpha == 0f)
            {
                p.currentItem = null;
                p.root.SetActive(false);
            }
        }
    }

    private void FindPlayerIfNeeded()
    {
        if (player == null)
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null) player = p.transform;
            else
            {
                var go = GameObject.Find("Jugador");
                if (go != null) player = go.transform;
            }
        }
    }
}
