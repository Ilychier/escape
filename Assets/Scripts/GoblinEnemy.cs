using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoblinEnemy : MonoBehaviour
{
    [Header("Persecución y Ataque")]
    [SerializeField] private float moveSpeed = 2.3f;
    [SerializeField] private float killDistance = 1f;
    [Tooltip("Altura de la cara del nuevo monstruo desde el suelo (ej. 2.0 o 2.2 metros)")]
    [SerializeField] private float monsterFaceHeight = 2.3f;
    [SerializeField] private float stunDuration = 4.5f;

    private Transform player;
    private CapsuleCollider bodyCollider;
    private Light eerieLight;
    private AudioSource monsterAmbientSource;
    private AudioClip catchClip;
    private float catchVolume = 1f;

    private bool isStunned = false;
    private float stunTimer = 0f;
    private float walkAnimPhase = 0f;
    private static bool isPlayerDying = false;

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
        float onCatchVol)
    {
        player = playerTarget;
        moveSpeed = speed;
        stunDuration = stunTime;
        catchClip = onCatchMp3;
        catchVolume = onCatchVol;

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
        eerieLight.intensity = 1.2f;

        if (ambientMp3 != null)
        {
            monsterAmbientSource = gameObject.AddComponent<AudioSource>();
            monsterAmbientSource.clip = ambientMp3;
            monsterAmbientSource.loop = false;
            monsterAmbientSource.playOnAwake = false;
            monsterAmbientSource.spatialBlend = 1f;
            monsterAmbientSource.rolloffMode = AudioRolloffMode.Linear;
            monsterAmbientSource.minDistance = 1.2f;
            monsterAmbientSource.maxDistance = hearingRadius;
            monsterAmbientSource.volume = ambientVol;
            monsterAmbientSource.Play();
        }
    }

    private void Update()
    {
        if (player == null || isPlayerDying) return;

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

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;

        if (distanceToPlayer <= killDistance)
        {
            StartCoroutine(KillPlayerRoutine());
            return;
        }

        Vector3 nextWaypoint = player.position;
        if (MazeGenerator.Instance != null)
        {
            nextWaypoint = MazeGenerator.Instance.GetNextPathStep(transform.position, player.position);
        }

        Vector3 moveDir = nextWaypoint - transform.position;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            moveDir.Normalize();
        }

        transform.position += moveDir * (moveSpeed * Time.deltaTime);

        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            walkAnimPhase += Time.deltaTime * 14f;
            float swayZ = Mathf.Sin(walkAnimPhase) * 9f;
            float leanForward = 12f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot * Quaternion.Euler(leanForward, 0f, swayZ), Time.deltaTime * 10f);

            Vector3 pos = transform.position;
            pos.y = Mathf.Abs(Mathf.Sin(walkAnimPhase)) * 0.08f;
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

        if (eerieLight != null) eerieLight.color = new Color(1f, 0.2f, 0.1f, 1f);

        if (monsterAmbientSource != null)
        {
            monsterAmbientSource.UnPause();
        }
    }

    private IEnumerator KillPlayerRoutine()
    {   
        moveSpeed = 0f;
        isPlayerDying = true;

        FirstPersonWalker walker = player.GetComponent<FirstPersonWalker>();
        if (walker != null) walker.enabled = false;

        ParabolicRockThrower thrower = player.GetComponent<ParabolicRockThrower>();
        if (thrower != null) thrower.enabled = false;

        if (monsterAmbientSource != null) monsterAmbientSource.Stop();

        if (catchClip != null)
        {
            AudioSource catchAudio = gameObject.AddComponent<AudioSource>();
            catchAudio.clip = catchClip;
            catchAudio.spatialBlend = 0f;
            catchAudio.volume = catchVolume;
            catchAudio.Play();
        }

        if (eerieLight != null)
        {
            eerieLight.intensity = 12f;
            eerieLight.color = Color.red;
        }

        // 1.2 segundos de susto donde la cámara se gira hacia el monstruo
        float timer = 0f;
        Camera cam = Camera.main;
        while (timer < 1.2f)
        {
            timer += Time.deltaTime;
            if (cam != null)
            {
                Vector3 lookTarget = transform.position + Vector3.up * monsterFaceHeight;
                cam.transform.rotation = Quaternion.Slerp(
                    cam.transform.rotation,
                    Quaternion.LookRotation(lookTarget - cam.transform.position),
                    Time.deltaTime * 18f
                );
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
            // Red de rescate: enciende directamente GameOverCanvas y libera el cursor
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
    }
}