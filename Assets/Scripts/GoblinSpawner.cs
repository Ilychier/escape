using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoblinSpawner : MonoBehaviour
{
    [Header("Prefabs de tus Enemigos")]
    [SerializeField] private GameObject[] goblinPrefabs;

    [Header("Referencias")]
    [SerializeField] private Transform playerTransform;

    [Header("Distribución de Patrullas en el Laberinto (120x120m)")]
    [Tooltip("Segundos de gracia al inicio de la partida antes de desplegar el primer enemigo")]
    [SerializeField] private float initialSpawnDelay = 10f;
    [Tooltip("Cantidad total de enemigos patrullando los 4 cuadrantes del laberinto (4 = 1 por cuadrante)")]
    [SerializeField] private int maxActiveGoblins = 4;
    [Tooltip("Intervalo en segundos entre la aparición de cada enemigo hasta completar el cupo")]
    [SerializeField] private float spawnInterval = 6f;
    [Tooltip("Distancia mínima respecto al jugador al aparecer inicialmente")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 18f;

    [Header("Parámetros de IA y Búsqueda")]
    [Tooltip("Velocidad del enemigo cuando te persigue con línea de visión directa")]
    [SerializeField] private float goblinChaseSpeed = 2.85f;
    [Tooltip("Duración del aturdimiento al golpearlo con una roca")]
    [SerializeField] private float goblinStunDuration = 4.5f;
    [Tooltip("Segundos que el enemigo investiga los pasillos cercanos tras perderte de vista en una esquina")]
    [SerializeField] private float searchDurationAfterLosingSight = 7.5f;
    [SerializeField] private float goblinScale = 0.85f;
    [Range(0.05f, 1f)]
    [SerializeField] private float enemyBrightness = 0.25f;

    [Header("Encuadre y Duración del Jumpscare (Fondo Negro)")]
    [SerializeField] private float jumpscareCameraHeight = 1.45f;
    [SerializeField] private float jumpscareDistance = 1.75f;
    [SerializeField] private float jumpscareDuration = 3.0f;

    [Header("Sonidos de los Enemigos (.mp3)")]
    [SerializeField] private AudioClip goblinAmbientMp3;
    [SerializeField] private float goblinHearingRadius = 12f;
    [Range(0f, 1f)]
    [SerializeField] private float goblinAmbientVolume = 0.8f;

    [SerializeField] private AudioClip goblinCatchMp3;
    [Range(0f, 1f)]
    [SerializeField] private float goblinCatchVolume = 1.0f;

    [Header("Truco de Desarrollo (Desmarcar para el Juego Final)")]
    [SerializeField] private bool allowDebugCheatF2 = true;
    private bool spawningDisabledByCheat = false;

    private List<GoblinEnemy> activeGoblins = new List<GoblinEnemy>();
    private float spawnTimer;

    private void Start()
    {
        if (playerTransform == null && Camera.main != null)
        {
            playerTransform = Camera.main.transform.root;
        }

        spawnTimer = initialSpawnDelay;
    }

    private void Update()
    {
        // Truco de desarrollo con la tecla F2
        if (allowDebugCheatF2 && Input.GetKeyDown(KeyCode.F2))
        {
            spawningDisabledByCheat = !spawningDisabledByCheat;
            if (spawningDisabledByCheat)
            {
                ClearAllActiveGoblins();
            }
        }

        if (spawningDisabledByCheat) return;
        if (playerTransform == null || goblinPrefabs == null || goblinPrefabs.Length == 0) return;
        if (MazeGenerator.Instance == null) return;

        activeGoblins.RemoveAll(g => g == null);

        if (activeGoblins.Count < maxActiveGoblins)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                SpawnPatrolGoblinInSector(activeGoblins.Count);
                spawnTimer = spawnInterval;
            }
        }
    }

    private void SpawnPatrolGoblinInSector(int enemyIndex)
    {
        int assignedSector = enemyIndex % 4;
        Vector3 spawnPos = MazeGenerator.Instance.GetRandomPatrolPointInSector(
            assignedSector,
            playerTransform.position,
            minSpawnDistanceFromPlayer
        );

        GameObject chosenPrefab = goblinPrefabs[Random.Range(0, goblinPrefabs.Length)];
        if (chosenPrefab == null) return;

        GameObject goblinObj = Instantiate(chosenPrefab, spawnPos, Quaternion.identity, transform);
        goblinObj.name = $"Enemy_Sector_{assignedSector + 1}";
        goblinObj.transform.localScale = Vector3.one * goblinScale;

        GoblinEnemy enemyScript = goblinObj.GetComponent<GoblinEnemy>();
        if (enemyScript == null)
        {
            enemyScript = goblinObj.AddComponent<GoblinEnemy>();
        }

        enemyScript.Initialize(
            playerTransform,
            goblinChaseSpeed,
            goblinStunDuration,
            goblinAmbientMp3,
            goblinHearingRadius,
            goblinAmbientVolume,
            goblinCatchMp3,
            goblinCatchVolume,
            assignedSector,
            jumpscareCameraHeight,
            jumpscareDistance,
            jumpscareDuration,
            searchDurationAfterLosingSight,
            enemyBrightness
        );

        activeGoblins.Add(enemyScript);
    }

    public void ClearAllActiveGoblins()
    {
        for (int i = 0; i < activeGoblins.Count; i++)
        {
            if (activeGoblins[i] != null)
            {
                Destroy(activeGoblins[i].gameObject);
            }
        }
        activeGoblins.Clear();
    }

    private void OnGUI()
    {
        if (allowDebugCheatF2 && spawningDisabledByCheat)
        {
            GUI.color = Color.yellow;
            GUI.Label(new Rect(15f, 15f, 350f, 30f), "[TRUCO F2 ACTIVO] Enemigos Desactivados");
        }
    }
}