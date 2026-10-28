using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoblinSpawner : MonoBehaviour
{
    [Header("Prefabs de tus Enemigos")]
    [SerializeField] private GameObject[] goblinPrefabs;

    [Header("Referencias")]
    [SerializeField] private Transform playerTransform;

    [Header("Distribución de Patrullas Cercanas (4 Roles Pac-Man)")]
    [Tooltip("Segundos de gracia al inicio de la partida antes de desplegar el primer enemigo")]
    [SerializeField] private float initialSpawnDelay = 1.5f;
    [Tooltip("4 enemigos activos (0: Emboscador, 1: Acechador, 2: Centinela, 3: Merodeador Acústico)")]
    [SerializeField] private int maxActiveGoblins = 4;
    [Tooltip("Intervalo en segundos entre la aparición de cada enemigo")]
    [SerializeField] private float spawnInterval = 2.0f;
    [Tooltip("Distancia mínima respecto al jugador al aparecer")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 10f;
    [Tooltip("Distancia máxima respecto al jugador al aparecer o reubicarse")]
    [SerializeField] private float maxSpawnDistanceFromPlayer = 18f;
    [Tooltip("Si un enemigo se queda atrás a más de esta distancia, se reubica adelante fuera de vista")]
    [SerializeField] private float maxPatrolLeashDistance = 22f;

    [Header("Parámetros de IA y Búsqueda")]
    [SerializeField] private float goblinChaseSpeed = 2.9f;
    [SerializeField] private float goblinStunDuration = 4.5f;
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
        GoblinEnemy.ResetDeathState();

        if (playerTransform == null && Camera.main != null)
        {
            playerTransform = Camera.main.transform.root;
        }

        spawnTimer = initialSpawnDelay;
    }

    private void Update()
    {
        // Si el jugador murió, desactivar el spawner por completo
        if (GoblinEnemy.IsPlayerDying)
        {
            ClearAllActiveGoblins();
            enabled = false;
            return;
        }

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
                SpawnPatrolGoblinNearPlayer(activeGoblins.Count);
                spawnTimer = spawnInterval;
            }
        }

        // Correa inteligente: si el jugador sale corriendo y deja atrás a un enemigo, lo reubicamos por delante (fuera de vista)
        for (int i = 0; i < activeGoblins.Count; i++)
        {
            GoblinEnemy goblin = activeGoblins[i];
            if (goblin == null || goblin.IsStunned || goblin.CurrentState == GoblinEnemy.AIState.Chase) continue;

            float dist = Vector3.Distance(playerTransform.position, goblin.transform.position);
            if (dist > maxPatrolLeashDistance)
            {
                // Nunca teletransportar si el jugador lo está viendo en el mismo pasillo
                if (MazeGenerator.Instance.HasCorridorLineOfSight(goblin.transform.position, playerTransform.position))
                    continue;

                Vector3 newPatrolStart = GetStrategicRepositionPoint(goblin.AssignedRoleIndex);
                goblin.transform.position = newPatrolStart;
                goblin.EnterPatrolState();
            }
        }
    }

    private void SpawnPatrolGoblinNearPlayer(int enemyIndex)
    {
        int assignedRole = enemyIndex % 4;
        Vector3 spawnPos = GetStrategicRepositionPoint(assignedRole);

        GameObject chosenPrefab = goblinPrefabs[Random.Range(0, goblinPrefabs.Length)];
        if (chosenPrefab == null) return;

        GameObject goblinObj = Instantiate(chosenPrefab, spawnPos, Quaternion.identity, transform);
        string[] roleNames = { "Ambusher", "Stalker", "Sentinel", "Acoustic" };
        goblinObj.name = $"Enemy_{roleNames[assignedRole]}_{enemyIndex + 1}";
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
            assignedRole,
            jumpscareCameraHeight,
            jumpscareDistance,
            jumpscareDuration,
            searchDurationAfterLosingSight,
            enemyBrightness
        );

        activeGoblins.Add(enemyScript);
    }

    private Vector3 GetStrategicRepositionPoint(int roleIndex)
    {
        Vector3 pPos = playerTransform.position;
        Vector3 pForward = playerTransform.forward;
        pForward.y = 0f;
        if (pForward.sqrMagnitude < 0.001f) pForward = Vector3.forward;
        pForward.Normalize();

        Vector3 bestPoint = MazeGenerator.Instance.GetTacticalPatrolPointNearPlayer(
            pPos,
            minSpawnDistanceFromPlayer,
            maxSpawnDistanceFromPlayer
        );
        float bestScore = -9999f;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 candidate = MazeGenerator.Instance.GetTacticalPatrolPointNearPlayer(
                pPos,
                minSpawnDistanceFromPlayer,
                maxSpawnDistanceFromPlayer
            );

            // Evitar aparecer en línea de visión directa del jugador
            bool hasLOS = MazeGenerator.Instance.HasCorridorLineOfSight(candidate, pPos);
            float losPenalty = hasLOS ? 5.0f : 0f;

            Vector3 dirFromPlayer = (candidate - pPos);
            dirFromPlayer.y = 0f;
            if (dirFromPlayer.sqrMagnitude > 0.01f) dirFromPlayer.Normalize();

            float forwardDot = Vector3.Dot(pForward, dirFromPlayer);

            // Priorizar que aparezcan hacia adelante o en los flancos hacia donde corre el jugador
            float roleAlignment = (roleIndex == 0 || roleIndex == 2) ? forwardDot * 1.8f : forwardDot * 0.8f;

            // Separación respecto a otros enemigos activos para no amontonarlos en el mismo pasillo
            float crowdingPenalty = 0f;
            for (int i = 0; i < activeGoblins.Count; i++)
            {
                if (activeGoblins[i] == null) continue;
                float dToOther = Vector3.Distance(candidate, activeGoblins[i].transform.position);
                if (dToOther < 8f)
                {
                    crowdingPenalty += (8f - dToOther) * 0.6f;
                }
            }

            float score = roleAlignment - losPenalty - crowdingPenalty;
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = candidate;
            }
        }

        return bestPoint;
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