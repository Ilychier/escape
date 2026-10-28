using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParabolicRockThrower : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Arrastra aquí la Main Camera del Player")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Arrastra aquí una roca pequeña de tus assets invernales")]
    [SerializeField] private GameObject rockPrefab;

    [Header("Física del Arco")]
    [Tooltip("Fuerza inicial del lanzamiento (puedes ajustarla jugando con la rueda del ratón)")]
    [SerializeField] private float throwForce = 9.5f;
    [SerializeField] private float minThrowForce = 4f;
    [SerializeField] private float maxThrowForce = 16f;
    [Tooltip("Impulso extra hacia arriba para que el arco sea bien pronunciado y bombeado")]
    [SerializeField] private float upwardArcBoost = 5.2f;
    [Tooltip("Multiplicador de gravedad para que la roca caiga pesada y obligue a calcular el tiro")]
    [SerializeField] private float gravityMultiplier = 2.5f;
    [Tooltip("Escala de la roca lanzada (ajústala si la roca de tu paquete es gigante)")]
    [SerializeField] private float rockScale = 0.22f;
    [Tooltip("Segundos de espera entre cada lanzamiento de piedra")]
    [SerializeField] private float throwCooldown = 2.5f;
    private float nextAllowedThrowTime = 0f;

    [Header("Impacto, Aturdimiento y Señuelo Sonoro")]
    [Tooltip("Radio en metros dentro del cual el impacto de la roca aturde al enemigo")]
    [SerializeField] private float rockStunRadius = 1.65f;
    [Tooltip("Radio en metros en el que el golpe de una roca contra el piso atrae enemigos")]
    [SerializeField] private float decoyNoiseRadius = 15f;

    [Header("Puntos del Arco en Pantalla")]
    [SerializeField] private int maxTrajectoryDots = 28;
    [SerializeField] private float timeStepBetweenDots = 0.055f;
    [SerializeField] private float dotBaseSize = 0.11f;
    [SerializeField] private Color arcColor = new Color(1f, 0.75f, 0.2f, 0.9f);
    [SerializeField] private Color impactPointColor = new Color(1f, 0.25f, 0.2f, 1f);
    [Tooltip("Color que toma la mira cuando el tiro va a aturdir a un enemigo con seguridad")]
    [SerializeField] private Color stunTargetLockColor = new Color(0.2f, 0.95f, 1f, 1f);

    [Header("Efecto de Sonido (.mp3)")]
    [Tooltip("Arrastra aquí tu .mp3 de lanzamiento (whoosh, impulso o lanzamiento)")]
    [SerializeField] private AudioClip throwRockSfx;
    [Range(0f, 1f)]
    [SerializeField] private float throwSfxVolume = 0.85f;

    private AudioSource throwAudioSource;

    private List<Transform> trajectoryDots = new List<Transform>();
    private Transform impactMarker;
    private GameObject trajectoryContainer;
    private Collider playerCollider;
    private Material dotMaterial;
    private Material impactMaterial;

    private bool isAiming = false;
    private Texture2D guiBarTex;

    private void Start()
    {
        playerCollider = GetComponent<Collider>();
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        CreateTrajectoryVisuals();
        SetTrajectoryVisible(false);

        // Reproductor de sonido en un objeto hijo para no interferir con otros audios
        GameObject sfxChild = new GameObject("ThrowSfxPlayer");
        sfxChild.transform.SetParent(transform);
        sfxChild.transform.localPosition = Vector3.zero;
        throwAudioSource = sfxChild.AddComponent<AudioSource>();
        throwAudioSource.playOnAwake = false;
        throwAudioSource.spatialBlend = 0f;
    }

    private void Update()
    {
        // Ocultar arco y bloquear disparo en Pausa, Jumpscare/GameOver o Victoria
        if (PauseManager.IsGamePaused || GoblinEnemy.IsPlayerDying || MazeGenerator.win)
        {
            SetTrajectoryVisible(false);
            isAiming = false;
            return;
        }

        if (Time.timeSinceLevelLoad < 0.3f)
        {
            SetTrajectoryVisible(false);
            return;
        }

        // Si todavía está en cooldown, mantenemos el arco oculto y bloqueamos el disparo
        if (Time.time < nextAllowedThrowTime)
        {
            isAiming = false;
            SetTrajectoryVisible(false);
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            isAiming = true;
        }

        if (isAiming && Input.GetMouseButton(0))
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                throwForce = Mathf.Clamp(throwForce + scroll * 6f, minThrowForce, maxThrowForce);
            }

            SetTrajectoryVisible(true);
            DrawParabolicTrajectory();
        }
        else if (isAiming && Input.GetMouseButtonUp(0))
        {
            isAiming = false;
            SetTrajectoryVisible(false);
            ThrowRock();

            // Activamos el cooldown tras lanzar la piedra
            nextAllowedThrowTime = Time.time + throwCooldown;
        }
        else
        {
            isAiming = false;
            SetTrajectoryVisible(false);
        }
    }

    private Vector3 GetSpawnPoint()
    {
        // Sale ligeramente a la derecha y abajo de la cámara (como la mano de un niño)
        return cameraTransform.position
               + (cameraTransform.forward * 0.45f)
               + (cameraTransform.right * 0.22f)
               - (cameraTransform.up * 0.12f);
    }

    private Vector3 GetLaunchVelocity()
    {
        // Combinamos la dirección de la mirada con el impulso vertical pronunciado
        return (cameraTransform.forward * throwForce) + (Vector3.up * upwardArcBoost);
    }

    private Vector3 GetCustomGravity()
    {
        return Physics.gravity * gravityMultiplier;
    }

    private void DrawParabolicTrajectory()
    {
        Vector3 startPos = GetSpawnPoint();
        Vector3 initialVelocity = GetLaunchVelocity();
        Vector3 customGravity = GetCustomGravity();

        Vector3 previousPos = startPos;
        bool hitFound = false;
        bool willStunEnemy = false;

        for (int i = 0; i < maxTrajectoryDots; i++)
        {
            float t = (i + 1) * timeStepBetweenDots;
            // Ecuación exacta del movimiento parabólico: P(t) = P0 + V0*t + 0.5*g*t^2
            Vector3 currentPos = startPos + (initialVelocity * t) + (0.5f * customGravity * (t * t));

            Vector3 segment = currentPos - previousPos;
            float segmentLength = segment.magnitude;

            // Comprobamos si en este tramo la roca choca contra un enemigo, el suelo o una pared
            if (!hitFound && segmentLength > 0.001f &&
                Physics.SphereCast(previousPos, 0.12f, segment.normalized, out RaycastHit hit, segmentLength))
            {
                if (hit.collider != playerCollider)
                {
                    hitFound = true;
                    impactMarker.gameObject.SetActive(true);
                    impactMarker.position = hit.point + (hit.normal * 0.03f);
                    impactMarker.rotation = Quaternion.LookRotation(hit.normal);

                    willStunEnemy = WillImpactStunAnyEnemy(hit.point, hit.collider);
                }
            }

            if (!hitFound)
            {
                trajectoryDots[i].gameObject.SetActive(true);
                trajectoryDots[i].position = currentPos;

                // Los puntos se van haciendo más pequeños a lo largo de la curva
                float scaleFactor = Mathf.Lerp(1f, 0.35f, (float)i / maxTrajectoryDots);
                trajectoryDots[i].localScale = Vector3.one * (dotBaseSize * scaleFactor);
            }
            else
            {
                trajectoryDots[i].gameObject.SetActive(false);
            }

            previousPos = currentPos;
        }

        // Feedback UX: si el impacto va a aturdir a un enemigo, iluminamos la parábola en azul cian
        if (dotMaterial != null && impactMaterial != null)
        {
            dotMaterial.color = willStunEnemy ? stunTargetLockColor : arcColor;
            impactMaterial.color = willStunEnemy ? stunTargetLockColor : impactPointColor;
        }

        if (!hitFound)
        {
            impactMarker.gameObject.SetActive(false);
        }
    }

    private bool WillImpactStunAnyEnemy(Vector3 impactPoint, Collider hitCollider)
    {
        if (hitCollider != null && hitCollider.GetComponentInParent<GoblinEnemy>() != null)
        {
            return true;
        }

        for (int i = 0; i < GoblinEnemy.AllActiveEnemies.Count; i++)
        {
            GoblinEnemy enemy = GoblinEnemy.AllActiveEnemies[i];
            if (enemy == null) continue;

            float dist = Vector3.Distance(impactPoint, enemy.transform.position);
            if (dist <= rockStunRadius)
            {
                return true;
            }
        }
        return false;
    }

    private void ThrowRock()
    {
        if (throwRockSfx != null && throwAudioSource != null)
        {
            throwAudioSource.pitch = Random.Range(0.94f, 1.06f);
            throwAudioSource.PlayOneShot(throwRockSfx, throwSfxVolume);
        }

        Vector3 spawnPos = GetSpawnPoint();
        GameObject rockInstance;

        if (rockPrefab != null)
        {
            rockInstance = Instantiate(rockPrefab, spawnPos, Random.rotation);
            rockInstance.transform.localScale = Vector3.one * rockScale;
        }
        else
        {
            // Si aún no has arrastrado un prefab de roca, crea una piedra gris automáticamente
            rockInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rockInstance.transform.position = spawnPos;
            rockInstance.transform.localScale = Vector3.one * rockScale;
        }

        rockInstance.name = "ThrownRock";

        // Limpiamos colisionadores pesados del modelo y ponemos un SphereCollider limpio para que ruede bien
        foreach (Collider col in rockInstance.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }

        SphereCollider sphereCol = rockInstance.AddComponent<SphereCollider>();
        if (playerCollider != null)
        {
            Physics.IgnoreCollision(sphereCol, playerCollider, true);
        }

        Rigidbody rb = rockInstance.GetComponent<Rigidbody>();
        if (rb == null) rb = rockInstance.AddComponent<Rigidbody>();

        rb.mass = 1.5f;
        rb.useGravity = false; // Usaremos nuestra gravedad pronunciada personalizada
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Aplicamos la velocidad exacta del arco y un giro en el aire
        rb.AddForce(GetLaunchVelocity(), ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);

        // Añadimos el componente que aplica la gravedad pesada, el aturdimiento y el señuelo de ruido
        HeavyRockGravity heavyGrav = rockInstance.AddComponent<HeavyRockGravity>();
        heavyGrav.Initialize(GetCustomGravity(), rockStunRadius, decoyNoiseRadius);

        // Destruimos la roca a los 25 segundos para no acumular objetos
        Destroy(rockInstance, 25f);
    }

    private void CreateTrajectoryVisuals()
    {
        trajectoryContainer = new GameObject("TrajectoryArcDots");

        // Shader Sprites/Default para que los puntos del arco se vean brillantes en la noche
        dotMaterial = new Material(Shader.Find("Sprites/Default"));
        dotMaterial.color = arcColor;

        impactMaterial = new Material(Shader.Find("Sprites/Default"));
        impactMaterial.color = impactPointColor;

        for (int i = 0; i < maxTrajectoryDots; i++)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = $"ArcDot_{i}";
            dot.transform.SetParent(trajectoryContainer.transform);
            Destroy(dot.GetComponent<Collider>());

            Renderer rend = dot.GetComponent<Renderer>();
            rend.material = dotMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            trajectoryDots.Add(dot.transform);
        }

        // Creamos el punto donde caerá la roca
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "ImpactMarker";
        marker.transform.SetParent(trajectoryContainer.transform);
        marker.transform.localScale = new Vector3(0.28f, 0.28f, 0.05f);
        Destroy(marker.GetComponent<Collider>());

        Renderer markerRend = marker.GetComponent<Renderer>();
        markerRend.material = impactMaterial;
        markerRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markerRend.receiveShadows = false;

        impactMarker = marker.transform;
    }

    private void SetTrajectoryVisible(bool visible)
    {
        if (trajectoryContainer != null)
        {
            trajectoryContainer.SetActive(visible);
        }
    }

    private void OnGUI()
    {
        if (PauseManager.IsGamePaused || GoblinEnemy.IsPlayerDying || MazeGenerator.win)
            return;

        float remaining = nextAllowedThrowTime - Time.time;

        // Si ya no está en cooldown, no dibujamos absolutamente nada en pantalla
        if (remaining <= 0f) return;

        if (guiBarTex == null)
        {
            guiBarTex = new Texture2D(1, 1);
            guiBarTex.SetPixel(0, 0, Color.white);
            guiBarTex.Apply();
        }

        // Proporción descendente: empieza en 1 (llena) y baja hasta 0 conforme se agotan los segundos
        float descendingFill = Mathf.Clamp01(remaining / throwCooldown);

        float barWidth = 320f;
        float barHeight = 32f;
        float x = (Screen.width - barWidth) * 0.5f;
        float y = (Screen.height - barHeight) * 0.5f + 40f; // En la mitad de la pantalla, justo bajo la mira

        // 1. Marco oscuro semitransparente
        GUI.color = new Color(0.04f, 0.06f, 0.09f, 0.85f);
        GUI.DrawTexture(new Rect(x - 4f, y - 4f, barWidth + 8f, barHeight + 8f), guiBarTex);

        // 2. Barra que va descendiendo de tamaño
        GUI.color = new Color(0.45f, 0.5f, 0.58f, 0.9f); // color gris claro
        GUI.DrawTexture(new Rect(x, y, barWidth * descendingFill, barHeight), guiBarTex);

        // 3. Solo el número de segundos restantes (sin palabras)
        GUI.color = Color.white;
        GUIStyle timeStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 18
        };
        GUI.Label(new Rect(x, y, barWidth, barHeight), $"{remaining:F1}s", timeStyle);
    }
}

