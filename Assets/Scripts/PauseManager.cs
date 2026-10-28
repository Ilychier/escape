using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PauseManager : MonoBehaviour
{
    [Header("Panel de pausa")]
    [SerializeField] private GameObject pausePanel;

    [Header("Música del menú de pausa")]
    [SerializeField] private AudioSource pauseMusic;

    // Propiedad estática para que cualquier otro script consulte si el juego está en pausa
    public static bool IsGamePaused { get; private set; } = false;

    void Awake()
    {
        IsGamePaused = false;
    }

    void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (pauseMusic != null)
        {
            pauseMusic.ignoreListenerPause = true;
            pauseMusic.playOnAwake = false;
            pauseMusic.Stop();
        }
    }

    void Update()
    {
        if (MazeGenerator.win)
            return;

        bool pausePressed = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);

#if UNITY_EDITOR
        // En el editor, la primera pulsación de Escape desbloquea el cursor sin disparar GetKeyDown.
        // Si el cursor se desbloqueó externamente mientras el juego sigue activo, pausamos de inmediato.
        if (!IsGamePaused && Cursor.lockState == CursorLockMode.None && Input.GetKey(KeyCode.Escape))
        {
            pausePressed = true;
        }
#endif

        if (pausePressed)
        {
            if (IsGamePaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    public void PauseGame()
    {
        if (IsGamePaused)
            return;

        IsGamePaused = true;

        // 1. Congelar físicas
        Time.timeScale = 0f;

        // 2. Activar panel primero
        if (pausePanel != null)
            pausePanel.SetActive(true);

        // 3. Configurar y arrancar la música ANTES de pausar el listener global
        if (pauseMusic != null)
        {
            pauseMusic.ignoreListenerPause = true;
            if (!pauseMusic.isPlaying)
                pauseMusic.Play();
        }

        // 4. Pausar los demás sonidos del juego
        AudioListener.pause = true;

        // 5. Cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ResumeGame()
    {
        if (!IsGamePaused)
            return;

        IsGamePaused = false;

        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        // 1. Detener música del menú
        if (pauseMusic != null)
            pauseMusic.Stop();

        // 2. Reactivar audio general
        AudioListener.pause = false;

        // 3. Reanudar tiempo
        Time.timeScale = 1f;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        // 4. Bloquear y ocultar cursor nuevamente
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        if (IsGamePaused)
        {
            AudioListener.pause = false;
            Time.timeScale = 1f;
            IsGamePaused = false;

            if (pauseMusic != null)
                pauseMusic.Stop();
        }
    }
}