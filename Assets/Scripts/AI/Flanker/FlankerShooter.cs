using System;
using UnityEngine;
using Halazia.Shared.Combat;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

[RequireComponent(typeof(FlankerAgent))]
public class FlankerShooter : MonoBehaviour
{
    // Existing field names are retained so Unity keeps current assignments.
    [SerializeField] private Transform target;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float range = 15f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private float shotInterval = 1f;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private bool automaticFire = false;
    [SerializeField] private string shootingStateTag = "Shooting";
    [SerializeField] private string attackTrigger = "Attack";
    [SerializeField] private float sequenceTimeout = 5f;
    [SerializeField] private bool debugShots = true;
    [SerializeField, TextArea] private string automaticFireStatus = "Not running";

    [Header("Optional shot effects")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private AudioSource shotAudio;
    [SerializeField] private AudioClip shotSound;
    [SerializeField, Range(0f, 1f)] private float shotVolume = 0.6f;

    private FlankerAgent brain;
    private IAgentMotor motor;
    private LineRenderer tracer;
    private Material tracerMaterial;
    private float hideTracerAt;
    private float nextSequence;
    private float requestedAt;
    private bool requested;
    private bool wasShooting;
    private bool controllingFacing;
    private int lastShotFrame = -1;
    private int shotsThisSequence;

    private void Awake()
    {
        brain = GetComponent<FlankerAgent>();
        motor = GetComponent<IAgentMotor>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        var visual = new GameObject("ShotTracer");
        visual.transform.SetParent(transform, false);
        tracer = visual.AddComponent<LineRenderer>();
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = 0.025f;
        tracer.endWidth = 0.01f;
        tracer.startColor = Color.yellow;
        tracer.endColor = Color.yellow;
        tracer.enabled = false;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            tracerMaterial = new Material(shader);
            tracer.sharedMaterial = tracerMaterial;
        }
    }

