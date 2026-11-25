using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Halazia.Interaction
{
    /// <summary>
    /// The collectible cyan crystal inside the Artifact Holder chamber. It can only be collected once.
    /// On collection it flares, shrinks away in place and is then deactivated; the Holder itself stays.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArtifactCrystal : MonoBehaviour
    {
        [SerializeField] private string itemId = "Artifact_Crystal";

        [Tooltip("Optional glow light inside the chamber. It pulses gently and fades out when the crystal is taken.")]
        [SerializeField] private Light glowLight;
        [SerializeField, Min(0f)] private float glowPulse = 0.18f;
        [SerializeField, Min(0.01f)] private float vanishDuration = 0.45f;

        [Tooltip("Raised once, when the crystal is collected. Hook game logic (inventory, objectives) here.")]
        [SerializeField] private UnityEvent onCollected = new UnityEvent();

        public event Action<ArtifactCrystal> Collected;

        public string ItemId => itemId;
        public bool IsCollected { get; private set; }

        private float baseIntensity;
        private Vector3 baseScale;

        private void Awake()
        {
            baseScale = transform.localScale;
            if (glowLight != null) baseIntensity = glowLight.intensity;
        }

        private void Update()
        {
            if (IsCollected || glowLight == null) return;
            glowLight.intensity = baseIntensity * (1f + glowPulse * Mathf.Sin(Time.time * 2.2f));
        }

        /// <summary>Collects the crystal. Returns false if it was already collected.</summary>
        public bool Collect()
        {
            if (IsCollected) return false;
            IsCollected = true;
            Collected?.Invoke(this);
            onCollected.Invoke();
            if (isActiveAndEnabled) StartCoroutine(Vanish());
            else FinishVanish();
            return true;
        }

        private IEnumerator Vanish()
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / vanishDuration);
                // brief flare, then shrink to nothing in place
                float s = t < 0.25f ? Mathf.Lerp(1f, 1.15f, t / 0.25f) : Mathf.Lerp(1.15f, 0f, (t - 0.25f) / 0.75f);
                transform.localScale = baseScale * s;
                if (glowLight != null) glowLight.intensity = baseIntensity * (t < 0.25f ? Mathf.Lerp(1f, 2.5f, t / 0.25f) : Mathf.Lerp(2.5f, 0f, (t - 0.25f) / 0.75f));
                yield return null;
            }
            FinishVanish();
        }

        private void FinishVanish()
        {
            if (glowLight != null) glowLight.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
