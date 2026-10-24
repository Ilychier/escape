using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoblinEnemy : MonoBehaviour
{
    public enum StalkDirectionRole
    {
        FrontApproacher, // Spawnea adelante en la ruta y viene hacia el jugador para acecharlo de frente
        RearStalker      // Spawnea detrás del jugador unos segundos después y lo acecha por la espalda
    }

    [Header("Persecución y Ataque")]
    [SerializeField] private float moveSpeed = 2.3f;
    [SerializeField] private float killDistance = 1.2f;
    [SerializeField] private float stunDuration = 4.5f;

    [Header("IA de Acecho (Stalking antes de Cazar)")]
    [Tooltip("Segundos que el enemigo te acecha antes de lanzarse a cazarte")]
    [SerializeField] private float stalkDuration = 8f;
    [Tooltip("Distancia a la que se mantiene acechándote antes de pasar a modo caza")]
    [SerializeField] private float stalkKeepDistance = 7.5f;
    [Tooltip("Multiplicador de velocidad cuando pasa a cazarte definitivamente")]
    [SerializeField] private float huntSpeedMultiplier = 1.3f;

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

    private bool isStunned = false;
    private float stunTimer = 0f;
    private bool isHunting = false;
    private float stalkTimer = 0f;
    private float walkAnimPhase = 0f;
    private static bool isPlayerDying = false;

    public int AssignedQuarter { get; private set; }
    public StalkDirectionRole Role { get; private set; }
    public bool IsOutOfSegment { get; private set; }
    public bool IsStunned => isStunned;

    private void Awake()
    {
        isPlayerDying = false;
    }

    public void Initialize(
        Transform playerTarget,
        float speed,
        float stunTime,
        AudioClip ambientMp3,
        float hearingRadius,
        float ambientVol,
        AudioClip onCatchMp3,
        float onCatchVol,
        int quarterIndex = 0,
        StalkDirectionRole role = StalkDirectionRole.FrontApproacher,
        float customJumpscareHeight = 1.45f,
        float customJumpscareDistance = 1.75f,
        float customJumpscareDuration = 3.0f,
        float customStalkDuration = 8f,
        float customBrightness = 0.25f)
    {
        player = playerTarget;
        moveSpeed = speed;
        stunDuration = stunTime;
        catchClip = onCatchMp3;
        catchVolume = onCatchVol;
        AssignedQuarter = quarterIndex;
        Role = role;
        IsOutOfSegment = false;

        jumpscareCameraHeight = customJumpscareHeight;
        jumpscareDistance = customJumpscareDistance;
        jumpscareDuration = customJumpscareDuration;
        stalkDuration = customStalkDuration;
        enemyBrightness = customBrightness;

        StartStalkingPhase();
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
        eerieLight.intensity = 0.45f;

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
    }

    public void StartStalkingPhase()
    {
        isHunting = false;
        stalkTimer = stalkDuration * Random.Range(0.9f, 1.15f);
        if (eerieLight != null)
        {
            eerieLight.intensity = 0.45f;
            eerieLight.color = new Color(0.8f, 0.15f, 0.1f, 1f);
        }
    }

    // Llamado cuando el jugador avanza a otro cuarto del laberinto: el enemigo se queda quieto y hace despawn cuando no lo miras
    public void MarkAsOutOfSegment()
    {
        IsOutOfSegment = true;
        isHunting = false;
    }

    private void ApplyDarkTintToModel(float brightness)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            foreach (Material mat in rend.materials)
            {
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

    private void Update()
    {
        if (player == null || isPlayerDying) return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;

        // 1. Si el jugador ya salió del cuarto de este enemigo, se queda quieto y desaparece cuando no lo estás viendo
        if (IsOutOfSegment)
        {
            LookAtPlayerQuietly(toPlayer);
            if (!IsVisibleToPlayerCamera() || distanceToPlayer > 18f)
            {
                Destroy(gameObject);
            }
            return;
        }

        // 2. Si está aturdido por una roca
        if (isStunned)
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

        // 3. Comprobamos si el jugador está en la Zona Segura del Street Light central
        bool playerInSafeZone = MazeGenerator.Instance != null && MazeGenerator.Instance.IsPlayerInSafeZone();
        bool monsterNearSafeZone = MazeGenerator.Instance != null && MazeGenerator.Instance.IsPositionInSafeZone(transform.position, 1.2f);

        if (playerInSafeZone || monsterNearSafeZone)
        {
            // En la zona segura todos los monstruos se fuerzan a modo acecho y no pueden entrar al radio de luz
            isHunting = false;
            stalkTimer = Mathf.Max(stalkTimer, 4.0f);
            if (eerieLight != null) eerieLight.intensity = 0.45f;

            if (monsterNearSafeZone || distanceToPlayer <= stalkKeepDistance)
            {
                LookAtPlayerQuietly(toPlayer);
                return;
            }
        }

        // 4. Ataque si llega a distancia de captura (y el jugador no está bajo el Street Light)
        if (!playerInSafeZone && !isPlayerDying && distanceToPlayer <= killDistance)
        {
            isPlayerDying = true;
            StartCoroutine(KillPlayerRoutine());
            return;
        }

        // 5. Temporizador de Acecho -> Caza definitiva
        if (!isHunting && !playerInSafeZone)
        {
            stalkTimer -= Time.deltaTime;
            if (stalkTimer <= 0f)
            {
                isHunting = true;
                if (eerieLight != null)
                {
                    eerieLight.intensity = 1.35f;
                }
            }
        }

        Vector3 desiredDestination;
        float currentSpeed;

        if (!isHunting)
        {
            // MODO ACECHO:
            // Si su rol es Frontal, se acerca desde adelante hasta quedar a distancia de acecho frente a ti;
            // Si su rol es Trasero, busca posicionarse a tu espalda.
            if (distanceToPlayer <= stalkKeepDistance && distanceToPlayer >= 4.2f)
            {
                LookAtPlayerQuietly(toPlayer);
                return;
            }

            if (Role == StalkDirectionRole.RearStalker)
            {
                desiredDestination = player.position - (player.forward * stalkKeepDistance);
                desiredDestination.y = 0f;
            }
            else
            {
                desiredDestination = player.position + (player.forward * (stalkKeepDistance * 0.7f));
                desiredDestination.y = 0f;
            }

            currentSpeed = moveSpeed * 0.95f;
        }
        else
        {
            // MODO CAZA DEFINITIVA: Va directo hacia el jugador
            desiredDestination = player.position;
            currentSpeed = moveSpeed * huntSpeedMultiplier;
        }

        Vector3 nextWaypoint = desiredDestination;
        if (MazeGenerator.Instance != null)
        {
            nextWaypoint = MazeGenerator.Instance.GetNextPathStep(transform.position, desiredDestination);
        }

        // Evita pisar dentro del círculo del Street Light central
        if (MazeGenerator.Instance != null && MazeGenerator.Instance.IsPositionInSafeZone(nextWaypoint, 0.5f))
        {
            LookAtPlayerQuietly(toPlayer);
            return;
        }

        Vector3 moveDir = nextWaypoint - transform.position;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            moveDir.Normalize();
        }

        transform.position += moveDir * (currentSpeed * Time.deltaTime);

        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            walkAnimPhase += Time.deltaTime * (isHunting ? 18f : 12f);
            float swayZ = Mathf.Sin(walkAnimPhase) * 9f;
            float leanForward = isHunting ? 16f : 8f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot * Quaternion.Euler(leanForward, 0f, swayZ), Time.deltaTime * 10f);

            Vector3 pos = transform.position;
            pos.y = Mathf.Abs(Mathf.Sin(walkAnimPhase)) * 0.08f;
            transform.position = pos;
        }
    }

    private bool IsVisibleToPlayerCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 dirToEnemy = (transform.position + Vector3.up * 0.8f) - cam.transform.position;
        float angle = Vector3.Angle(cam.transform.forward, dirToEnemy);
        if (angle > 55f) return false;

        // Si hay una pared de árboles tapando la vista entre el jugador y el enemigo, no lo está viendo
        if (Physics.Raycast(cam.transform.position, dirToEnemy.normalized, out RaycastHit hit, dirToEnemy.magnitude))
        {
            if (hit.transform != transform && !hit.transform.IsChildOf(transform))
            {
                return false;
            }
        }
        return true;
    }

    private void LookAtPlayerQuietly(Vector3 toPlayer)
    {
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion lookRot = Quaternion.LookRotation(toPlayer.normalized);
            float breathe = Mathf.Sin(Time.time * 3f) * 2.5f;
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot * Quaternion.Euler(breathe, 0f, 0f), Time.deltaTime * 6f);

            Vector3 pos = transform.position;
            pos.y = 0f;
            transform.position = pos;
        }
    }

    public void Stun(float customDuration = -1f)
    {
        if (isPlayerDying) return;

        isStunned = true;
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
        isStunned = false;
        if (bodyCollider != null) bodyCollider.isTrigger = false;

        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
        Vector3 pos = transform.position;
        pos.y = 0f;
        transform.position = pos;

        StartStalkingPhase();

        if (monsterAmbientSource != null)
        {
            monsterAmbientSource.UnPause();
        }
    }

    private IEnumerator KillPlayerRoutine()
    {
        moveSpeed = 0f;
        isPlayerDying = true;

        foreach (GoblinEnemy other in FindObjectsOfType<GoblinEnemy>())
        {
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