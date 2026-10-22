using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BirdFlightController : MonoBehaviour
{
    [Header("Parámetros de Vuelo y Gravedad")]
    public float flapForce = 6f;          // Impulso hacia arriba (al hacer clic o espacio)
    public float gravityScale = 2.5f;     // Multiplicador de gravedad
    public float maxFallSpeed = -12f;     // Límite de velocidad de caída

    [Header("Movimiento en el Laberinto (X-Z)")]
    public float moveSpeed = 5f;          // Velocidad de desplazamiento horizontal
    public float forwardSpeed = 3f;       // Velocidad constante automática hacia adelante (opcional para el laberinto)

    private Rigidbody rb;
    private float horizontalInput;
    private float verticalInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // Controlamos la gravedad de forma manual
        // Congelamos la rotación para que el ave no ruede o caiga de lado al chocar
        rb.freezeRotation = true; 
    }

    void Update()
    {
        // 1. Capturar entradas de movimiento libre (A/D o Flechas Izq/Der para X, W/S para Z)
        horizontalInput = Input.GetAxis("Horizontal"); // A/D o Flechas
        verticalInput = Input.GetAxis("Vertical");     // W/S o Flechas (si quieres moverte hacia adelante/atrás además del avance automático)

        // 2. Impulso vertical (Flap / Gravedad)
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
        {
            Flap();
        }
    }

    void FixedUpdate()
    {
        // --- GRAVEDAD Y CAÍDA ---
        Vector3 customGravity = Physics.gravity * gravityScale;
        rb.AddForce(customGravity, ForceMode.Acceleration);

        if (rb.velocity.y < maxFallSpeed)
        {
            rb.velocity = new Vector3(rb.velocity.x, maxFallSpeed, rb.velocity.z);
        }

        // --- MOVIMIENTO HORIZONTAL (X y Z) ---
        // Calculamos la dirección basada en las teclas presionadas
        Vector3 moveDirection = new Vector3(horizontalInput, 0f, verticalInput + forwardSpeed).normalized;
        
        // Aplicamos la velocidad manteniendo la velocidad en Y actual (para no interferir con el salto/gravedad)
        Vector3 targetVelocity = new Vector3(moveDirection.x * moveSpeed, rb.velocity.y, (moveDirection.z + forwardSpeed) * moveSpeed);
        
        // Suavizamos el movimiento horizontal para que se sienta fluido
        rb.velocity = Vector3.Lerp(rb.velocity, targetVelocity, Time.fixedDeltaTime * 10f);
    }

    void Flap()
    {
        // Reseteamos el impulso vertical momentáneamente para que el salto sea consistente
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        rb.AddForce(Vector3.up * flapForce, ForceMode.Impulse);
    }
}