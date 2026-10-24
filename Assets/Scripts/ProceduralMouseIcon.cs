using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class ProceduralMouseIcon : MonoBehaviour
{
    [Header("Estado del Icono del Ratón")]
    [Tooltip("Pinta el Clic Izquierdo con el color resaltado (para indicar mantener presionado)")]
    [SerializeField] private bool highlightLeftClick = true;
    [Tooltip("Pinta la Rueda del Ratón con el color resaltado (para indicar ajustar fuerza/arco)")]
    [SerializeField] private bool highlightScrollWheel = true;
    [Tooltip("Pinta el Clic Derecho con el color resaltado (opcional)")]
    [SerializeField] private bool highlightRightClick = false;

    [Header("Colores del Vector")]
    [Tooltip("Color base del cuerpo del ratón (ej. blanco frío o gris claro para que contraste sobre fondo oscuro)")]
    [SerializeField] private Color baseColor = new Color(0.92f, 0.92f, 0.92f, 1f);
    [Tooltip("Color de resaltado para el Clic Izquierdo y la Rueda (ej. rojo terror o naranja)")]
    [SerializeField] private Color highlightColor = new Color(0.88f, 0.16f, 0.16f, 1f);
    [Tooltip("Si quieres que la rueda tenga un color distinto al del clic izquierdo, cámbialo aquí")]
    [SerializeField] private bool useSeparateWheelColor = false;
    [SerializeField] private Color customWheelColor = new Color(1f, 0.65f, 0.2f, 1f);

    [Header("Resolución y Proporciones")]
    [Range(128, 1024)]
    [SerializeField] private int textureSize = 512;

    private Image targetImage;
    private Texture2D generatedTexture;
    private Sprite generatedSprite;

    private void OnEnable()
    {
        GenerateMouseSprite();
    }

    private void OnValidate()
    {
        // Actualiza el icono en tiempo real cuando cambias colores o casillas en el Inspector
        if (isActiveAndEnabled)
        {
            GenerateMouseSprite();
        }
    }

    [ContextMenu("Regenerar Icono del Ratón")]
    public void GenerateMouseSprite()
    {
        targetImage = GetComponent<Image>();
        if (targetImage == null) return;

        int size = Mathf.Clamp(textureSize, 128, 1024);

        if (generatedTexture == null || generatedTexture.width != size)
        {
            generatedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            generatedTexture.wrapMode = TextureWrapMode.Clamp;
            generatedTexture.filterMode = FilterMode.Bilinear;
        }

        Color[] pixels = new Color[size * size];
        float aaWidth = 2.2f / size; // Suavizado de bordes (Anti-aliasing)

        // Parámetros geométricos idénticos a la silueta vectorial del ratón
        float capsuleHalfHeight = 0.33f;
        float outerRadius = 0.54f;
        float dividerY = 0.05f;
        float gapHalfThickness = 0.028f;

        float wheelMinY = 0.28f;
        float wheelMaxY = 0.50f;
        float wheelInnerRadius = 0.082f;
        float wheelCutoutRadius = 0.162f;

        Color leftBtnCol = highlightLeftClick ? highlightColor : baseColor;
        Color rightBtnCol = highlightRightClick ? highlightColor : baseColor;
        Color wheelCol = highlightScrollWheel
            ? (useSeparateWheelColor ? customWheelColor : highlightColor)
            : baseColor;

        for (int py = 0; py < size; py++)
        {
            // Coordenadas normalizadas [-1, 1]
            float y = ((py + 0.5f) / size) * 2f - 1f;

            for (int px = 0; px < size; px++)
            {
                float x = ((px + 0.5f) / size) * 2f - 1f;

                // 1. Distancia SDF a la cápsula exterior del ratón
                float clampedOuterY = Mathf.Clamp(y, -capsuleHalfHeight, capsuleHalfHeight);
                float distOuter = Vector2.Distance(new Vector2(x, y), new Vector2(0f, clampedOuterY)) - outerRadius;

                if (distOuter > aaWidth)
                {
                    pixels[py * size + px] = Color.clear;
                    continue;
                }

                // 2. Distancia SDF al eje de la rueda central
                float clampedWheelY = Mathf.Clamp(y, wheelMinY, wheelMaxY);
                float distToWheelAxis = Vector2.Distance(new Vector2(x, y), new Vector2(0f, clampedWheelY));

                // 3. Evaluamos en qué pieza del ratón cae este píxel
                float shapeSignedDist;
                Color partColor = baseColor;

                // Pieza A: La Rueda Central (cápsula interior)
                float distWheelInner = distToWheelAxis - wheelInnerRadius;
                if (distWheelInner < aaWidth && y > dividerY)
                {
                    shapeSignedDist = Mathf.Max(distOuter, distWheelInner);
                    partColor = wheelCol;
                }
                // Pieza B: El Cuerpo Inferior del ratón (debajo de la línea divisoria horizontal)
                else if (y < dividerY)
                {
                    float distBelowLine = y - (dividerY - gapHalfThickness);
                    shapeSignedDist = Mathf.Max(distOuter, distBelowLine);
                    partColor = baseColor;
                }
                // Pieza C: Botones Superiores (Clic Izquierdo y Clic Derecho)
                else
                {
                    // Hueco de la línea horizontal
                    float distAboveLine = (dividerY + gapHalfThickness) - y;
                    // Hueco de la ranura vertical central
                    float distVerticalSlot = gapHalfThickness - Mathf.Abs(x);
                    // Hueco del anillo que rodea la rueda
                    float distWheelCutout = wheelCutoutRadius - distToWheelAxis;

                    shapeSignedDist = Mathf.Max(distOuter, Mathf.Max(distAboveLine, Mathf.Max(distVerticalSlot, distWheelCutout)));
                    partColor = (x < 0f) ? leftBtnCol : rightBtnCol;
                }

                // Calculamos la opacidad con bordes suavizados (Anti-aliasing perfecto sin píxeles dentados)
                float alpha = Mathf.Clamp01(0.5f - (shapeSignedDist / aaWidth));
                pixels[py * size + px] = new Color(partColor.r, partColor.g, partColor.b, partColor.a * alpha);
            }
        }

        generatedTexture.SetPixels(pixels);
        generatedTexture.Apply();

        generatedSprite = Sprite.Create(
            generatedTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f
        );
        generatedSprite.name = "ProceduralMouseVector";

        targetImage.sprite = generatedSprite;
        targetImage.color = Color.white;
        targetImage.preserveAspect = true;
    }
}