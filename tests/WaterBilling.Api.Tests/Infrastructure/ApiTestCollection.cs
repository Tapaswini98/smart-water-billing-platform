using Xunit;

namespace WaterBilling.Api.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL container and one host for the whole suite, not one per test
/// class. Tests in the same xunit collection run sequentially, which is what makes
/// sharing a single fixture safe without extra locking — and avoids starting three
/// Testcontainers just to check three different guarantees.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiTestCollection : ICollectionFixture<WaterBillingApiFactory>
{
    public const string Name = "Water Billing API";
}
