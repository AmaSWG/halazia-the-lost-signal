using UnityEngine;

namespace Halazia.Interaction
{
    /// <summary>
    /// Hinged containment door of the GV Artifact Holder. The hinge transform sits on the hinge axis
    /// (centre of the hinge pillar) and swings smoothly about its parent's up axis. The door mesh is a
    /// child of the hinge, so it always stays attached to the Holder.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArtifactHolderDoor : MonoBehaviour
    {
        [Tooltip("Transform placed on the hinge axis. Rotated about the parent's up axis. Defaults to this transform.")]
        [SerializeField] private Transform hinge;

        [Tooltip("Signed opening angle in degrees about the hinge's up axis. The sign is chosen so the door swings " +
                 "outward, away from the chamber (checked against the cage geometry up to 105 degrees).")]
        [SerializeField, Range(-150f, 150f)] private float openAngle = -105f;

        [Tooltip("Seconds for a full open or close movement.")]
        [SerializeField, Min(0.05f)] private float swingDuration = 1.25f;

        [Tooltip("Optional point on the door used to place the on-screen prompt (the handle).")]
        [SerializeField] private Transform promptAnchor;

        private Quaternion closedLocalRotation;
        private float progress;        // 0 = closed, 1 = fully open (linear time)
        private bool targetOpen;
        private bool initialised;

        public bool IsOpen => targetOpen;
        public bool IsMoving => !Mathf.Approximately(progress, targetOpen ? 1f : 0f);
        /// <summary>Eased opening fraction 0..1 (how far open the door actually is).</summary>
        public float OpenAmount => Ease(progress);
        public float CurrentAngle => openAngle * OpenAmount;
        public float OpenAngle => openAngle;
        public Transform PromptAnchor => promptAnchor != null ? promptAnchor : Hinge;
        private Transform Hinge => hinge != null ? hinge : transform;

        private void Awake() => Initialise();

        private void Initialise()
        {
            if (initialised) return;
            closedLocalRotation = Hinge.localRotation;
            initialised = true;
        }

        public void Toggle() => SetOpen(!targetOpen);
        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);

        public void SetOpen(bool open)
        {
            Initialise();
            targetOpen = open;
        }

        /// <summary>Jumps straight to the open or closed pose (used by tools/tests, not by gameplay).</summary>
        public void SetOpenImmediate(bool open)
        {
            Initialise();
            targetOpen = open;
            progress = open ? 1f : 0f;
            Apply();
        }

        private void Update()
        {
            float goal = targetOpen ? 1f : 0f;
            if (Mathf.Approximately(progress, goal)) return;
            progress = Mathf.MoveTowards(progress, goal, Time.deltaTime / swingDuration);
            Apply();
        }

        private void Apply()
        {
            Hinge.localRotation = closedLocalRotation * Quaternion.AngleAxis(CurrentAngle, Vector3.up);
        }

        // Smooth ease-in-out, like a damped cabinet hinge.
        private static float Ease(float t) => t * t * (3f - 2f * t);
    }
}
