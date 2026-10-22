using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MenuBackdropGenerator : MonoBehaviour
{
    [Header("Tus Assets")]
    [Tooltip("Arrastra aquí tu prefab de la cabaña")]
    [SerializeField] private GameObject cabinPrefab;

    [Tooltip("Arrastra aquí todos tus prefabs de árboles invernales y pinos")]
    [SerializeField] private GameObject[] treePrefabs;

    [Tooltip("Arrastra aquí tus prefabs de rocas y troncos nevados")]
    [SerializeField] private GameObject[] rockPrefabs;

    [Header("Posición de la Cabaña")]
    [SerializeField] private Vector3 cabinLocalPosition = new Vector3(0f, 0f, 10f);
    [SerializeField] private float cabinRotationY = 170f;
    [SerializeField] private float cabinScale = 1.0f;

    [Header("Densidad del Bosque de Fondo")]
    [Range(15, 80)]
    [SerializeField] private int treeCount = 45;
    [Range(5, 40)]
    [SerializeField] private int rockCount = 20;

    [Tooltip("Ancho del bosque (eje X) a los lados de la cámara")]
    [SerializeField] private float areaWidth = 32f;
    [Tooltip("Profundidad del bosque (eje Z)")]
    [SerializeField] private float areaDepth = 24f;

    [Header("Variación de Tamaño")]
    [SerializeField] private Vector2 treeScaleRange = new Vector2(0.8f, 1.4f);
    [SerializeField] private Vector2 rockScaleRange = new Vector2(0.7f, 1.3f);

    [Header("Iluminación Focal de la Cabaña")]
    [Tooltip("Añade una luz tenue que hace resaltar la cabaña en medio del bosque oscuro")]
    [SerializeField] private bool addCabinSpotlight = true;
    [SerializeField] private Color cabinLightColor = new Color(1f, 0.75f, 0.45f, 1f);

    // Botón ejecutable con clic derecho en el Inspector o al pulsar Play
    [ContextMenu("Generar Bosque de Fondo")]
    public void GenerateBackdrop()
    {
        ClearBackdrop();

        GameObject root = new GameObject("_GeneratedBackdrop");
        root.transform.SetParent(transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        // 1. Suelo blanco mate para que el bosque tenga base
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "MenuSnowFloor";
        floor.transform.SetParent(root.transform);
        floor.transform.localPosition = new Vector3(0f, -0.05f, cabinLocalPosition.z + (areaDepth * 0.25f));
        floor.transform.localScale = new Vector3(areaWidth * 0.15f, 1f, areaDepth * 0.15f);
        Material snowMat = new Material(Shader.Find("Standard"));
        snowMat.color = new Color(0.85f, 0.9f, 0.95f, 1f);
        snowMat.SetFloat("_Smoothness", 0.1f);
        floor.GetComponent<Renderer>().material = snowMat;

        // 2. Colocar la cabaña protagonista
        if (cabinPrefab != null)
        {
            GameObject cabinObj = Instantiate(cabinPrefab, root.transform);
            cabinObj.name = "MenuCabin";
            cabinObj.transform.localPosition = cabinLocalPosition;
            cabinObj.transform.localRotation = Quaternion.Euler(0f, cabinRotationY, 0f);
            cabinObj.transform.localScale = cabinPrefab.transform.localScale * cabinScale;

            if (addCabinSpotlight)
            {
                GameObject lightObj = new GameObject("CabinWarmLight");
                lightObj.transform.SetParent(cabinObj.transform);
                lightObj.transform.localPosition = new Vector3(0f, 2.8f, -1.8f);
                Light l = lightObj.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = cabinLightColor;
                l.range = 8.5f;
                l.intensity = 2.2f;
            }
        }

        // 3. Generar los árboles con encuadre de cámara (laterales y fondo profundo)
        if (treePrefabs != null && treePrefabs.Length > 0)
        {
            for (int i = 0; i < treeCount; i++)
            {
                GameObject randomTree = treePrefabs[Random.Range(0, treePrefabs.Length)];
                if (randomTree == null) continue;

                Vector3 pos = CalculateNaturalPosition(true);
                GameObject treeObj = Instantiate(randomTree, root.transform);
                treeObj.name = $"Tree_{i + 1}";
                treeObj.transform.localPosition = pos;
                treeObj.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                float scale = Random.Range(treeScaleRange.x, treeScaleRange.y);
                treeObj.transform.localScale = randomTree.transform.localScale * scale;
            }
        }

        // 4. Generar rocas y troncos en el suelo
        if (rockPrefabs != null && rockPrefabs.Length > 0)
        {
            for (int i = 0; i < rockCount; i++)
            {
                GameObject randomRock = rockPrefabs[Random.Range(0, rockPrefabs.Length)];
                if (randomRock == null) continue;

                Vector3 pos = CalculateNaturalPosition(false);
                GameObject rockObj = Instantiate(randomRock, root.transform);
                rockObj.name = $"Rock_{i + 1}";
                rockObj.transform.localPosition = pos;
                rockObj.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                float scale = Random.Range(rockScaleRange.x, rockScaleRange.y);
                rockObj.transform.localScale = randomRock.transform.localScale * scale;
            }
        }
    }

    [ContextMenu("Borrar Bosque")]
    public void ClearBackdrop()
    {
        Transform existing = transform.Find("_GeneratedBackdrop");
        if (existing != null)
        {
            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }
    }

    private Vector3 CalculateNaturalPosition(bool isTree)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float x = Random.Range(-areaWidth * 0.5f, areaWidth * 0.5f);
            float z = Random.Range(4.5f, cabinLocalPosition.z + areaDepth);

            // Dejamos libre el pasillo central directo hacia la cabaña para que la cámara la vea de frente
            bool inCameraViewCorridor = Mathf.Abs(x) < 2.8f && z < (cabinLocalPosition.z - 0.5f);
            if (inCameraViewCorridor) continue;

            // Evitamos encimar árboles directamente dentro de las paredes de la cabaña
            float distToCabin = Vector2.Distance(new Vector2(x, z), new Vector2(cabinLocalPosition.x, cabinLocalPosition.z));
            if (distToCabin < 2.5f) continue;

            return new Vector3(x, 0f, z);
        }

        return new Vector3(Random.Range(-areaWidth * 0.5f, areaWidth * 0.5f), 0f, Random.Range(6f, areaDepth));
    }
}