using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Halazia.Interaction
{
    /// <summary>
    /// Player interaction for the GV Artifact Holder: E opens/closes the hinged chamber door, and once the door
    /// is open, E collects the crystal when the player is in reach and looking at it.
    /// It does not touch the player controller: it reads the existing player's camera (assign
    /// <see cref="interactor"/>, or it uses Camera.main, like the ship's sliding doors) and the existing E key.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArtifactHolderInteraction : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] private ArtifactHolderDoor door;
        [SerializeField] private ArtifactCrystal crystal;

        [Header("Viewer")]
        [Tooltip("The player's camera (eye). If empty, Camera.main is used.")]
        [SerializeField] private Transform interactor;

        [Header("Door interaction")]
        [Tooltip("Direction (in this object's local space) the chamber door faces. The player must stand on this side.")]
        [SerializeField] private Vector3 localFront = Vector3.right;
        [Tooltip("Max horizontal distance from the Holder's centre line to the player's eye.")]
        [SerializeField, Min(0.5f)] private float interactRange = 2.4f;
        [Tooltip("Max angle (degrees) between the door's facing and the direction to the player. Kept narrow so the " +
                 "chamber's sliding doors (3 m E range) never react to the same key press.")]
        [SerializeField, Range(10f, 90f)] private float frontArc = 40f;
        [SerializeField, Min(0.1f)] private float maxHeightDifference = 3f;
        [Tooltip("Max angle between the view direction and the Holder for the door prompt.")]
        [SerializeField, Range(10f, 90f)] private float lookAngle = 55f;
        [Tooltip("Height range of the Holder's centre line (local Y) used for look checks.")]
        [SerializeField] private Vector2 localHeightRange = new Vector2(0.6f, 3.1f);

        [Header("Crystal")]
        [Tooltip("The door must be at least this far open (0..1) before the crystal can be taken.")]
        [SerializeField, Range(0.1f, 1f)] private float doorOpenToCollect = 0.85f;
        [SerializeField, Min(0.5f)] private float crystalReach = 2.6f;
        [SerializeField, Range(5f, 45f)] private float crystalAimAngle = 22f;

        [Header("Prompts")]
        [SerializeField] private string openPrompt = "Press E to Open";
        [SerializeField] private string closePrompt = "Press E to Close";
        [SerializeField] private string collectPrompt = "Press E to Collect Crystal";
        [SerializeField] private string collectedMessage = "Crystal Collected!";
        [SerializeField, Min(0.5f)] private float collectedMessageSeconds = 2.5f;

        public enum Prompt { None, Open, Close, Collect }

        public ArtifactHolderDoor Door => door;
        public ArtifactCrystal Crystal => crystal;
        public Prompt CurrentPrompt { get; private set; }
        public bool PlayerInRange { get; private set; }
        public bool CanCollectCrystal { get; private set; }
        public string CollectedMessage => collectedMessage;
        public bool ShowingCollectedMessage => Time.time < messageUntil;

        private Transform viewer;
        private float messageUntil = -1f;
        private GUIStyle promptStyle, messageStyle;

        private void Reset()
        {
            door = GetComponentInChildren<ArtifactHolderDoor>();
            crystal = GetComponentInChildren<ArtifactCrystal>(true);
        }

        private void Update()
        {
            Evaluate();
            if (InteractPressed()) TryInteract();
        }

        /// <summary>Recomputes range/prompt state for the current viewer.</summary>
        public void Evaluate()
        {
            viewer = ResolveViewer();
            PlayerInRange = viewer != null && door != null && IsInDoorZone(viewer);
            CanCollectCrystal = PlayerInRange && CrystalTargetable(viewer);
            CurrentPrompt = CanCollectCrystal ? Prompt.Collect
                          : PlayerInRange ? (door.IsOpen ? Prompt.Close : Prompt.Open)
                          : Prompt.None;
        }

        /// <summary>What pressing E does right now. Returns true if something happened.</summary>
        public bool TryInteract()
        {
            Evaluate();
            if (CanCollectCrystal)
            {
                if (crystal.Collect()) messageUntil = Time.time + collectedMessageSeconds;
                Evaluate();
                return true;
            }
            if (PlayerInRange)
            {
                door.Toggle();
                Evaluate();
                return true;
            }
            return false;
        }

        private Transform ResolveViewer()
        {
            if (interactor != null) return interactor;
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private Vector3 FrontWorld()
        {
            Vector3 f = transform.TransformDirection(localFront); f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        private bool IsInDoorZone(Transform eye)
        {
            Vector3 centre = transform.position;
            Vector3 toEye = eye.position - centre;
            float dy = toEye.y; toEye.y = 0f;
            if (Mathf.Abs(dy) > maxHeightDifference) return false;
            if (toEye.sqrMagnitude > interactRange * interactRange) return false;
            if (toEye.sqrMagnitude < 1e-4f || Vector3.Angle(toEye, FrontWorld()) > frontArc) return false;

            // Looking at the Holder: compare with the nearest point on its centre line.
            float yMin = transform.TransformPoint(0f, localHeightRange.x, 0f).y;
            float yMax = transform.TransformPoint(0f, localHeightRange.y, 0f).y;
            Vector3 axisPoint = new Vector3(centre.x, Mathf.Clamp(eye.position.y, yMin, yMax), centre.z);
            return Vector3.Angle(eye.forward, axisPoint - eye.position) <= lookAngle;
        }

        private bool CrystalTargetable(Transform eye)
        {
            if (crystal == null || crystal.IsCollected || !crystal.gameObject.activeInHierarchy) return false;
            if (!door.IsOpen || door.OpenAmount < doorOpenToCollect) return false;
            Vector3 toCrystal = crystal.transform.position - eye.position;
            if (toCrystal.magnitude > crystalReach) return false;
            return Vector3.Angle(eye.forward, toCrystal) <= crystalAimAngle;
        }

        private static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------ on-screen prompts
        public string PromptText(Prompt p) => p switch
        {
            Prompt.Open => openPrompt,
            Prompt.Close => closePrompt,
            Prompt.Collect => collectPrompt,
            _ => string.Empty,
        };

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            EnsureStyles();

            if (ShowingCollectedMessage)
            {
                float remaining = messageUntil - Time.time;
                Color c = messageStyle.normal.textColor; c.a = Mathf.Clamp01(remaining / 0.5f);
                messageStyle.normal.textColor = c;
                GUI.Label(new Rect(0f, Screen.height * 0.28f, Screen.width, 60f), collectedMessage, messageStyle);
            }

            if (CurrentPrompt == Prompt.None) return;
            string text = PromptText(CurrentPrompt);
            Transform anchor = CurrentPrompt == Prompt.Collect ? crystal.transform : door.PromptAnchor;
            Vector2 size = promptStyle.CalcSize(new GUIContent(text)) + new Vector2(24f, 12f);
            Rect r = new Rect((Screen.width - size.x) * 0.5f, Screen.height - 120f, size.x, size.y);

            Camera cam = viewer != null ? viewer.GetComponent<Camera>() : null;
            if (cam != null && anchor != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(anchor.position);
                if (sp.z > 0f)
                {
                    float x = Mathf.Clamp(sp.x - size.x * 0.5f, 10f, Screen.width - size.x - 10f);
                    float y = Mathf.Clamp(Screen.height - sp.y + 18f, 10f, Screen.height - size.y - 10f);
                    r = new Rect(x, y, size.x, size.y);
                }
            }
            GUI.Box(r, text, promptStyle);
        }

        private void EnsureStyles()
        {
            if (promptStyle != null) return;
            var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.04f, 0.07f, 0.09f, 0.82f)); bg.Apply();
            promptStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(12, 12, 6, 6),
            };
            promptStyle.normal.background = bg;
            promptStyle.normal.textColor = new Color(0.55f, 0.92f, 1f);
            messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            messageStyle.normal.textColor = new Color(0.45f, 1f, 0.95f, 1f);
        }
    }
}
