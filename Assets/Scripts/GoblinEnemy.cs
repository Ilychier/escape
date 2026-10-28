using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoblinEnemy : MonoBehaviour
{
    public static readonly List<GoblinEnemy> AllActiveEnemies = new List<GoblinEnemy>();

    public enum AIState
    {
        Patrol,               // Patrulla por los pasillos cercanos a la zona activa sin conocer la celda exacta del jugador
        Chase,                // Persecución activa mientras tiene línea de visión o escucha cercana
        InvestigateLastKnown, // Va hasta la última posición donde vio o escuchó al jugador
        SearchNearbyRoutes,   // Explora los pasillos y bifurcaciones adyacentes antes de volver a patrullar
        Stunned               // Aturdido temporalmente por el impacto de una roca
    }

    [Header("Velocidades por Estado")]
    [SerializeField] private float patrolSpeed = 2.15f;
    [SerializeField] private float investigateSpeed = 2.5f;
    [SerializeField] private float searchSpeed = 2.25f;
    [SerializeField] private float chaseSpeed = 2.9f;

    [Header("Percepción (Línea de Visión y Oído)")]
    [SerializeField] private float visionRange = 17.0f;
    [Range(60f, 170f)]
    [SerializeField] private float visionAngle = 120f;
    [SerializeField] private float hearingRangeRunning = 10.0f;
    [SerializeField] private float closeProximitySenseRange = 3.2f;
    [SerializeField] private float loseSightGraceTime = 0.6f;

    [Header("Búsqueda e Investigación")]
    [SerializeField] private float searchDuration = 7.5f;
    [SerializeField] private float pathUpdateInterval = 0.2f;

    [Header("Captura y Aturdimiento")]
    [SerializeField] private float killDistance = 1.15f;
    [SerializeField] private float stunDuration = 4.5f;

    [Header("Apariencia Oscura del Enemigo")]
    [Range(0.05f, 1f)]
    [SerializeField] private float enemyBrightness = 0.25f;

    [Header("Escena de Jumpscare (Fondo Negro)")]
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
    private float walkAnimPhase = 0f;
    private static bool isPlayerDying = false;

    public AIState CurrentState => currentState;
    public bool IsStunned => currentState == AIState.Stunned;

    private void Awake()
    {
        isPlayerDying = false;
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
        chaseSpeed = baseChaseSpeed;
        patrolSpeed = baseChaseSpeed * 0.74f;
        investigateSpeed = baseChaseSpeed * 0.88f;
        searchSpeed = baseChaseSpeed * 0.78f;
        stunDuration = stunTime;

        catchClip = onCatchMp3;
        catchVolume = onCatchVol;
        homeSectorIndex = assignedSector;

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
        eerieLight.color = new Color(1f, 0.2f, 0.1f, 1f);
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
            monsterAmbientSource.Play();
        }

        EnterPatrolState();
    }

    private void Update()
    {
        if (PauseManager.IsGamePaused)
        return;

        if (player == null || isPlayerDying) return;

        Vector3 playerFrameDelta = player.position - previousPlayerPos;
        playerFrameDelta.y = 0f;
        float playerCurrentSpeed = playerFrameDelta.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
        Vector3 playerMoveDir = playerFrameDelta.sqrMagnitude > 0.0001f ? playerFrameDelta.normalized : player.forward;
        previousPlayerPos = player.position;

        if (currentState == AIState.Stunned)
        {
            stunTimer -= Time.deltaTime;
            float wobbleX = Mathf.Sin(Time.time * 8f) * 12f - 25f;
            float wobbleZ = Mathf.Cos(Time.time * 8f) * 12f;
            transform.localRotation = Quaternion.Euler(wobbleX, transform.eulerAngles.y, wobbleZ);

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

        float dist3DToPlayer = Vector3.Distance(
            new Vector3(transform.position.x, player.position.y, transform.position.z),
            player.position
        );

        if (!playerProtectedInSafeZone && !isPlayerDying && dist3DToPlayer <= killDistance)
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

            if (currentState != AIState.Chase)
            {
                EnterChaseState();
            }
        }
        else
        {
            timeSinceLastSawPlayer += Time.deltaTime;
        }

        switch (currentState)
        {
            case AIState.Chase:
                UpdateChaseState(playerProtectedInSafeZone);
                break;

            case AIState.InvestigateLastKnown:
                UpdateInvestigateState();
                break;

            case AIState.SearchNearbyRoutes:
                UpdateSearchState();
                break;

            case AIState.Patrol:
                UpdatePatrolState();
                break;
        }
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
            eerieLight.color = new Color(1f, 0.15f, 0.08f, 1f);
            eerieLight.intensity = 1.4f;
        }
    }

    private void UpdateChaseState(bool playerInSafeZone)
    {
        if (playerInSafeZone || timeSinceLastSawPlayer > loseSightGraceTime)
        {
            EnterInvestigateLastKnownState();
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
            eerieLight.color = new Color(1f, 0.45f, 0.1f, 1f);
            eerieLight.intensity = 0.9f;
        }
    }

    private void UpdateInvestigateState()
    {
        float distToLastKnown = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(currentTargetPosition.x, currentTargetPosition.z)
        );

        if (distToLastKnown <= 0.75f)
        {
            EnterSearchNearbyRoutesState();
            return;
        }

        MoveAlongMazeCorridors(currentTargetPosition, investigateSpeed, true);
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
                float dotA = Vector3.Dot(dirA, lastSeenPlayerMoveDir);
                float dotB = Vector3.Dot(dirB, lastSeenPlayerMoveDir);
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

    private void UpdateSearchState()
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

        MoveAlongMazeCorridors(currentTargetPosition, searchSpeed, true);
    }

    public void EnterPatrolState()
    {
        currentState = AIState.Patrol;
        if (eerieLight != null)
        {
            eerieLight.color = new Color(0.85f, 0.2f, 0.1f, 1f);
            eerieLight.intensity = 0.5f;
        }

        PickNewPatrolDestination();
    }

    private void PickNewPatrolDestination()
    {
        if (MazeGenerator.Instance != null)
        {
            // Elige intersecciones en un anillo táctico de 8 a 22 metros alrededor de la región donde se mueve el jugador
            // Así nunca se queda patrullando esquinas vacías a 80 metros donde el jugador ya no está
            currentTargetPosition = MazeGenerator.Instance.GetTacticalPatrolPointNearPlayer(transform.position, 8f, 22f);
            pathUpdateTimer = 0f;
        }
    }

    private void UpdatePatrolState()
    {
        float distToPatrolTarget = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(currentTargetPosition.x, currentTargetPosition.z)
        );

        if (distToPatrolTarget <= 0.9f)
        {
            PickNewPatrolDestination();
        }

        MoveAlongMazeCorridors(currentTargetPosition, patrolSpeed, true);
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

        currentState = AIState.Stunned;
        stunTimer = customDuration > 0f ? customDuration : stunDuration;

        if (bodyCollider != null) bodyCollider.isTrigger = true;
        if (eerieLight != null) eerieLight.color = new Color(0.2f, 0.9f, 1f, 1f);

        if (monsterAmbientSource != null && monsterAmbientSource.isPlaying)
        {
            monsterAmbientSource.Pause();
        }
    }

    private void RecoverFromStun()
    {
        if (bodyCollider != null) bodyCollider.isTrigger = false;

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

        for (int i = 0; i < AllActiveEnemies.Count; i++)
        {
            GoblinEnemy other = AllActiveEnemies[i];
            if (other == null) continue;
            if (other.monsterAmbientSource != null)
            {
                other.monsterAmbientSource.Stop();
            }
            if (other != this)
            {
                other.gameObject.SetActive(false);
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