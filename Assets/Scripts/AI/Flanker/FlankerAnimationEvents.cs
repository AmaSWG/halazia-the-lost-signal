using UnityEngine;

public sealed class FlankerAnimationEvents : MonoBehaviour
{
    [SerializeField] private FlankerShooter shooter;

    private void Awake()
    {
        if (shooter == null) shooter = GetComponentInParent<FlankerShooter>();
    }

    public void FlankerShot()
    {
        if (shooter != null) shooter.OnAnimationShot();
    }
}
