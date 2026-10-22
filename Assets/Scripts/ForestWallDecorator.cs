using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ForestWallDecorator : MonoBehaviour
{
    [Header("Tus Assets del Bosque Invernal")]
    [SerializeField] private GameObject[] treePrefabs;
    [SerializeField] private GameObject[] rockPrefabs;

    [Header("Configuración del Tramo de Pared")]
    [SerializeField] private float wallLength = 4f;
    [SerializeField] private int treesPerWall = 3;
    [SerializeField] private int rocksPerWall = 2;

    [Header("Variación Orgánica")]
    [SerializeField] private float positionJitter = 0.45f;
    [SerializeField] private Vector2 treeScaleRange = new Vector2(0.85f, 1.25f);
    [SerializeField] private Vector2 rockScaleRange = new Vector2(0.75f, 1.30f);

    [Header("Apariencia de Límite del Mapa")]
    [Tooltip("Color ultra oscuro que absorbe la linterna en los bordes del mapa")]
    [SerializeField] private Color borderTint = new Color(0.03f, 0.04f, 0.06f, 1f);

    private bool isBorderWall = false;
    private bool isPopulated = false;

    private void Start()
    {
        if (!isPopulated)
        {
            PopulateWall();
        }
    }

    public void SetAsBorderWall()
    {
        isBorderWall = true;
        if (!isPopulated)
        {
            PopulateWall();
        }
    }

    private void PopulateWall()
    {
        isPopulated = true;

        // Si es un borde del mapa, duplicamos la densidad de árboles para que sea una muralla cerrada
        int finalTrees = isBorderWall ? treesPerWall * 2 : treesPerWall;
        int finalRocks = isBorderWall ? rocksPerWall * 2 : rocksPerWall;
        Vector2 finalTreeScale = isBorderWall ? treeScaleRange * 1.45f : treeScaleRange;

        if (treePrefabs != null && treePrefabs.Length > 0)
        {
            float step = wallLength / Mathf.Max(1, finalTrees);
            float startX = -wallLength / 2f + (step / 2f);

            for (int i = 0; i < finalTrees; i++)
            {
                GameObject randomTree = treePrefabs[Random.Range(0, treePrefabs.Length)];
                if (randomTree == null) continue;

                float localX = startX + (i * step) + Random.Range(-positionJitter, positionJitter);
                float localZ = Random.Range(-positionJitter, positionJitter);

                SpawnElement(randomTree, new Vector3(localX, 0f, localZ), finalTreeScale);
            }
        }

        if (rockPrefabs != null && rockPrefabs.Length > 0)
        {
            for (int i = 0; i < finalRocks; i++)
            {
                GameObject randomRock = rockPrefabs[Random.Range(0, rockPrefabs.Length)];
                if (randomRock == null) continue;

                float localX = Random.Range(-wallLength / 2f, wallLength / 2f);
                float localZ = Random.Range(-positionJitter, positionJitter);

                SpawnElement(randomRock, new Vector3(localX, 0f, localZ), rockScaleRange);
            }
        }

        // Si es pared límite del mapa, creamos un fondo negro mate detrás de los árboles para que no pase ni un rayo de luz
        if (isBorderWall)
        {
            CreateDarkAbyssBackdrop();
        }
    }

    private void SpawnElement(GameObject prefab, Vector3 localPos, Vector2 scaleRange)
    {
        GameObject instance = Instantiate(prefab, transform);
        instance.transform.localPosition = localPos;
        instance.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        float randomScale = Random.Range(scaleRange.x, scaleRange.y);
        instance.transform.localScale = prefab.transform.localScale * randomScale;

        // Collider[] childColliders = instance.GetComponentsInChildren<Collider>();
        // foreach (Collider col in childColliders)
        // {
            // col.enabled = false;
        // }

        // Si pertenece al borde exterior del mapa, oscurecemos todos sus materiales para que absorban la luz
        if (isBorderWall)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                foreach (Material mat in rend.materials)
                {
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", borderTint);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", borderTint);
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 0f);
                    if (mat.HasProperty("_Glossiness"))
                        mat.SetFloat("_Glossiness", 0f);
                }
            }
        }
    }

    private void CreateDarkAbyssBackdrop()
    {
        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "DarkAbyssBarrier";
        backdrop.transform.SetParent(transform);
        backdrop.transform.localPosition = new Vector3(0f, 5f, 0f);
        backdrop.transform.localRotation = Quaternion.identity;
        backdrop.transform.localScale = new Vector3(wallLength + 0.6f, 10f, 0.25f);

        Destroy(backdrop.GetComponent<Collider>());

        Renderer rend = backdrop.GetComponent<Renderer>();
        Material blackMat = new Material(Shader.Find("Sprites/Default"));
        blackMat.color = new Color(0.01f, 0.015f, 0.025f, 1f);
        rend.material = blackMat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
    }
}