using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [Header("Paneles del Menú (Secuencia de Inicio)")]
    [Tooltip("1. Arrastra aquí tu Panel_Menu principal")]
    [SerializeField] private GameObject panelMenu;
    [Tooltip("2. Arrastra aquí tu Panel_Instructions (el tutorial de controles)")]
    [SerializeField] private GameObject panelInstructions;
    [Tooltip("3. Arrastra aquí tu nuevo panel con la frase en inglés ('STALK THEM BEFORE THEY STALK YOU')")]
    [SerializeField] private GameObject panelStalkWarning;

    [Header("Tiempos y Escena")]
    [Tooltip("Segundos que se muestra el panel de instrucciones")]
    [SerializeField] private float instructionsDuration = 5.0f;
    [Tooltip("Segundos que se muestra la frase de advertencia antes de entrar al laberinto")]
    [SerializeField] private float stalkWarningDuration = 3.0f;
    [Tooltip("Nombre exacto de la escena del laberinto en Build Settings")]
    [SerializeField] private string gameSceneName = "MainScene";

    private bool isTransitioning = false;

    private void Awake()
    {
        if (panelMenu == null)
        {
            Transform foundMenu = transform.Find("Panel_Menu");
            if (foundMenu != null) panelMenu = foundMenu.gameObject;
        }

        if (panelInstructions == null)
        {
            Transform foundInst = transform.Find("Panel_Instructions");
            if (foundInst != null) panelInstructions = foundInst.gameObject;
        }

        if (panelStalkWarning == null)
        {
            Transform foundWarn = transform.Find("Panel_Warning");
            if (foundWarn != null) panelStalkWarning = foundWarn.gameObject;
        }

        // Estado inicial al abrir el menú: solo el menú principal visible
        if (panelMenu != null) panelMenu.SetActive(true);
        if (panelInstructions != null) panelInstructions.SetActive(false);
        if (panelStalkWarning != null) panelStalkWarning.SetActive(false);
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Se conecta al botón Play
    public void PlayGame()
    {
        if (isTransitioning) return;
        StartCoroutine(StartGameSequenceRoutine());
    }

    private IEnumerator StartGameSequenceRoutine()
    {
        isTransitioning = true;

        // Fase 1: Ocultar Menú y mostrar Tutorial de Instrucciones
        if (panelMenu != null) panelMenu.SetActive(false);
        if (panelInstructions != null) panelInstructions.SetActive(true);
        if (panelStalkWarning != null) panelStalkWarning.SetActive(false);

        yield return new WaitForSeconds(instructionsDuration);

        // Fase 2: Ocultar Tutorial y mostrar el panel con la frase "STALK THEM BEFORE THEY STALK YOU"
        if (panelInstructions != null) panelInstructions.SetActive(false);
        if (panelStalkWarning != null)
        {
            panelStalkWarning.SetActive(true);
            yield return new WaitForSeconds(stalkWarningDuration);
        }

        // Fase 3: Cargar la escena del laberinto
        SceneManager.LoadScene(gameSceneName);
    }

    // Se conecta al botón Quit
    public void QuitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}