namespace FinanceApp.Mobile;

/// <summary>The phone's fingerprint (or face) check.</summary>
public interface IBiometrics
{
    /// <summary>The phone can check a fingerprint and one is enrolled.</summary>
    bool IsAvailable { get; }

    /// <returns><c>true</c> when the owner was recognised; <c>false</c> when cancelled or not recognised.</returns>
    Task<bool> AuthenticateAsync(string title);
}

/// <summary>Off the phone (tests) and on phones without a sensor.</summary>
public sealed class NoBiometrics : IBiometrics
{
    public bool IsAvailable => false;

    public Task<bool> AuthenticateAsync(string title) => Task.FromResult(false);
}
