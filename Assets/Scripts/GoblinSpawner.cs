using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoblinSpawner : MonoBehaviour
{
    [Header("Prefabs de tu Low Poly Goblin Pack")]
    [SerializeField] private GameObject[] goblinPrefabs;

    [Header("Referencias")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float cellSize = 4f;
    [SerializeField] private int mazeWidth = 15;
    [SerializeField] private int mazeDepth = 15;

    [Header("Distancias y Tiempos de Aparición")]
    [SerializeField] private int maxActiveGoblins = 6;
    [SerializeField] private float spawnInterval = 4f;
    [SerializeField] private float minSpawnDistance = 10f;
    [SerializeField] private float maxSpawnDistance = 22f;
    [SerializeField] private float relocateDistance = 30f;

    [Header("Ajustes de los Goblins")]
    [SerializeField] private float goblinSpeed = 2.1f;
    [SerializeField] private float goblinStunDuration = 4.5f;
    [SerializeField] private float goblinScale = 0.85f;

    [Header("Sonidos de los Goblins (.mp3)")]
    [Tooltip("Arrastra aquí el .mp3 que hacen los goblins al caminar/acechar")]
    [SerializeField] private AudioClip goblinAmbientMp3;
    [Tooltip("Radio en metros desde el que se empieza a escuchar al goblin")]
    [SerializeField] private float goblinHearingRadius = 11f;
    [Range(0f, 1f)]
    [SerializeField] private float goblinAmbientVolume = 0.8f;

    [Tooltip("Arrastra aquí el .mp3 de susto cuando el goblin te atrapa")]
    [SerializeField] private AudioClip goblinCatchMp3;
    [Range(0f, 1f)]
    [SerializeField] private float goblinCatchVolume = 1.0f;

    [Header("Truco de Desarrollo (Desmarcar para el Juego Final)")]
    [Tooltip("Si está activo, pulsar F2 elimina a los Goblins y detiene su aparición")]
    [SerializeField] private bool allowDebugCheatF2 = true;
    private bool spawningDisabledByCheat = false;

    private List<GoblinEnemy> activeGoblins = new List<GoblinEnemy>();
    private float spawnTimer = 2.5f;

    private void Start()
    {
        if (playerTransform == null && Camera.main != null)
        {
            playerTransform = Camera.main.transform.root;
        }
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

        if (activeGoblins.Count < maxActiveGoblins)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                SpawnGoblinAroundPlayer();
                spawnTimer = spawnInterval;
            }
        }

        for (int i = 0; i < activeGoblins.Count; i++)
        {
            GoblinEnemy goblin = activeGoblins[i];
            if (goblin == null || goblin.IsStunned) continue;

            float dist = Vector3.Distance(playerTransform.position, goblin.transform.position);
            if (dist > relocateDistance && TryFindValidCellAroundPlayer(out Vector3 newCellPos))
            {
                goblin.transform.position = newCellPos;
            }
        }
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

    private void SpawnGoblinAroundPlayer()
    {
        if (!TryFindValidCellAroundPlayer(out Vector3 spawnPos)) return;

        GameObject chosenPrefab = goblinPrefabs[Random.Range(0, goblinPrefabs.Length)];
        if (chosenPrefab == null) return;

        GameObject goblinObj = Instantiate(chosenPrefab, spawnPos, Quaternion.identity, transform);
        goblinObj.name = $"GoblinHunter_{activeGoblins.Count + 1}";
        goblinObj.transform.localScale = Vector3.one * goblinScale;

        GoblinEnemy enemyScript = goblinObj.GetComponent<GoblinEnemy>();
        if (enemyScript == null)
        {
            enemyScript = goblinObj.AddComponent<GoblinEnemy>();
        }

        enemyScript.Initialize(
            playerTransform,
            goblinSpeed,
            goblinStunDuration,
            goblinAmbientMp3,
            goblinHearingRadius,
            goblinAmbientVolume,
            goblinCatchMp3,
            goblinCatchVolume
        );
        activeGoblins.Add(enemyScript);
    }

    private bool TryFindValidCellAroundPlayer(out Vector3 validPosition)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int randomX = Random.Range(0, mazeWidth);
            int randomZ = Random.Range(0, mazeDepth);

            Vector3 candidatePos = new Vector3(randomX * cellSize, 0f, randomZ * cellSize);
            float distToPlayer = Vector3.Distance(playerTransform.position, candidatePos);

            if (distToPlayer >= minSpawnDistance && distToPlayer <= maxSpawnDistance)
            {
                validPosition = candidatePos;
                return true;
            }
        }

        validPosition = Vector3.zero;
        return false;
    }

    private void OnGUI()
    {
        if (allowDebugCheatF2 && spawningDisabledByCheat)
        {
            GUI.color = Color.yellow;
            GUI.Label(new Rect(15f, 15f, 350f, 30f), "[TRUCO F2 ACTIVO] Goblins Desactivados");
        }
    }
}