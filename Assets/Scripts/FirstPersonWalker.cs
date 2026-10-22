using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalker : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float gravity = -15f;

    [Header("Cámara")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 2.0f;
    [SerializeField] private float maxLookAngle = 80f;

    [Header("Sonido de Pasos (.mp3)")]
    [Tooltip("Arrastra aquí tu archivo .mp3 de pasos caminando")]
    [SerializeField] private AudioClip footstepsMp3;
    [Range(0f, 1f)]
    [SerializeField] private float footstepsVolume = 0.65f;

    private CharacterController controller;
    private AudioSource footstepsAudioSource;
    private float verticalRotation = 0f;
    private float verticalVelocity = 0f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Creamos el reproductor para tu .mp3 de pasos
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
    }

    private void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        if (cameraTransform != null)
        {
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -maxLookAngle, maxLookAngle);
            cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        Vector3 move = (transform.right * moveX + transform.forward * moveZ) * walkSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);

        // Reproducir o pausar el .mp3 de pasos según si te estás moviendo
        if (footstepsAudioSource != null)
        {
            footstepsAudioSource.volume = footstepsVolume;
            bool isWalking = (Mathf.Abs(moveX) > 0.1f || Mathf.Abs(moveZ) > 0.1f) && controller.isGrounded;

            if (isWalking && !footstepsAudioSource.isPlaying)
            {
                footstepsAudioSource.Play();
            }
            else if (!isWalking && footstepsAudioSource.isPlaying)
            {
                footstepsAudioSource.Pause();
            }
        }
    }
}