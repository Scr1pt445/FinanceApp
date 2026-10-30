using System.Security.Cryptography;

namespace FinanceApp.Application.Settings;

/// <summary>Four digits, kept as a salted PBKDF2-SHA256 hash; compared in constant time.</summary>
public static class PinCode
{
    public const int Length = 4;

    private const int Iterations = 60_000;
    private const int HashBytes = 32;

    public static bool IsValid(string? pin) => pin is { Length: Length } && pin.All(char.IsAsciiDigit);

    /// <summary>A lock with a new salt for <paramref name="pin"/>.</summary>
    public static LockSettings Create(string pin, bool biometrics)
    {
        if (!IsValid(pin))
        {
            throw new ArgumentException($"PIN — {Length} цифры.", nameof(pin));
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        return new LockSettings { PinSalt = Convert.ToBase64String(salt), PinHash = Hash(pin, salt), Biometrics = biometrics };
    }

    public static bool Verify(LockSettings settings, string? pin)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsOn || !IsValid(pin))
        {
            return false;
        }

        var expected = Convert.FromBase64String(settings.PinHash!);
        var actual = Convert.FromBase64String(Hash(pin!, Convert.FromBase64String(settings.PinSalt!)));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Hash(string pin, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashBytes));
}
