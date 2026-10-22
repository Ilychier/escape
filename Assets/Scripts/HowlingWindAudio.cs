using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class HowlingWindAudio : MonoBehaviour
{
    [Header("Audio Personalizado (Opcional)")]
    [Tooltip("Si tienes un MP3/WAV de viento, arrástralo aquí. Si lo dejas vacío, se generará viento procedural automáticamente.")]
    [SerializeField] private AudioClip customWindClip;

    [Header("Intensidad de las Ráfagas")]
    [Range(0f, 1f)]
    [SerializeField] private float baseVolume = 0.35f;
    [Range(0f, 1f)]
    [SerializeField] private float gustVolumeBoost = 0.40f;
    [SerializeField] private float gustSpeed = 0.35f;

    private AudioSource audioSource;
    private bool useProceduralWind;

    // Variables del sintetizador procedural de viento (hilo de audio)
    private System.Random audioRng = new System.Random();
    private float filterLow;
    private float filterBand;
    private double dspPhase1;
    private double dspPhase2;
    private int sampleRate;
    private volatile float currentGustFactor = 0.5f;

    private void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = true;
        audioSource.spatialBlend = 0f; // 2D envolvente en toda la escena

        if (customWindClip != null)
        {
            useProceduralWind = false;
            audioSource.clip = customWindClip;
            audioSource.Play();
        }
        else
        {
            useProceduralWind = true;
            // En Unity 2021+/6, un AudioSource necesita estar en Play() para activar OnAudioFilterRead
            AudioClip dummyClip = AudioClip.Create("SilentCarrier", sampleRate, 1, sampleRate, false);
            audioSource.clip = dummyClip;
            audioSource.Play();
        }
    }

    private void Update()
    {
        // Calculamos ráfagas irregulares con Perlin Noise
        float noise = Mathf.PerlinNoise(Time.time * gustSpeed, 10.5f);
        currentGustFactor = noise;

        if (!useProceduralWind)
        {
            audioSource.volume = baseVolume + (noise * gustVolumeBoost);
            audioSource.pitch = Mathf.Lerp(0.82f, 1.18f, noise);
        }
        else
        {
            audioSource.volume = Mathf.Clamp01(baseVolume + (noise * gustVolumeBoost));
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!useProceduralWind) return;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Ruido blanco base
            float whiteNoise = (float)(audioRng.NextDouble() * 2.0 - 1.0);

            // Dos ondas lentas para modular el silbido helado del viento
            dspPhase1 += 0.22 / sampleRate;
            dspPhase2 += 0.09 / sampleRate;
            if (dspPhase1 > 1.0) dspPhase1 -= 1.0;
            if (dspPhase2 > 1.0) dspPhase2 -= 1.0;

            float wave1 = Mathf.Sin((float)(dspPhase1 * 2.0 * Mathf.PI));
            float wave2 = Mathf.Sin((float)(dspPhase2 * 2.0 * Mathf.PI));
            float modulation = (wave1 * 0.6f + wave2 * 0.4f) * 0.5f + 0.5f;

            // Filtro de estado variable (State Variable Filter) para crear el aullido resonante
            float cutoffFrequency = Mathf.Lerp(0.015f, 0.055f, (modulation + currentGustFactor) * 0.5f);
            float resonance = 0.08f; // Cuanto más bajo, más silba el viento

            filterLow += cutoffFrequency * filterBand;
            float filterHigh = whiteNoise - filterLow - (resonance * filterBand);
            filterBand += cutoffFrequency * filterHigh;

            // Mezclamos el cuerpo grave del viento (filterLow) con el aullido agudo (filterBand)
            float windSample = (filterLow * 0.25f) + (filterBand * 0.85f);
            windSample = Mathf.Clamp(windSample * 0.35f, -1f, 1f);

            for (int c = 0; c < channels; c++)
            {
                data[i + c] = windSample;
            }
        }
    }
}