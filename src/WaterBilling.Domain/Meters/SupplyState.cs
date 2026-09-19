namespace WaterBilling.Domain.Meters;

/// <summary>
/// Desired position of the relay-operated shut-off valve. The platform records
/// intent; the on-site gateway reconciles the physical valve and reports back.
/// Modelled as desired-vs-reported so a valve that fails to actuate is visible
/// rather than silently assumed.
/// </summary>
public enum SupplyState
{
    Open = 0,
    Closed = 1
}
