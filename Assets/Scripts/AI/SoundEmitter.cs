using System;
using UnityEngine;

public class SoundEmitter : MonoBehaviour
{
    public static event Action<Vector3, float> SoundEmitted;

    public float hearingRadius = 20f;

    [ContextMenu("Emit Test Sound")]
    public void EmitSound()
    {
        if (!Application.isPlaying)
            return;

        SoundEmitted?.Invoke(transform.position, hearingRadius);
        Debug.Log("Sound emitted at " + transform.position);
    }
}