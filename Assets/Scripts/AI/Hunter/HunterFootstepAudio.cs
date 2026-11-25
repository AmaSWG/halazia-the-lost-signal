
using UnityEngine;
using Halazia.Shared.Movement;

[RequireComponent(typeof(AudioSource))]
public class HunterFootstepAudio : MonoBehaviour
{
    [SerializeField] private AudioClip footstepClip;
    [SerializeField] private float movementThreshold = 0.1f;

    private AgentMotor motor;
    private AudioSource audioSource;

    private void Awake()
    {
        motor = GetComponent<AgentMotor>();
        audioSource = GetComponent<AudioSource>();

        audioSource.clip = footstepClip;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
    }

    private void Update()
    {
        if (motor == null || footstepClip == null)
            return;

        if (motor.CurrentSpeed > movementThreshold)
        {
            if (!audioSource.isPlaying)
                audioSource.Play();
        }
        else
        {
            if (audioSource.isPlaying)
                audioSource.Stop();
        }
    }
}
