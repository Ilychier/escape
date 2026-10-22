using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [Header("Referencias del Panel")]
    [Tooltip("Arrastra aquí el objeto hijo GameOverPanel")]
    [SerializeField] private GameObject gameOverPanel;

    [Header("Escena del Menú Principal")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private void Awake()
    {
        Instance = this;

        // Si la casilla quedó vacía en el Inspector, busca automáticamente su panel hijo
        if (gameOverPanel == null)
        {
            Transform panelChild = transform.Find("GameOverPanel");
            if (panelChild != null)
            {
                gameOverPanel = panelChild.gameObject;
            }
            else if (transform.childCount > 0)
            {
                gameOverPanel = transform.GetChild(0).gameObject;
            }
        }

        // Oculta el panel hijo al iniciar la partida
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
    }

    public void ShowGameOver()
    {
        // 1. Asegura que el Canvas padre esté activo
        gameObject.SetActive(true);

        // 2. Enciende el panel con el título y los botones
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        else if (transform.childCount > 0)
        {
            transform.GetChild(0).gameObject.SetActive(true);
        }

        // 3. Libera y muestra el cursor del ratón
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void OnRestartClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void OnMainMenuClicked()
    {
        if (!string.IsNullOrEmpty(mainMenuSceneName) && Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}