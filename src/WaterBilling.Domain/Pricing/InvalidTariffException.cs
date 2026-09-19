namespace WaterBilling.Domain.Pricing;

/// <summary>
/// Thrown when pricing is attempted against a structurally invalid tariff. It should
/// be unreachable in practice — the same <see cref="TariffValidator"/> runs at plan
/// creation — so reaching it means a plan was written around the API.
/// </summary>
public sealed class InvalidTariffException : Exception
{
    public InvalidTariffException(Guid planVersionId, IReadOnlyList<string> errors)
        : base($"Pricing plan version {planVersionId} is invalid: {string.Join(" ", errors)}")
    {
        PlanVersionId = planVersionId;
        Errors = errors;
    }

    public Guid PlanVersionId { get; }

    public IReadOnlyList<string> Errors { get; }
}