    private void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    private bool Shooting()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return false;
        return animator.GetCurrentAnimatorStateInfo(0).IsTag(shootingStateTag) ||
            (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag(shootingStateTag));
    }

    private void Update()
    {
        if (Time.time >= hideTracerAt) tracer.enabled = false;
        bool shooting = Shooting();
        if (shooting && !wasShooting)
        {
            requested = false;
            shotsThisSequence = 0;
        }
        if (!shooting && wasShooting)
        {
            requested = false;
            nextSequence = Time.time + Mathf.Max(0.1f, shotInterval);
        }
        wasShooting = shooting;
        if (requested && Time.time - requestedAt > sequenceTimeout)
        {
            requested = false;
            nextSequence = Time.time + 1f;
        }

        string blocked = !automaticFire ? "Automatic Fire is OFF" :
            !brain.IsAlive ? "Flanker is dead" :
            motor == null ? "Missing AgentMotor" :
            target == null ? "Target is not assigned" :
            muzzle == null ? "Muzzle is not assigned" :
            animator == null ? "Animator is not assigned" :
            !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null ? "Animator is inactive or has no controller" :
            brain.CurrentTask != SquadTaskType.Flank &&
            brain.CurrentTask != SquadTaskType.Pursue
                ? $"Waiting for a combat order; current task is {brain.CurrentTask}" :
            motor.IsMovementLocked ? "Movement is locked" :
            motor.IsMoving ? "Moving to combat position" : null;
        if (blocked != null)
        {
            SetAutomaticStatus(blocked);
            ReleaseFacing();
            return;
        }
        if (!TryGetAim(out Vector3 aimPoint, out _))
        {
            DamageReceiver receiver = target.GetComponentInParent<DamageReceiver>();
            Collider collider = target.GetComponentInChildren<Collider>();
            Vector3 point = collider != null ? collider.bounds.center : target.position + Vector3.up;
            float distance = Vector3.Distance(muzzle.position, point);
            SetAutomaticStatus(receiver == null ? "Target has no DamageReceiver" :
                !receiver.IsAlive ? "Target is dead; restart Play mode to reset its health" :
                distance > range ? $"Target out of range: {distance:F1} m / {range:F1} m" :
                Cast((point-muzzle.position).normalized, out RaycastHit obstacle) ?
                    $"Line of sight blocked by {obstacle.collider.name}" : "Ray misses target; check target collider and Hit Mask");
            ReleaseFacing();
            return;
        }
        motor.SetFaceTarget(target);
        controllingFacing = true;
        Vector3 flat = aimPoint - transform.position;
        flat.y = 0f;
        if (shooting) { SetAutomaticStatus("Playing shooting sequence"); return; }
        if (requested) { SetAutomaticStatus("Attack requested; waiting for Shooting-tagged state"); return; }
        if (Time.time < nextSequence) { SetAutomaticStatus("Cooldown between bursts"); return; }
        float angle = Vector3.Angle(transform.forward, flat);
        if (angle > 12f) { SetAutomaticStatus($"Turning towards target: {angle:F0} degrees"); return; }
        // Only request the animation here. Damage comes exclusively from its shot events.
        requested = true;
        requestedAt = Time.time;
        animator.SetTrigger(attackTrigger);
        SetAutomaticStatus("Attack trigger sent");
    }

    private void SetAutomaticStatus(string value)
    {
        if (automaticFireStatus == value) return;
        automaticFireStatus = value;
        if (debugShots) Debug.Log($"[Flanker auto] {value}", this);
    }

    public bool CanEngageTarget(Transform candidate)
    {
        return isActiveAndEnabled && candidate != null && candidate == target &&
            TryGetAim(out _, out _);
    }

    private bool TryGetAim(out Vector3 aimPoint, out DamageReceiver receiver)
    {
        aimPoint = Vector3.zero;
        receiver = target != null ? target.GetComponentInParent<DamageReceiver>() : null;
        if (receiver == null || !receiver.IsAlive || muzzle == null) return false;
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        aimPoint = targetCollider != null ? targetCollider.bounds.center : target.position + Vector3.up;
        Vector3 delta = aimPoint - muzzle.position;
        if (delta.sqrMagnitude < 0.001f || delta.sqrMagnitude > range * range) return false;
        return Cast(delta.normalized, out RaycastHit hit) &&
            hit.collider.GetComponentInParent<DamageReceiver>() == receiver;
    }

    private bool Cast(Vector3 direction, out RaycastHit first)
    {
        first = default;
        if (muzzle == null) return false;
        RaycastHit[] hits = Physics.RaycastAll(muzzle.position, direction, range,
            hitMask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            first = hit;
            return true;
        }
        return false;
    }

    // Invoked by FlankerAnimationEvents on the same GameObject as the Animator.
    public void OnAnimationShot()
    {
        if (debugShots)
            Debug.Log($"[Flanker shot] Event received. Enabled={isActiveAndEnabled}, Alive={brain.IsAlive}, Muzzle={(muzzle != null ? muzzle.name : "NONE")}, Shooting={Shooting()}, Shots={shotsThisSequence}", this);
        if (!isActiveAndEnabled || !brain.IsAlive || muzzle == null || !Shooting() ||
            Time.frameCount == lastShotFrame || shotsThisSequence >= 5) return;
        lastShotFrame = Time.frameCount;
        shotsThisSequence++;
        if (muzzleFlash != null) muzzleFlash.Play(true);
        if (shotAudio != null && shotSound != null) shotAudio.PlayOneShot(shotSound, shotVolume);

        Vector3 direction = muzzle.forward;
        DamageReceiver intended = target != null ? target.GetComponentInParent<DamageReceiver>() : null;
        if (debugShots && intended == null)
            Debug.LogWarning("[Flanker shot] Target has no DamageReceiver on itself or a parent. Add DamageReceiver to Player_Target (the object assigned as Target).", this);
        if (target != null && intended != null && intended.IsAlive)
        {
            Collider c = target.GetComponentInChildren<Collider>();
            Vector3 point = c != null ? c.bounds.center : target.position + Vector3.up;
            if ((point - muzzle.position).sqrMagnitude > 0.001f)
                direction = (point - muzzle.position).normalized;
        }
        Vector3 impact = muzzle.position + direction * range;
        if (Cast(direction, out RaycastHit hit))
        {
            impact = hit.point;
            DamageReceiver receiver = hit.collider.GetComponentInParent<DamageReceiver>();
            if (debugShots)
                Debug.Log($"[Flanker shot] First hit={hit.collider.name}, distance={hit.distance:F2}, intended={(intended != null ? intended.name : "NONE")}, hit receiver={(receiver != null ? receiver.name : "NONE")}, target alive={(intended != null && intended.IsAlive)}", this);
            if (receiver != null && receiver == intended && receiver.IsAlive) receiver.TakeDamage(damage);
        }
        else if (debugShots)
            Debug.LogWarning($"[Flanker shot] Ray missed all colliders within {range:F1} metres. Check target collider, Hit Mask and muzzle position.", this);
        tracer.SetPosition(0, muzzle.position);
        tracer.SetPosition(1, impact);
        tracer.enabled = true;
        hideTracerAt = Time.time + 0.06f;
    }

    private void ReleaseFacing()
    {
        if (!controllingFacing || motor == null) return;
        motor.SetFaceTarget((Transform)null);
        controllingFacing = false;
    }

    private void OnDisable()
    {
        ReleaseFacing();
        requested = false;
        wasShooting = false;
        if (tracer != null) tracer.enabled = false;
    }

    private void OnDestroy()
    {
        if (tracerMaterial != null) Destroy(tracerMaterial);
    }
}
