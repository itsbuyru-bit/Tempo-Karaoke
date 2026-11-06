using System.Security.Cryptography;
using System.Text;

namespace TempoKaraoke.Security;

/// <summary>Stored representation of a PIN. The plaintext is never persisted.</summary>
public sealed class PinHashRecord
{
    public const string CurrentAlgorithm = "PBKDF2-SHA256";

    public string Algorithm { get; set; } = CurrentAlgorithm;

    public string Salt { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public int Iterations { get; set; } = PinHasher.DefaultIterations;

    public bool IsUsable =>
        string.Equals(Algorithm, CurrentAlgorithm, StringComparison.Ordinal) &&
        Iterations > 0 &&
        !string.IsNullOrWhiteSpace(Salt) &&
        !string.IsNullOrWhiteSpace(Hash);

    public PinHashRecord Clone() => new()
    {
        Algorithm = Algorithm,
        Salt = Salt,
        Hash = Hash,
        Iterations = Iterations,
    };
}

/// <summary>PBKDF2-SHA256 hashing for persistent Host/Admin PINs.</summary>
public static class PinHasher
{
    public const int DefaultIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static PinHashRecord Hash(string pin, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        if (iterations < 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Use at least 10 000 iterations.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return new PinHashRecord
        {
            Algorithm = PinHashRecord.CurrentAlgorithm,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            Iterations = iterations,
        };
    }

    public static bool Verify(string? pin, PinHashRecord? record)
    {
        if (string.IsNullOrEmpty(pin) || record is null || !record.IsUsable)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(record.Salt);
            var expected = Convert.FromBase64String(record.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                pin,
                salt,
                record.Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Constant-time comparison for the randomly generated PINs.</summary>
    public static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
