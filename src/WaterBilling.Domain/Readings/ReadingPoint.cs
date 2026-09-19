namespace WaterBilling.Domain.Readings;

/// <summary>
/// The only thing the consumption calculator needs to know about a reading.
/// Keeping it a struct with no entity reference is what makes the calculator
/// testable with three lines of setup and no database.
/// </summary>
public readonly record struct ReadingPoint(DateTimeOffset AtUtc, decimal TotalM3, decimal? FlowM3PerHour = null);
