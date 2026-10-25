using System.Collections.Generic;
using UnityEngine;

public class EnemyProximityArcIndicator : MonoBehaviour
{
    [Header("Detección de Monstruos Cercanos")]
    [Tooltip("Distancia en metros a partir de la cual empieza a aparecer el arco indicador")]
    [SerializeField] private float detectionRadius = 14f;
    [Tooltip("Si está activo, muestra un arco por cada monstruo cercano; si lo desmarcas, muestra solo el del monstruo más cercano")]
    [SerializeField] private bool showAllNearbyEnemies = true;

    [Header("Apariencia del Arco en Pantalla")]
    [Tooltip("Color del arco direccional (puedes cambiarlo libremente desde el Inspector)")]
    [SerializeField] private Color arcColor = new Color(0.95f, 0.15f, 0.15f, 0.9f);
    [Tooltip("Tamaño del anillo alrededor del centro de la pantalla (en píxeles)")]
    [SerializeField] private float indicatorSize = 190f;
    [Tooltip("Apertura del arco en grados (ej. 65 grados forma una media luna pequeña)")]
    [Range(25f, 120f)]
    [SerializeField] private float arcAngleSpan = 65f;
    [Tooltip("Grosor del arco")]
    [Range(0.03f, 0.18f)]
    [SerializeField] private float arcThickness = 0.08f;

    private Texture2D arcTexture;
    private float cachedArcSpan = -1f;
    private float cachedThickness = -1f;

    private void Start()
    {
        RebuildArcTexture();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            RebuildArcTexture();
        }
    }

    private void RebuildArcTexture()
    {
        int size = 256;
        if (arcTexture == null)
        {
            arcTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            arcTexture.wrapMode = TextureWrapMode.Clamp;
            arcTexture.filterMode = FilterMode.Bilinear;
        }

        cachedArcSpan = arcAngleSpan;
        cachedThickness = arcThickness;

        float halfAngle = arcAngleSpan * 0.5f;
        float targetRadius = 0.78f;
        float halfThick = arcThickness * 0.5f;

        Color[] pixels = new Color[size * size];

        for (int py = 0; py < size; py++)
        {
            float y = ((py + 0.5f) / size) * 2f - 1f;

            for (int px = 0; px < size; px++)
            {
                float x = ((px + 0.5f) / size) * 2f - 1f;

                float r = Mathf.Sqrt(x * x + y * y);
                float distToRing = Mathf.Abs(r - targetRadius);

                if (distToRing > halfThick)
                {
                    pixels[py * size + px] = Color.clear;
                    continue;
                }

                float angleFromTop = Mathf.Abs(Mathf.Atan2(x, y) * Mathf.Rad2Deg);
                if (angleFromTop > halfAngle)
                {
                    pixels[py * size + px] = Color.clear;
                    continue;
                }

                float radialAlpha = 1f - Mathf.Pow(distToRing / halfThick, 1.6f);
                float angularAlpha = 1f - Mathf.Pow(angleFromTop / halfAngle, 2.2f);
                float finalAlpha = Mathf.Clamp01(radialAlpha * angularAlpha);

                pixels[py * size + px] = new Color(1f, 1f, 1f, finalAlpha);
            }
        }

        arcTexture.SetPixels(pixels);
        arcTexture.Apply();
    }

    private void OnGUI()
    {
        if (Time.timeSinceLevelLoad < 0.2f) return;

        // Usamos la lista estática directa sin llamar jamás a FindObjectsOfType
        List<GoblinEnemy> enemies = GoblinEnemy.AllActiveEnemies;
        if (enemies == null || enemies.Count == 0) return;

        if (arcTexture == null || !Mathf.Approximately(cachedArcSpan, arcAngleSpan) || !Mathf.Approximately(cachedThickness, arcThickness))
        {
            RebuildArcTexture();
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Rect drawRect = new Rect(
            screenCenter.x - indicatorSize * 0.5f,
            screenCenter.y - indicatorSize * 0.5f,
            indicatorSize,
            indicatorSize
        );

        Matrix4x4 originalMatrix = GUI.matrix;
        Color originalColor = GUI.color;

        if (showAllNearbyEnemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                GoblinEnemy enemy = enemies[i];
                if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;

                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist <= detectionRadius)
                {
                    DrawDirectionalArcForEnemy(enemy.transform.position, dist, screenCenter, drawRect);
                }
            }
        }
        else
        {
            GoblinEnemy closest = null;
            float minDist = detectionRadius;

            for (int i = 0; i < enemies.Count; i++)
            {
                GoblinEnemy enemy = enemies[i];
                if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;

                float d = Vector3.Distance(transform.position, enemy.transform.position);
                if (d <= minDist)
                {
                    minDist = d;
                    closest = enemy;
                }
            }

            if (closest != null)
            {
                DrawDirectionalArcForEnemy(closest.transform.position, minDist, screenCenter, drawRect);
            }
        }

        GUI.matrix = originalMatrix;
        GUI.color = originalColor;
    }

    private void DrawDirectionalArcForEnemy(Vector3 enemyWorldPos, float distance, Vector2 screenCenter, Rect drawRect)
    {
        Vector3 toEnemy = enemyWorldPos - transform.position;
        toEnemy.y = 0f;
        if (toEnemy.sqrMagnitude < 0.001f) return;

        Vector3 playerForward = transform.forward;
        playerForward.y = 0f;

        float signedAngle = Vector3.SignedAngle(playerForward.normalized, toEnemy.normalized, Vector3.up);

        float proximityFactor = Mathf.Clamp01(1f - (distance / detectionRadius));
        float pulse = 0.85f + Mathf.Sin(Time.time * 7f) * 0.15f;
        float finalAlpha = Mathf.Clamp01(Mathf.Lerp(0.2f, 1f, proximityFactor) * arcColor.a * pulse);

        GUI.matrix = Matrix4x4.identity;
        GUIUtility.RotateAroundPivot(signedAngle, screenCenter);

        GUI.color = new Color(arcColor.r, arcColor.g, arcColor.b, finalAlpha);
        GUI.DrawTexture(drawRect, arcTexture);
    }
}