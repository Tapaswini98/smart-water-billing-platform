namespace WaterBilling.Domain.Meters;

public enum MeterStatus
{
    /// <summary>Registered but not yet commissioned; ingestion is rejected.</summary>
    Provisioned = 0,

    /// <summary>Commissioned and ingesting.</summary>
    Active = 1,

    /// <summary>Temporarily out of service (maintenance). Ingestion accepted, billing flags it.</summary>
    Suspended = 2,

    /// <summary>Physically removed. Readings are retained for historical invoices.</summary>
    Decommissioned = 3
}
