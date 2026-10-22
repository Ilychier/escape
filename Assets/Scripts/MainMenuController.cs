using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [Header("Escena del Juego")]
    [Tooltip("Nombre exacto de la escena del laberinto")]
    [SerializeField] private string gameSceneName = "MainScene";

    private void Start()
    {
        // Asegura que en el menú el cursor siempre esté libre y visible
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Se conecta al botón Play
    public void PlayGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    // Se conecta al botón Quit
    public void QuitGame()
    {
        // Si estás probando dentro del Editor de Unity, detiene el Play
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        // Si es el juego compilado (.exe), cierra la aplicación
        Application.Quit();
        #endif
    }
}