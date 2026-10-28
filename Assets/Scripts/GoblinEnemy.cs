using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoblinEnemy : MonoBehaviour
{
    public static readonly List<GoblinEnemy> AllActiveEnemies = new List<GoblinEnemy>();

    public enum AIState
    {
        Patrol,               // Patrulla táctica según su arquetipo (Emboscador, Acechador, Centinela, Acústico)
        Chase,                // Persecución activa mientras tiene línea de visión o escucha cercana
        InvestigateLastKnown, // Va hasta la última posición donde vio o escuchó al jugador o una roca
        SearchNearbyRoutes,   // Explora los pasillos y bifurcaciones adyacentes antes de volver a patrullar
        Stunned               // Aturdido temporalmente por el impacto de una roca
    }

    public enum HunterArchetype
    {
        Ambusher = 0,         // Pinky: Busca intersecciones por delante del jugador
        Stalker = 1,          // Blinky: Presiona de cerca y recorta distancia rápido
        RouteSentinel = 2,    // Inky: Corta flancos y pasillos diagonales a media distancia
        AcousticProwler = 3   // Clyde: Patrulla abierto pero tiene un oído mucho más agudo
    }

    [Header("Velocidades por Estado")]
    [SerializeField] private float patrolSpeed = 2.15f;
    [SerializeField] private float investigateSpeed = 2.5f;
    [SerializeField] private float searchSpeed = 2.25f;
    [SerializeField] private float chaseSpeed = 2.9f;
    [Tooltip("Multiplicador de velocidad cuando el enemigo está lejos y fuera de vista para que no lo dejes atrás corriendo")]
    [SerializeField] private float offScreenCatchUpMultiplier = 1.35f;

    [Header("Percepción (Línea de Visión y Oído)")]
    [SerializeField] private float visionRange = 17.0f;
    [Range(60f, 170f)]
    [SerializeField] private float visionAngle = 120f;
    [SerializeField] private float hearingRangeRunning = 10.0f;
    [SerializeField] private float closeProximitySenseRange = 3.2f;
    [SerializeField] private float loseSightGraceTime = 1.5f;

    [Header("Equilibrio Anti-Encierro (AI Director)")]
    [Tooltip("Máximo de enemigos que pueden perseguir a matar simultáneamente (evita encierros imposibles de 3 o 4)")]
    [SerializeField] private int maxSimultaneousChasers = 2;
    [Tooltip("Distancia mínima que mantienen los enemigos adicionales cuando el cupo de persecución está lleno")]
    [SerializeField] private float flankContainmentDistance = 6.5f;

    [Header("Búsqueda e Investigación")]
    [SerializeField] private float searchDuration = 7.5f;
    [SerializeField] private float pathUpdateInterval = 0.2f;

    [Header("Captura, Aturdimiento y Mejoras UX de Roca")]
    [SerializeField] private float killDistance = 1.15f;
    [SerializeField] private float stunDuration = 4.5f;
    [Tooltip("Metros que el enemigo retrocede al recibir un golpe de roca")]
    [SerializeField] private float stunKnockbackDistance = 1.25f;
    [Tooltip("Radio en el que un segundo enemigo pegado al primero también queda aturdido por el impacto")]
    [SerializeField] private float splashStunRadius = 2.8f;

    [Header("Apariencia Oscura del Enemigo")]
    [Range(0.05f, 1f)]
    [SerializeField] private float enemyBrightness = 0.25f;

    [Header("Escena de Jumpscare")]
    [SerializeField] private float jumpscareCameraHeight = 1.45f;
    [SerializeField] private float jumpscareDistance = 1.75f;
    [SerializeField] private float jumpscareDuration = 3.0f;
    [SerializeField] private bool keepShakingDuringGameOver = true;
    [SerializeField] private float cameraShakeAmount = 0.04f;

    private Transform player;
    private CapsuleCollider bodyCollider;
    private Light eerieLight;
    private AudioSource monsterAmbientSource;
    private AudioClip catchClip;
    private float catchVolume = 1f;

    private AIState currentState = AIState.Patrol;
    private int homeSectorIndex = 0;
    private HunterArchetype archetype = HunterArchetype.Ambusher;

    private Vector3 lastKnownPlayerPosition;
    private Vector3 lastSeenPlayerMoveDir;
    private Vector3 previousPlayerPos;
    private float timeSinceLastSawPlayer = 999f;

    private Vector3 currentTargetPosition;
    private Vector3 currentStepWaypoint;
    private float pathUpdateTimer = 0f;
    private float stateTimer = 0f;
    private Queue<Vector3> searchBranchQueue = new Queue<Vector3>();

    private float stunTimer = 0f;
    private float postStunGraceTimer = 0f;
    private float walkAnimPhase = 0f;

    private static bool isPlayerDying = false;
    private static float slipThroughProtectionTimer = 0f;

    public static bool IsPlayerDying => isPlayerDying;
    public AIState CurrentState => currentState;
    public bool IsStunned => currentState == AIState.Stunned;
    public int AssignedRoleIndex => homeSectorIndex;

    private void Awake()
    {
        isPlayerDying = false;
        slipThroughProtectionTimer = 0f;
    }

    public static void ResetDeathState()
    {
        isPlayerDying = false;
        slipThroughProtectionTimer = 0f;
    }

    /// <summary>
    /// Llama a este método desde tu script de la roca cuando impacte contra el suelo o una pared
    /// para atraer a los enemigos cercanos a investigar el ruido.
    /// </summary>
    public static void NotifyNoiseAtPosition(Vector3 noiseWorldPos, float baseNoiseRadius = 15f)
    {
        for (int i = 0; i < AllActiveEnemies.Count; i++)
        {
            GoblinEnemy enemy = AllActiveEnemies[i];
            if (enemy == null || enemy.IsStunned || enemy.currentState == AIState.Chase) continue;

            float effectiveRadius = (enemy.archetype == HunterArchetype.AcousticProwler)
                ? baseNoiseRadius * 1.4f
                : baseNoiseRadius;

            float dist = Vector3.Distance(enemy.transform.position, noiseWorldPos);
            if (dist <= effectiveRadius)
            {
                enemy.HearExternalNoise(noiseWorldPos);
            }
        }
    }

    private void OnEnable()
    {
        if (!AllActiveEnemies.Contains(this))
        {
            AllActiveEnemies.Add(this);
        }
    }

    private void OnDisable()
    {
        AllActiveEnemies.Remove(this);
    }

    public void Initialize(
        Transform playerTarget,
        float baseChaseSpeed,
        float stunTime,
        AudioClip ambientMp3,
        float hearingRadius,
        float ambientVol,
        AudioClip onCatchMp3,
        float onCatchVol,
        int assignedSector = 0,
        float customJumpscareHeight = 1.45f,
        float customJumpscareDistance = 1.75f,
        float customJumpscareDuration = 3.0f,
        float customSearchDuration = 7.5f,
        float customBrightness = 0.25f)
    {
        player = playerTarget;
        homeSectorIndex = Mathf.Abs(assignedSector) % 4;
        archetype = (HunterArchetype)homeSectorIndex;

        chaseSpeed = baseChaseSpeed;
        patrolSpeed = baseChaseSpeed * 0.76f;
        investigateSpeed = baseChaseSpeed * 0.90f;
        searchSpeed = baseChaseSpeed * 0.80f;
        stunDuration = stunTime;

        // Ajustes de personalidad según el arquetipo estilo Pac-Man
        switch (archetype)
        {
            case HunterArchetype.Ambusher: // Pinky: rápido posicionándose en patrulla para emboscar adelante
                patrolSpeed = baseChaseSpeed * 0.82f;
                break;

            case HunterArchetype.Stalker: // Blinky: persecución constante y tenaz
                chaseSpeed = baseChaseSpeed * 1.02f;
                loseSightGraceTime = 1.8f;
                break;

            case HunterArchetype.RouteSentinel: // Inky: equilibra visión amplia en cruces
                visionAngle = 135f;
                break;

            case HunterArchetype.AcousticProwler: // Clyde: menos visión frontal, pero escucha correr desde muy lejos
                visionAngle = 90f;
                hearingRangeRunning = 15.5f;
                patrolSpeed = baseChaseSpeed * 0.72f;
                break;
        }

        catchClip = onCatchMp3;
        catchVolume = onCatchVol;

        jumpscareCameraHeight = customJumpscareHeight;
        jumpscareDistance = customJumpscareDistance;
        jumpscareDuration = customJumpscareDuration;
        searchDuration = customSearchDuration;
        enemyBrightness = customBrightness;

        if (player != null)
        {
            previousPlayerPos = player.position;
        }

        ApplyDarkTintToModel(enemyBrightness);

        bodyCollider = gameObject.GetComponent<CapsuleCollider>();
        if (bodyCollider == null)
        {
            bodyCollider = gameObject.AddComponent<CapsuleCollider>();
        }
        bodyCollider.center = new Vector3(0f, 0.65f, 0f);
        bodyCollider.radius = 0.35f;
        bodyCollider.height = 1.3f;

        GameObject lightObj = new GameObject("GoblinGlow");
        lightObj.transform.SetParent(transform);
        lightObj.transform.localPosition = new Vector3(0f, 1.05f, 0.15f);
        eerieLight = lightObj.AddComponent<Light>();
        eerieLight.type = LightType.Point;
        eerieLight.color = Color.black;
        eerieLight.range = 1.8f;
        eerieLight.intensity = 0.5f;
        eerieLight.shadows = LightShadows.None;

        if (ambientMp3 != null)
        {
            monsterAmbientSource = gameObject.AddComponent<AudioSource>();
            monsterAmbientSource.clip = ambientMp3;
            monsterAmbientSource.loop = true;
            monsterAmbientSource.playOnAwake = false;
            monsterAmbientSource.spatialBlend = 1f;
            monsterAmbientSource.rolloffMode = AudioRolloffMode.Linear;
            monsterAmbientSource.minDistance = 1.2f;
            monsterAmbientSource.maxDistance = hearingRadius;
            monsterAmbientSource.volume = ambientVol;

            // Tono sutilmente distinto por arquetipo para que el oído del jugador los diferencie
            float[] rolePitches = { 1.12f, 0.86f, 1.0f, 1.22f };
            monsterAmbientSource.pitch = rolePitches[homeSectorIndex];
            monsterAmbientSource.Play();
        }

        EnterPatrolState();
    }

    private void Update()
    {
        if (PauseManager.IsGamePaused)
            return;

        if (player == null || isPlayerDying) return;

        if (slipThroughProtectionTimer > 0f)
        {
            slipThroughProtectionTimer -= Time.deltaTime;
        }

        if (postStunGraceTimer > 0f)
        {
            postStunGraceTimer -= Time.deltaTime;
        }

        Vector3 playerFrameDelta = player.position - previousPlayerPos;
        playerFrameDelta.y = 0f;
        float playerCurrentSpeed = playerFrameDelta.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
        Vector3 playerMoveDir = playerFrameDelta.sqrMagnitude > 0.0001f ? playerFrameDelta.normalized : player.forward;
        previousPlayerPos = player.position;

        float dist3DToPlayer = Vector3.Distance(
            new Vector3(transform.position.x, player.position.y, transform.position.z),
            player.position
        );

        if (currentState == AIState.Stunned)
        {
            stunTimer -= Time.deltaTime;
            float wobbleX = Mathf.Sin(Time.time * 8f) * 12f - 25f;
            float wobbleZ = Mathf.Cos(Time.time * 8f) * 12f;
            transform.localRotation = Quaternion.Euler(wobbleX, transform.eulerAngles.y, wobbleZ);

            // Si el jugador está atravesando al enemigo aturdido en un pasillo estrecho, darle protección breve
            if (dist3DToPlayer <= 1.65f)
            {
                slipThroughProtectionTimer = Mathf.Max(slipThroughProtectionTimer, 0.45f);
            }

            if (stunTimer <= 0f)
            {
                RecoverFromStun();
            }
            return;
        }

        bool playerProtectedInSafeZone = MazeGenerator.Instance != null && MazeGenerator.Instance.IsPlayerInSafeZone();

        if (monsterAmbientSource != null)
        {
            monsterAmbientSource.mute = playerProtectedInSafeZone;
        }

        if (!playerProtectedInSafeZone && !isPlayerDying && postStunGraceTimer <= 0f && slipThroughProtectionTimer <= 0f && dist3DToPlayer <= killDistance)
        {
            if (MazeGenerator.Instance == null || MazeGenerator.Instance.HasCorridorLineOfSight(transform.position, player.position))
            {
                isPlayerDying = true;
                StartCoroutine(KillPlayerRoutine());
                return;
            }
        }

        bool perceivesPlayerNow = CanPerceivePlayer(playerCurrentSpeed, playerProtectedInSafeZone, out Vector3 sensedPlayerPos);

        if (perceivesPlayerNow)
        {
            lastKnownPlayerPosition = sensedPlayerPos;
            lastSeenPlayerMoveDir = playerMoveDir;
            timeSinceLastSawPlayer = 0f;

            // AI Director: permitir Chase solo si no superamos el cupo de atacantes simultáneos (o si ya está muy cerca)
            if (currentState != AIState.Chase)
            {
                if (CanJoinActiveChase(dist3DToPlayer))
                {
                    EnterChaseState();
                }
                else if (currentState == AIState.Patrol)
                {
                    EnterInvestigateLastKnownState();
                }
            }
        }
        else
        {
            timeSinceLastSawPlayer += Time.deltaTime;
        }

        switch (currentState)
        {
            case AIState.Chase:
                UpdateChaseState(playerProtectedInSafeZone, dist3DToPlayer);
                break;

            case AIState.InvestigateLastKnown:
                UpdateInvestigateState(playerCurrentSpeed, dist3DToPlayer);
                break;

            case AIState.SearchNearbyRoutes:
                UpdateSearchState(playerCurrentSpeed, dist3DToPlayer);
                break;

            case AIState.Patrol:
                UpdatePatrolState(playerCurrentSpeed, dist3DToPlayer);
                break;
        }
    }

    private bool CanJoinActiveChase(float myDistToPlayer)
    {
        int closerOrActiveChasers = 0;
        for (int i = 0; i < AllActiveEnemies.Count; i++)
        {
            GoblinEnemy other = AllActiveEnemies[i];
            if (other == null || other == this || other.IsStunned) continue;

            if (other.currentState == AIState.Chase)
            {
                closerOrActiveChasers++;
            }
        }

        if (closerOrActiveChasers < maxSimultaneousChasers)
        {
            return true;
        }

        // Si ya hay 2 persiguiendo, solo entra en Chase si está pegado al jugador (< 3.8m)
        return myDistToPlayer <= 3.8f;
    }

    private bool IsOutnumberedContainmentActive(float myDistToPlayer)
    {
        if (player == null) return false;

        int closerActiveThreats = 0;
        for (int i = 0; i < AllActiveEnemies.Count; i++)
        {
            GoblinEnemy other = AllActiveEnemies[i];
            if (other == null || other == this || other.IsStunned) continue;

            if (other.currentState == AIState.Chase || other.currentState == AIState.InvestigateLastKnown)
            {
                float otherDist = Vector3.Distance(other.transform.position, player.position);
                if (otherDist < myDistToPlayer)
                {
                    closerActiveThreats++;
                }
            }
        }

        return closerActiveThreats >= maxSimultaneousChasers;
    }

    public void HearExternalNoise(Vector3 noisePosition)
    {
        if (IsStunned || isPlayerDying) return;
        if (MazeGenerator.Instance != null && MazeGenerator.Instance.IsPositionInSafeZone3D(noisePosition, 0.8f)) return;

        lastKnownPlayerPosition = noisePosition;
        Vector3 dirToNoise = (noisePosition - transform.position);
        dirToNoise.y = 0f;
        if (dirToNoise.sqrMagnitude > 0.01f)
        {
            lastSeenPlayerMoveDir = dirToNoise.normalized;
        }

        EnterInvestigateLastKnownState();
    }

    private bool CanPerceivePlayer(float playerSpeed, bool playerProtectedInSafeZone, out Vector3 sensedPos)
    {
        sensedPos = Vector3.zero;
        if (player == null || playerProtectedInSafeZone) return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (distance > visionRange) return false;

        bool hasCorridorLOS = MazeGenerator.Instance != null
            ? MazeGenerator.Instance.HasCorridorLineOfSight(transform.position, player.position)
            : true;

        if (distance <= closeProximitySenseRange && hasCorridorLOS)
        {
            sensedPos = player.position;
            return true;
        }

        if (playerSpeed > 4.0f && distance <= hearingRangeRunning)
        {
            sensedPos = player.position;
            return true;
        }

        float angleToPlayer = Vector3.Angle(transform.forward, toPlayer.normalized);
        if (angleToPlayer <= visionAngle * 0.5f && hasCorridorLOS)
        {
            Vector3 eyeOrigin = transform.position + Vector3.up * 1.0f;
            Vector3 targetChest = player.position + Vector3.up * 0.4f;
            Vector3 rayDir = (targetChest - eyeOrigin).normalized;

            if (!Physics.Raycast(eyeOrigin, rayDir, out RaycastHit hit, distance))
            {
                sensedPos = player.position;
                return true;
            }
            if (hit.transform == player || hit.transform.IsChildOf(player))
            {
                sensedPos = player.position;
                return true;
            }
        }

        return false;
    }

    private void EnterChaseState()
    {
        currentState = AIState.Chase;
        pathUpdateTimer = 0f;
        if (eerieLight != null)
        {
            eerieLight.color = Color.black;
            eerieLight.intensity = 1.4f;
        }
    }

    private void UpdateChaseState(bool playerInSafeZone, float distToPlayer)
    {
        if (playerInSafeZone || timeSinceLastSawPlayer > loseSightGraceTime)
        {
            EnterInvestigateLastKnownState();
            return;
        }

        // Si ya hay otros 2 enemigos más cerca acosando al jugador, este pasa a flanquear para no encerrarlo entre 3 o 4
        if (IsOutnumberedContainmentActive(distToPlayer) && distToPlayer > 4.2f)
        {
            EnterSearchNearbyRoutesState();
            return;
        }

        currentTargetPosition = lastKnownPlayerPosition;
        MoveAlongMazeCorridors(currentTargetPosition, chaseSpeed, true);
    }

    private void EnterInvestigateLastKnownState()
    {
        currentState = AIState.InvestigateLastKnown;
        pathUpdateTimer = 0f;

        if (MazeGenerator.Instance != null && MazeGenerator.Instance.IsPositionInSafeZone3D(lastKnownPlayerPosition, 0.8f))
        {
            EnterPatrolState();
            return;
        }

        currentTargetPosition = lastKnownPlayerPosition;
        if (eerieLight != null)
        {
            eerieLight.color = Color.black;
            eerieLight.intensity = 0.9f;
        }
    }

    private void UpdateInvestigateState(float playerSpeed, float distToPlayer)
    {
        // Si ya hay 2 atacantes más cercanos y este se acerca demasiado, se desvía a cubrir rutas laterales
        if (IsOutnumberedContainmentActive(distToPlayer) && distToPlayer <= flankContainmentDistance)
        {
            EnterSearchNearbyRoutesState();
            return;
        }

        float distToLastKnown = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(currentTargetPosition.x, currentTargetPosition.z)
        );

        if (distToLastKnown <= 0.75f)
        {
            EnterSearchNearbyRoutesState();
            return;
        }

        float effectiveSpeed = GetElasticSpeed(investigateSpeed, playerSpeed, distToPlayer);
        MoveAlongMazeCorridors(currentTargetPosition, effectiveSpeed, true);
    }

    private void EnterSearchNearbyRoutesState()
    {
        currentState = AIState.SearchNearbyRoutes;
        stateTimer = searchDuration;
        searchBranchQueue.Clear();

        if (MazeGenerator.Instance != null)
        {
            List<Vector3> neighbors = MazeGenerator.Instance.GetConnectedCorridorPositions(transform.position, true);

            neighbors.Sort((a, b) =>
            {
                Vector3 dirA = (a - transform.position).normalized;
                Vector3 dirB = (b - transform.position).normalized;
                float dotA = Vector3.Dot(dirA, lastSeenPlayerMoveDir) - GetCrowdPenaltyAtPoint(a) * 0.5f;
                float dotB = Vector3.Dot(dirB, lastSeenPlayerMoveDir) - GetCrowdPenaltyAtPoint(b) * 0.5f;
                return dotB.CompareTo(dotA);
            });

            foreach (Vector3 branchPos in neighbors)
            {
                searchBranchQueue.Enqueue(branchPos);
            }
        }

        PickNextSearchBranch();
    }

    private void PickNextSearchBranch()
    {
        if (searchBranchQueue.Count > 0)
        {
            currentTargetPosition = searchBranchQueue.Dequeue();
            pathUpdateTimer = 0f;
        }
        else if (MazeGenerator.Instance != null)
        {
            List<Vector3> branches = MazeGenerator.Instance.GetConnectedCorridorPositions(transform.position, true);
            if (branches.Count > 0)
            {
                currentTargetPosition = branches[Random.Range(0, branches.Count)];
                pathUpdateTimer = 0f;
            }
        }
    }

    private void UpdateSearchState(float playerSpeed, float distToPlayer)
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            EnterPatrolState();
            return;
        }

        float distToBranch = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(currentTargetPosition.x, currentTargetPosition.z)
        );

        if (distToBranch <= 0.65f)
        {
            PickNextSearchBranch();
        }

        float effectiveSpeed = GetElasticSpeed(searchSpeed, playerSpeed, distToPlayer);
        MoveAlongMazeCorridors(currentTargetPosition, effectiveSpeed, true);
    }

    public void EnterPatrolState()
    {
        currentState = AIState.Patrol;
        if (eerieLight != null)
        {
            eerieLight.color = Color.black;
            eerieLight.intensity = 0.5f;
        }

        PickNewPatrolDestination();
    }

    private void PickNewPatrolDestination()
    {
        if (MazeGenerator.Instance == null) return;

        if (player == null)
        {
            currentTargetPosition = MazeGenerator.Instance.GetTacticalPatrolPointNearPlayer(transform.position, 8f, 20f);
            pathUpdateTimer = 0f;
            return;
        }

        // Configurar distancias según el arquetipo Pac-Man
        float minD = 8f;
        float maxD = 20f;
        switch (archetype)
        {
            case HunterArchetype.Ambusher:        minD = 9f;  maxD = 18f; break;
            case HunterArchetype.Stalker:         minD = 7f;  maxD = 14f; break;
            case HunterArchetype.RouteSentinel:   minD = 10f; maxD = 20f; break;
            case HunterArchetype.AcousticProwler: minD = 12f; maxD = 22f; break;
        }

        Vector3 pPos = player.position;
        Vector3 pForward = player.forward;
        pForward.y = 0f;
        if (pForward.sqrMagnitude < 0.001f) pForward = Vector3.forward;
        pForward.Normalize();

        Vector3 bestCandidate = Vector3.zero;
        float bestScore = -9999f;

        // Muestreamos 6 puntos válidos del laberinto y elegimos el que mejor cumpla el rol Pac-Man y esté lejos de otros enemigos
        for (int i = 0; i < 6; i++)
        {
            Vector3 candidate = MazeGenerator.Instance.GetTacticalPatrolPointNearPlayer(transform.position, minD, maxD);
            Vector3 toCandidate = candidate - pPos;
            toCandidate.y = 0f;
            float distFromPlayer = toCandidate.magnitude;
            Vector3 dirFromPlayer = distFromPlayer > 0.01f ? toCandidate / distFromPlayer : pForward;

            float forwardDot = Vector3.Dot(pForward, dirFromPlayer);
            float roleScore = 0f;

            switch (archetype)
            {
                case HunterArchetype.Ambusher:
                    // Pinky: premia fuertemente los pasillos situados frente al avance del jugador
                    roleScore = forwardDot * 2.2f;
                    break;

                case HunterArchetype.Stalker:
                    // Blinky: premia estar cerca del jugador presionando constantemente
                    roleScore = (1f - Mathf.Clamp01(distFromPlayer / 20f)) * 1.8f;
                    break;

                case HunterArchetype.RouteSentinel:
                    // Inky: premia ángulos laterales/diagonales (dot cercano a 0.35) para cortar cruces
                    roleScore = (1f - Mathf.Abs(forwardDot - 0.35f)) * 1.6f;
                    break;

                case HunterArchetype.AcousticProwler:
                    // Clyde: premia flancos amplios o retaguardia para rodear
                    roleScore = -forwardDot * 0.8f + (distFromPlayer / 22f);
                    break;
            }

            // Penalizar si otro enemigo ya está cerca o yendo hacia ese mismo punto (evita que anden juntos)
            float crowdPenalty = GetCrowdPenaltyAtPoint(candidate);
            float totalScore = roleScore - crowdPenalty + Random.Range(-0.15f, 0.15f);

            if (totalScore > bestScore)
            {
                bestScore = totalScore;
                bestCandidate = candidate;
            }
        }

        currentTargetPosition = bestCandidate;
        pathUpdateTimer = 0f;
    }

    private float GetCrowdPenaltyAtPoint(Vector3 point)
    {
        float penalty = 0f;
        for (int i = 0; i < AllActiveEnemies.Count; i++)
        {
            GoblinEnemy other = AllActiveEnemies[i];
            if (other == null || other == this) continue;

            float distToOtherPos = Vector3.Distance(point, other.transform.position);
            if (distToOtherPos < 7.5f)
            {
                penalty += (7.5f - distToOtherPos) * 0.45f;
            }

            float distToOtherTarget = Vector3.Distance(point, other.currentTargetPosition);
            if (distToOtherTarget < 7.5f)
            {
                penalty += (7.5f - distToOtherTarget) * 0.35f;
            }
        }
        return penalty;
    }

    private void UpdatePatrolState(float playerSpeed, float distToPlayer)
    {
        float distToPatrolTarget = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(currentTargetPosition.x, currentTargetPosition.z)
        );

        // Si es el Emboscador (Pinky) y quedó detrás del jugador mientras el jugador corre, recalcula destino hacia adelante
        if (archetype == HunterArchetype.Ambusher && player != null && playerSpeed > 3.5f)
        {
            Vector3 toEnemy = (transform.position - player.position).normalized;
            if (Vector3.Dot(player.forward, toEnemy) < -0.35f && distToPatrolTarget < 4.5f)
            {
                PickNewPatrolDestination();
            }
        }

        if (distToPatrolTarget <= 0.9f)
        {
            PickNewPatrolDestination();
        }

        float effectiveSpeed = GetElasticSpeed(patrolSpeed, playerSpeed, distToPlayer);
        MoveAlongMazeCorridors(currentTargetPosition, effectiveSpeed, true);
    }

    private float GetElasticSpeed(float baseStateSpeed, float playerSpeed, float distToPlayer)
    {
        if (MazeGenerator.Instance == null || player == null) return baseStateSpeed;

        // Solo acelerar cuando el jugador NO tiene línea de visión directa con este enemigo
        bool hasLOS = MazeGenerator.Instance.HasCorridorLineOfSight(transform.position, player.position);
        if (!hasLOS && (distToPlayer > 11.5f || playerSpeed > 4.0f))
        {
            return baseStateSpeed * offScreenCatchUpMultiplier;
        }

        return baseStateSpeed;
    }

    private void MoveAlongMazeCorridors(Vector3 destination, float speed, bool avoidSafeZone)
    {
        pathUpdateTimer -= Time.deltaTime;
        if (pathUpdateTimer <= 0f && MazeGenerator.Instance != null)
        {
            currentStepWaypoint = MazeGenerator.Instance.GetNextPathStep(transform.position, destination, avoidSafeZone);
            pathUpdateTimer = pathUpdateInterval;
        }

        if (MazeGenerator.Instance != null && MazeGenerator.Instance.IsPositionInSafeZone3D(currentStepWaypoint, 0.5f))
        {
            PickNewPatrolDestination();
            return;
        }

        Vector3 moveDir = currentStepWaypoint - transform.position;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            moveDir.Normalize();
        }

        Vector3 candidateNextPos = transform.position + moveDir * (speed * Time.deltaTime);
        if (MazeGenerator.Instance != null && MazeGenerator.Instance.IsPositionInSafeZone3D(candidateNextPos, 0.4f))
        {
            PickNewPatrolDestination();
            return;
        }

        transform.position = candidateNextPos;

        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            bool isChasing = (currentState == AIState.Chase);
            walkAnimPhase += Time.deltaTime * (isChasing ? 18f : 12f);
            float swayZ = Mathf.Sin(walkAnimPhase) * 9f;
            float leanForward = isChasing ? 15f : 8f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot * Quaternion.Euler(leanForward, 0f, swayZ), Time.deltaTime * 10f);

            Vector3 pos = transform.position;
            pos.y = Mathf.Abs(Mathf.Sin(walkAnimPhase)) * 0.08f;
            transform.position = pos;
        }
    }

    private void ApplyDarkTintToModel(float brightness)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            foreach (Material mat in rend.materials)
            {
                mat.enableInstancing = true;
                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    mat.SetColor("_Color", new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a));
                }
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    mat.SetColor("_BaseColor", new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a));
                }
                if (mat.HasProperty("_Smoothness"))
                {
                    mat.SetFloat("_Smoothness", 0f);
                }
                if (mat.HasProperty("_Glossiness"))
                {
                    mat.SetFloat("_Glossiness", 0f);
                }
            }
        }
    }

    public void Stun(float customDuration = -1f)
    {
        if (isPlayerDying) return;

        float finalDuration = customDuration > 0f ? customDuration : stunDuration;
        ApplyStunInternal(finalDuration, true);

        // Onda expansiva: si había otro enemigo pegado bloqueando el mismo pasillo, también se aturde brevemente
        for (int i = 0; i < AllActiveEnemies.Count; i++)
        {
            GoblinEnemy other = AllActiveEnemies[i];
            if (other == null || other == this || other.IsStunned) continue;

            float distToOther = Vector3.Distance(transform.position, other.transform.position);
            if (distToOther <= splashStunRadius)
            {
                other.ApplyStunInternal(finalDuration * 0.65f, true);
            }
        }
    }

    private void ApplyStunInternal(float duration, bool applyKnockback)
    {
        currentState = AIState.Stunned;
        stunTimer = duration;
        slipThroughProtectionTimer = Mathf.Max(slipThroughProtectionTimer, 0.65f);

        if (bodyCollider != null) bodyCollider.isTrigger = true;
        if (eerieLight != null)
        {
            eerieLight.color = new Color(0.2f, 0.9f, 1f, 1f);
            eerieLight.intensity = 1.6f;
        }

        if (monsterAmbientSource != null && monsterAmbientSource.isPlaying)
        {
            monsterAmbientSource.Pause();
        }

        // Empuje físico (Knockback) hacia atrás respecto al jugador sin atravesar muros
        if (applyKnockback && player != null && stunKnockbackDistance > 0.05f)
        {
            Vector3 pushDir = transform.position - player.position;
            pushDir.y = 0f;
            if (pushDir.sqrMagnitude > 0.001f)
            {
                pushDir.Normalize();
                Vector3 rayOrigin = transform.position + Vector3.up * 0.6f;
                float safePushDist = stunKnockbackDistance;

                if (Physics.Raycast(rayOrigin, pushDir, out RaycastHit hit, stunKnockbackDistance + 0.4f))
                {
                    safePushDist = Mathf.Max(0f, hit.distance - 0.45f);
                }

                Vector3 knockedPos = transform.position + pushDir * safePushDist;
                if (MazeGenerator.Instance == null || !MazeGenerator.Instance.IsPositionInSafeZone3D(knockedPos, 0.4f))
                {
                    transform.position = knockedPos;
                }
            }
        }
    }

    private void RecoverFromStun()
    {
        if (bodyCollider != null) bodyCollider.isTrigger = false;

        postStunGraceTimer = 0.85f;

        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
        Vector3 pos = transform.position;
        pos.y = 0f;
        transform.position = pos;

        if (monsterAmbientSource != null)
        {
            monsterAmbientSource.UnPause();
        }

        lastKnownPlayerPosition = transform.position;
        EnterSearchNearbyRoutesState();
    }

    private IEnumerator KillPlayerRoutine()
    {
        isPlayerDying = true;

        // 1. Detener el Spawner para que no instancie nuevos enemigos con audio en segundo plano
        GoblinSpawner spawner = FindObjectOfType<GoblinSpawner>();
        if (spawner != null)
        {
            spawner.enabled = false;
        }

        // 2. Apagar el audio ambiental de este enemigo (el que hace el jumpscare)
        if (monsterAmbientSource != null)
        {
            monsterAmbientSource.Stop();
        }

        GoblinEnemy[] enemies = AllActiveEnemies.ToArray();
        for (int i = 0; i < enemies.Length; i++)
        {
            GoblinEnemy other = enemies[i];
            if (other != null && other != this)
            {
                if (other.monsterAmbientSource != null)
                {
                    other.monsterAmbientSource.Stop();
                }
                Destroy(other.gameObject);
            }
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.SetParent(null);
        }

        if (player != null)
        {
            player.gameObject.SetActive(false);
        }

        if (MazeGenerator.Instance != null)
        {
            transform.SetParent(null);
            MazeGenerator.Instance.gameObject.SetActive(false);
        }

        Vector3 blackStageOrigin = new Vector3(0f, 500f, 0f);
        RenderSettings.fog = false;
        RenderSettings.ambientLight = Color.black;

        transform.position = blackStageOrigin;
        transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        if (eerieLight != null)
        {
            eerieLight.enabled = false;
        }

        Vector3 baseCamPos = blackStageOrigin + new Vector3(0f, jumpscareCameraHeight, -jumpscareDistance);
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.transform.position = baseCamPos;
            cam.transform.rotation = Quaternion.identity;

            GameObject stageLightObj = new GameObject("JumpscareStageLight");
            stageLightObj.transform.SetParent(cam.transform);
            stageLightObj.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            stageLightObj.transform.localRotation = Quaternion.identity;

            Light stageLight = stageLightObj.AddComponent<Light>();
            stageLight.type = LightType.Spot;
            stageLight.color = new Color(1f, 0.88f, 0.85f, 1f);
            stageLight.intensity = 6f;
            stageLight.range = 10f;
            stageLight.spotAngle = 75f;
        }

        if (catchClip != null)
        {
            AudioSource catchAudio = gameObject.AddComponent<AudioSource>();
            catchAudio.clip = catchClip;
            catchAudio.loop = false;
            catchAudio.spatialBlend = 0f;
            catchAudio.volume = catchVolume;
            catchAudio.PlayOneShot(catchClip, catchVolume);
        }

        float timer = 0f;
        while (timer < jumpscareDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / jumpscareDuration);

            float lungeZ = Mathf.Lerp(0f, -0.35f, Mathf.Pow(progress, 0.5f));
            float monsterSway = Mathf.Sin(timer * 35f) * 3.5f;
            transform.position = blackStageOrigin + new Vector3(0f, 0f, lungeZ);
            transform.rotation = Quaternion.Euler(Mathf.Sin(timer * 25f) * 4f, 180f + monsterSway, 0f);

            if (cam != null)
            {
                Vector3 shakeOffset = Random.insideUnitSphere * cameraShakeAmount;
                cam.transform.position = baseCamPos + shakeOffset;
            }

            yield return null;
        }

        GameOverUI ui = GameOverUI.Instance;
        if (ui == null)
        {
            ui = FindObjectOfType<GameOverUI>(true);
        }

        if (ui != null)
        {
            ui.ShowGameOver();
        }
        else
        {
            GameObject canvasObj = GameObject.Find("GameOverCanvas");
            if (canvasObj != null)
            {
                canvasObj.SetActive(true);
                foreach (Transform child in canvasObj.transform)
                {
                    child.gameObject.SetActive(true);
                }
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        while (keepShakingDuringGameOver)
        {
            timer += Time.deltaTime;
            float monsterSway = Mathf.Sin(timer * 35f) * 3.5f;
            transform.position = blackStageOrigin + new Vector3(0f, 0f, -0.35f);
            transform.rotation = Quaternion.Euler(Mathf.Sin(timer * 25f) * 4f, 180f + monsterSway, 0f);

            if (cam != null)
            {
                Vector3 shakeOffset = Random.insideUnitSphere * (cameraShakeAmount * 0.5f);
                cam.transform.position = baseCamPos + shakeOffset;
            }

            yield return null;
        }
    }
}