// Clase auxiliar que aplica la gravedad pesada, gestiona el aturdimiento y emite el señuelo de sonido
public class HeavyRockGravity : MonoBehaviour
{
    private Vector3 customGravity;
    private Rigidbody rb;
    private bool hasProcessedMainImpact = false;
    private float splashStunRadius = 1.65f;
    private float noiseDecoyRadius = 15f;

    public void Initialize(Vector3 gravity, float stunRadius = 1.65f, float decoyRadius = 15f)
    {
        customGravity = gravity;
        splashStunRadius = stunRadius;
        noiseDecoyRadius = decoyRadius;
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (rb != null)
        {
            rb.AddForce(customGravity, ForceMode.Acceleration);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasProcessedMainImpact) return;
        hasProcessedMainImpact = true;

        bool stunnedAny = false;

        // 1. Comprobamos si golpeó directamente a un Goblin
        GoblinEnemy directGoblin = collision.collider.GetComponentInParent<GoblinEnemy>();
        if (directGoblin != null)
        {
            directGoblin.Stun();
            stunnedAny = true;
        }

        // 2. Comprobamos si la roca estalló en el suelo o pared muy cerca de un Goblin
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, splashStunRadius);
        foreach (Collider col in nearbyColliders)
        {
            GoblinEnemy nearbyGoblin = col.GetComponentInParent<GoblinEnemy>();
            if (nearbyGoblin != null && nearbyGoblin != directGoblin)
            {
                nearbyGoblin.Stun();
                stunnedAny = true;
            }
        }

