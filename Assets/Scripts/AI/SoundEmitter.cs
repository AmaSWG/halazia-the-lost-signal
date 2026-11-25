using System;
using UnityEngine;

public class SoundEmitter : MonoBehaviour
{
    public static event Action<Vector3, float> SoundEmitted;

    public float hearingRadius = 20f;

    [Header("Player Sounds")]
    public float runningFootstepRadius = 10f;
    public float gunshotRadius = 25f;

    [ContextMenu("Emit Test Sound")]
    public void EmitSound()
    {
        Emit(hearingRadius, "Test sound");
    }

    [ContextMenu("Test/Running Footstep")]
    public void EmitRunningFootstep()
    {
        Emit(runningFootstepRadius, "Running footstep");
    }

    [ContextMenu("Test/Gunshot")]
    public void EmitGunshot()
    {
        Emit(gunshotRadius, "Gunshot");
    }

    private void Emit(float radius, string soundType)
    {
        if (!Application.isPlaying)
            return;

        SoundEmitted?.Invoke(transform.position, radius);
        Debug.Log($"{soundType} emitted at {transform.position}");

    }
}