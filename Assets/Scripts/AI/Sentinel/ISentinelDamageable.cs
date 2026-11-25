namespace Halazia.AI.Sentinel
{
    /// <summary>Local damage integration seam: existing shared combat contract animates only.</summary>
    public interface ISentinelDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(float damage);
    }
}