        // 3. Si NO aturdió a nadie, el golpe contra el suelo/pared emite ruido que atrae o distrae enemigos cercanos
        if (!stunnedAny)
        {
            GoblinEnemy.NotifyNoiseAtPosition(transform.position, noiseDecoyRadius);
        }

        // 4. Pequeña onda visual de impacto en el suelo para confirmar el golpe o el ruido en la oscuridad
        SpawnImpactPulseVisual(transform.position, stunnedAny);
    }

    private void SpawnImpactPulseVisual(Vector3 center, bool isStunHit)
    {
        GameObject pulseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pulseObj.name = "RockImpactPulse";
        Destroy(pulseObj.GetComponent<Collider>());

        pulseObj.transform.position = new Vector3(center.x, 0.04f, center.z);
        pulseObj.transform.localScale = new Vector3(0.2f, 0.01f, 0.2f);

        Renderer rend = pulseObj.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        Color baseColor = isStunHit
            ? new Color(0.2f, 0.95f, 1f, 0.55f)
            : new Color(0.85f, 0.85f, 0.85f, 0.28f);
        mat.color = baseColor;
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        RockPulseEffect pulseEffect = pulseObj.AddComponent<RockPulseEffect>();
        pulseEffect.Setup(isStunHit ? splashStunRadius * 2f : 2.6f, 0.32f, mat, baseColor);
    }
}

public class RockPulseEffect : MonoBehaviour
{
    private float targetDiameter;
    private float duration;
    private float elapsed;
    private Material pulseMat;
    private Color startColor;

    public void Setup(float maxDiameter, float lifeTime, Material mat, Color color)
    {
        targetDiameter = maxDiameter;
        duration = lifeTime;
        pulseMat = mat;
        startColor = color;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        float currentScale = Mathf.Lerp(0.2f, targetDiameter, 1f - Mathf.Pow(1f - t, 2f));
        transform.localScale = new Vector3(currentScale, 0.01f, currentScale);

        if (pulseMat != null)
        {
            Color c = startColor;
            c.a = Mathf.Lerp(startColor.a, 0f, t);
            pulseMat.color = c;
        }

        if (t >= 1f)
        {
            if (pulseMat != null) Destroy(pulseMat);
            Destroy(gameObject);
        }
    }
}