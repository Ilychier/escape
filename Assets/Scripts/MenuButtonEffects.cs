using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class MenuButtonEffects : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Animación de Escala (Hover)")]
    [Tooltip("Tamaño que alcanza el texto al pasar el ratón (1.15 = 15% más grande)")]
    [SerializeField] private float hoverScale = 1.15f;
    [SerializeField] private float animationSpeed = 12f;

    [Header("Colores del Texto")]
    [SerializeField] private Color normalColor = new Color(0.85f, 0.12f, 0.12f, 1f); // Rojo terror base
    [SerializeField] private Color hoverColor = new Color(1f, 0.25f, 0.25f, 1f);     // Rojo más claro al flotar
    [SerializeField] private Color pressedColor = new Color(1f, 0.85f, 0.3f, 1f);   // Destello amarillo/blanco al dar clic

    private TextMeshProUGUI tmpText;
    private Text legacyText;
    private float targetScale = 1.0f;
    private Color targetColor;

    private void Awake()
    {
        // 1. Buscamos el texto dentro del botón
        tmpText = GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText == null)
        {
            legacyText = GetComponentInChildren<Text>();
        }

        // 2. Quitamos el recuadro visual del botón pero dejamos su área de clic invisible
        Image btnImage = GetComponent<Image>();
        if (btnImage != null)
        {
            btnImage.color = Color.clear; // 100% transparente
            btnImage.raycastTarget = true; // Permite detectar el ratón en toda la zona
        }

        // 3. Desactivamos la transición por defecto de Unity para que no pinte fondos
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.transition = Selectable.Transition.None;
        }

        targetColor = normalColor;
        ApplyTextColor(normalColor);
    }

    private void Update()
    {
        // Animación suave de aumento y reducción de tamaño
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            Vector3.one * targetScale,
            Time.unscaledDeltaTime * animationSpeed
        );

        // Transición suave de color del texto
        if (tmpText != null)
        {
            tmpText.color = Color.Lerp(tmpText.color, targetColor, Time.unscaledDeltaTime * animationSpeed);
        }
        else if (legacyText != null)
        {
            legacyText.color = Color.Lerp(legacyText.color, targetColor, Time.unscaledDeltaTime * animationSpeed);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = hoverScale;
        targetColor = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = 1.0f;
        targetColor = normalColor;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        targetColor = pressedColor; // Cambia de color exactamente al hacer clic
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        targetColor = hoverColor;
    }

    private void ApplyTextColor(Color col)
    {
        if (tmpText != null) tmpText.color = col;
        if (legacyText != null) legacyText.color = col;
    }
}