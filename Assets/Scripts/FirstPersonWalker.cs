using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalker : MonoBehaviour
{
    [Header("Movimiento (Caminar y Correr)")]
    [SerializeField] private float walkSpeed = 3.0f;
    [Tooltip("Velocidad al mantener presionado Shift")]
    [SerializeField] private float runSpeed = 5.4f;
    [SerializeField] private float gravity = -15f;

    [Header("Resistencia y Cooldown de Cansancio (Shift)")]
    [Tooltip("Segundos máximos que puedes correr seguido antes de cansarte")]
    [SerializeField] private float maxRunDuration = 4.0f;
    [Tooltip("Si está activo, el cooldown dura exactamente lo que dure tu archivo .mp3 de cansado. Si lo desmarcas, usa Exhausted Cooldown Time.")]
    [SerializeField] private bool useTiredAudioLengthAsCooldown = true;
    [Tooltip("Segundos de cooldown sin poder correr cuando llegas al límite (si no usas la duración del audio)")]
    [SerializeField] private float exhaustedCooldownTime = 4.0f;
    [Tooltip("Velocidad a la que recuperas el aliento si sueltas Shift antes de llegar al límite")]
    [SerializeField] private float staminaRecoverySpeed = 1.0f;

    [Header("Sonido de Cansancio (.mp3)")]
    [Tooltip("Arrastra aquí tu .mp3 de respiración cansada / jadeo (tired)")]
    [SerializeField] private AudioClip tiredMp3;
    [Range(0f, 1f)]
    [SerializeField] private float tiredVolume = 0.85f;

    [Header("Cámara")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 2.0f;
    [SerializeField] private float maxLookAngle = 80f;

    [Header("Sonido de Pasos (.mp3)")]
    [Tooltip("Arrastra aquí tu archivo .mp3 de pasos caminando")]
    [SerializeField] private AudioClip footstepsMp3;
    [Range(0f, 1f)]
    [SerializeField] private float footstepsVolume = 0.65f;
    [Tooltip("Aceleración del sonido de pasos cuando vas corriendo con Shift")]
    [SerializeField] private float runFootstepsPitch = 1.35f;

    private CharacterController controller;
    private AudioSource footstepsAudioSource;
    private AudioSource tiredAudioSource;

    private float verticalRotation = 0f;
    private float verticalVelocity = 0f;

    private float currentRunTime = 0f;
    private bool isExhausted = false;
    private float exhaustedTimer = 0f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 1. Objeto hijo aislado para el reproductor de pasos
        if (footstepsMp3 != null)
        {
            GameObject footstepChild = new GameObject("FootstepsAudioPlayer");
            footstepChild.transform.SetParent(transform);
            footstepChild.transform.localPosition = Vector3.zero;

            footstepsAudioSource = footstepChild.AddComponent<AudioSource>();
            footstepsAudioSource.clip = footstepsMp3;
            footstepsAudioSource.loop = true;
            footstepsAudioSource.playOnAwake = false;
            footstepsAudioSource.spatialBlend = 0f;
            footstepsAudioSource.volume = footstepsVolume;
        }

        // 2. Objeto hijo aislado para el reproductor de cansancio (tired)
        GameObject tiredChild = new GameObject("TiredAudioPlayer");
        tiredChild.transform.SetParent(transform);
        tiredChild.transform.localPosition = Vector3.zero;

        tiredAudioSource = tiredChild.AddComponent<AudioSource>();
        tiredAudioSource.loop = false;
        tiredAudioSource.playOnAwake = false;
        tiredAudioSource.spatialBlend = 0f;
        tiredAudioSource.volume = tiredVolume;
    }

    private void Update()
    {   
        if (PauseManager.IsGamePaused)
        return;

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        // 1. Rotación de la vista con el ratón
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        if (cameraTransform != null)
        {
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -maxLookAngle, maxLookAngle);
            cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        // 2. Entrada de movimiento
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");
        bool isMoving = (Mathf.Abs(moveX) > 0.1f || Mathf.Abs(moveZ) > 0.1f);
        bool wantsToRun = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // 3. Lógica de correr y Cooldown por agotamiento (con audio de cansado)
        bool isRunningNow = false;

        if (isExhausted)
        {
            exhaustedTimer -= Time.deltaTime;

            // Si usamos la duración del audio, espera a que termine de sonar; si usamos tiempo manual, espera a que el contador llegue a 0
            bool audioFinished = (tiredMp3 != null && useTiredAudioLengthAsCooldown)
                ? !tiredAudioSource.isPlaying && exhaustedTimer <= 0f
                : exhaustedTimer <= 0f;

            if (audioFinished)
            {
                if (tiredAudioSource.isPlaying)
                {
                    tiredAudioSource.Stop();
                }
                isExhausted = false;
                currentRunTime = 0f;
            }
        }
        else
        {
            if (isMoving && wantsToRun)
            {
                isRunningNow = true;
                currentRunTime += Time.deltaTime;

                // Si llega al límite de tiempo corriendo, entra en Cooldown y reproduce el audio de cansado
                if (currentRunTime >= maxRunDuration)
                {
                    TriggerExhaustion();
                    isRunningNow = false;
                }
            }
            else if (currentRunTime > 0f)
            {
                // Si suelta Shift antes de agotarse por completo, recupera aliento poco a poco
                currentRunTime = Mathf.Max(0f, currentRunTime - Time.deltaTime * staminaRecoverySpeed);
            }
        }

        // 4. Aplicar velocidad según si está corriendo o caminando
        float activeSpeed = isRunningNow ? runSpeed : walkSpeed;
        Vector3 move = (transform.right * moveX + transform.forward * moveZ) * activeSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);

        // 5. Sonido de pasos (acelera el ritmo si va corriendo)
        if (footstepsAudioSource != null)
        {
            footstepsAudioSource.volume = footstepsVolume;
            footstepsAudioSource.pitch = isRunningNow ? runFootstepsPitch : 1.0f;

            bool shouldPlaySteps = isMoving && controller.isGrounded;
            if (shouldPlaySteps && !footstepsAudioSource.isPlaying)
            {
                footstepsAudioSource.Play();
            }
            else if (!shouldPlaySteps && footstepsAudioSource.isPlaying)
            {
                footstepsAudioSource.Pause();
            }
        }
    }

    private void TriggerExhaustion()
    {
        isExhausted = true;

        if (tiredMp3 != null)
        {
            tiredAudioSource.clip = tiredMp3;
            tiredAudioSource.volume = tiredVolume;
            tiredAudioSource.loop = !useTiredAudioLengthAsCooldown;
            tiredAudioSource.Play();

            exhaustedTimer = useTiredAudioLengthAsCooldown ? tiredMp3.length : exhaustedCooldownTime;
        }
        else
        {
            exhaustedTimer = exhaustedCooldownTime;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && !PauseManager.IsGamePaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}