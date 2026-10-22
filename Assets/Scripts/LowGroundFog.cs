using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LowGroundFog : MonoBehaviour
{
    [Header("Altura de la Neblina del Piso")]
    [Tooltip("Altura mínima desde el suelo")]
    [SerializeField] private float minHeight = 0.04f;
    [Tooltip("Altura máxima (0.58 cubre el cuerpo del goblin dejando solo su cabeza afuera)")]
    [SerializeField] private float maxHeight = 0.58f;
    [Tooltip("Cantidad de capas apiladas para que se vea muy espesa")]
    [SerializeField] private int fogLayers = 14;
    [SerializeField] private float fogRadius = 18f;

    [Header("Color y Densidad")]
    [Tooltip("Color gris frío oscuro que reacciona de forma tétrica en la noche")]
    [SerializeField] private Color groundFogColor = new Color(0.11f, 0.14f, 0.18f, 0.24f);
    [SerializeField] private float windDriftSpeed = 0.6f;

    private Transform[] layerTransforms;
    private Material[] layerMaterials;
    private Texture2D proceduralMistTexture;

    private void Start()
    {
        proceduralMistTexture = GenerateSoftMistTexture(128);
        layerTransforms = new Transform[fogLayers];
        layerMaterials = new Material[fogLayers];

        GameObject fogRoot = new GameObject("ThickGroundFogLayers");

        for (int i = 0; i < fogLayers; i++)
        {
            float t = (float)i / Mathf.Max(1, fogLayers - 1);
            float height = Mathf.Lerp(minHeight, maxHeight, t);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"GroundFogSlice_{i}";
            quad.transform.SetParent(fogRoot.transform);
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(fogRadius * 2f, fogRadius * 2f, 1f);
            Destroy(quad.GetComponent<Collider>());

            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = proceduralMistTexture;

            // Las capas inferiores son aún más densas y la superior se difumina a la altura del cuello del goblin
            float layerAlpha = groundFogColor.a * Mathf.Lerp(1.25f, 0.55f, t);
            mat.color = new Color(groundFogColor.r, groundFogColor.g, groundFogColor.b, layerAlpha);

            Renderer rend = quad.GetComponent<Renderer>();
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            layerTransforms[i] = quad.transform;
            layerMaterials[i] = mat;
        }
    }

    private void LateUpdate()
    {
        if (layerTransforms == null) return;

        Vector3 playerPos = transform.position;

        for (int i = 0; i < fogLayers; i++)
        {
            float t = (float)i / Mathf.Max(1, fogLayers - 1);
            float height = Mathf.Lerp(minHeight, maxHeight, t);

            // Ligero movimiento ondulante de cada capa para simular que la niebla se arrastra con el viento
            float swayX = Mathf.Sin(Time.time * windDriftSpeed + i * 0.7f) * 1.2f;
            float swayZ = Mathf.Cos(Time.time * (windDriftSpeed * 0.8f) + i * 0.9f) * 1.2f;

            layerTransforms[i].position = new Vector3(playerPos.x + swayX, height, playerPos.z + swayZ);
            layerTransforms[i].rotation = Quaternion.Euler(90f, (Time.time * 3f * (i % 2 == 0 ? 1 : -1)) + i * 25f, 0f);
        }
    }

    // Genera por código una textura de nube con bordes suaves para no necesitar imágenes externas
    private Texture2D GenerateSoftMistTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float edgeFade = Mathf.Clamp01(1f - Mathf.Pow(dist, 1.8f));

                // Añadimos variación de nubes con PerlinNoise para que tenga textura de bruma
                float cloudNoise = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.45f + 0.55f;
                float finalAlpha = edgeFade * cloudNoise;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, finalAlpha));
            }
        }

        tex.Apply();
        return tex;
    }
}
