namespace WaterBilling.Infrastructure.Auth;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// <paramref name="rehashNeeded"/> is true when the stored hash used older
    /// parameters, so the caller can transparently upgrade it on next login.
    /// </summary>
    bool Verify(string hash, string password, out bool rehashNeeded);
}
