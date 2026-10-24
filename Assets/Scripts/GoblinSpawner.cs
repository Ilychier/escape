using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoblinSpawner : MonoBehaviour
{
    [Header("Prefabs de tus Enemigos")]
    [SerializeField] private GameObject[] goblinPrefabs;

    [Header("Referencias")]
    [SerializeField] private Transform playerTransform;

    [Header("Spawneo por Cuartos del Mapa (4 Segmentos)")]
    [Tooltip("Segundos tras entrar a un segmento para que spawnee el monstruo frontal (que viene desde adelante hacia ti)")]
    [SerializeField] private float frontEnemySpawnDelay = 3.0f;
    [Tooltip("Segundos adicionales después de entrar al segmento para que spawnee el segundo monstruo a tu espalda")]
    [SerializeField] private float rearEnemySpawnDelay = 7.5f;
    [Tooltip("Si lo marcas, en el primer cuarto (segmento 0) solo sale 1 monstruo; si lo desmarcas, salen los 2 (frente y espalda) desde el primer cuarto")]
    [SerializeField] private bool onlyOneEnemyInFirstQuarter = false;

    [Header("Ajustes de los Enemigos y Acecho")]
    [SerializeField] private float goblinSpeed = 2.1f;
    [SerializeField] private float goblinStunDuration = 4.5f;
    [SerializeField] private float goblinScale = 0.85f;
    [Tooltip("Segundos que cada monstruo acecha (por delante o por detrás) antes de lanzarse a cazarte")]
    [SerializeField] private float stalkDuration = 8f;
    [Tooltip("Brillo del material del enemigo (1 = normal, 0.25 = muy oscuro)")]
    [Range(0.05f, 1f)]
    [SerializeField] private float enemyBrightness = 0.25f;

    [Header("Encuadre y Duración del Jumpscare (Fondo Negro)")]
    [SerializeField] private float jumpscareCameraHeight = 1.45f;
    [SerializeField] private float jumpscareDistance = 1.75f;
    [SerializeField] private float jumpscareDuration = 3.0f;

    [Header("Sonidos de los Enemigos (.mp3)")]
    [SerializeField] private AudioClip goblinAmbientMp3;
    [SerializeField] private float goblinHearingRadius = 11f;
    [Range(0f, 1f)]
    [SerializeField] private float goblinAmbientVolume = 0.8f;

    [SerializeField] private AudioClip goblinCatchMp3;
    [Range(0f, 1f)]
    [SerializeField] private float goblinCatchVolume = 1.0f;

    [Header("Truco de Desarrollo (Desmarcar para el Juego Final)")]
    [SerializeField] private bool allowDebugCheatF2 = true;
    private bool spawningDisabledByCheat = false;

    private List<GoblinEnemy> activeGoblins = new List<GoblinEnemy>();

    private int currentSegment = -1;
    private float timeInCurrentSegment = 0f;
    private bool frontSpawnedInSegment = false;
    private bool rearSpawnedInSegment = false;

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
        if (MazeGenerator.Instance == null) return;

        // Limpiamos referencias nulas de enemigos que ya hicieron despawn
        activeGoblins.RemoveAll(g => g == null);

        // 1. Detectamos en qué cuarto del mapa (0, 1, 2 o 3) está el jugador ahora mismo
        int playerQuarter = MazeGenerator.Instance.GetPlayerQuarterIndex(playerTransform.position);

        if (playerQuarter != currentSegment)
        {
            OnPlayerChangedSegment(playerQuarter);
        }

        // 2. Si el jugador está dentro de la Zona Segura (Street Light central), pausamos el temporizador de spawneo
        if (MazeGenerator.Instance.IsPlayerInSafeZone())
        {
            return;
        }

        // 3. Avanzamos el reloj del segmento actual y spawneamos el enemigo frontal y el trasero a sus segundos correspondientes
        timeInCurrentSegment += Time.deltaTime;

        if (!frontSpawnedInSegment && timeInCurrentSegment >= frontEnemySpawnDelay)
        {
            SpawnSegmentEnemy(GoblinEnemy.StalkDirectionRole.FrontApproacher, currentSegment);
            frontSpawnedInSegment = true;
        }

        bool allowSecondEnemy = !(onlyOneEnemyInFirstQuarter && currentSegment == 0);
        if (allowSecondEnemy && !rearSpawnedInSegment && timeInCurrentSegment >= rearEnemySpawnDelay)
        {
            SpawnSegmentEnemy(GoblinEnemy.StalkDirectionRole.RearStalker, currentSegment);
            rearSpawnedInSegment = true;
        }
    }

    private void OnPlayerChangedSegment(int newQuarter)
    {
        currentSegment = newQuarter;
        timeInCurrentSegment = 0f;
        frontSpawnedInSegment = false;
        rearSpawnedInSegment = false;

        // Todos los enemigos de segmentos anteriores dejan de perseguirte (se quedan quietos y hacen despawn cuando no los miras)
        for (int i = 0; i < activeGoblins.Count; i++)
        {
            GoblinEnemy goblin = activeGoblins[i];
            if (goblin != null && goblin.AssignedQuarter != newQuarter)
            {
                goblin.MarkAsOutOfSegment();
            }
        }
    }

    private void SpawnSegmentEnemy(GoblinEnemy.StalkDirectionRole role, int quarterIndex)
    {
        Vector3 spawnPos;

        if (role == GoblinEnemy.StalkDirectionRole.FrontApproacher)
        {
            // Spawnea más adelante en el mapa (hacia la mitad/salida) y se dirige hacia el jugador
            spawnPos = MazeGenerator.Instance.GetSpawnPointAheadOfPlayer(playerTransform.position, 5, 9);
        }
        else
        {
            // Spawnea unos segundos después a la espalda del jugador en el camino recorrido
            spawnPos = MazeGenerator.Instance.GetSpawnPointBehindPlayer(playerTransform.position, 4, 7);
        }

        GameObject chosenPrefab = goblinPrefabs[Random.Range(0, goblinPrefabs.Length)];
        if (chosenPrefab == null) return;

        GameObject goblinObj = Instantiate(chosenPrefab, spawnPos, Quaternion.identity, transform);
        goblinObj.name = $"Enemy_Q{quarterIndex + 1}_{role}";
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
            goblinCatchVolume,
            quarterIndex,
            role,
            jumpscareCameraHeight,
            jumpscareDistance,
            jumpscareDuration,
            stalkDuration,
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