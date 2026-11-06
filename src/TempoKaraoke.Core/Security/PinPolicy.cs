namespace TempoKaraoke.Security;

public readonly record struct PinValidationResult(bool IsValid, string ErrorCode)
{
    public static PinValidationResult Ok => new(true, string.Empty);
}

/// <summary>Validation rules shared by the settings UI and the tests.</summary>
public static class PinPolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 8;

    private static readonly HashSet<string> CommonPins = new(StringComparer.Ordinal)
    {
        "0000", "1111", "2222", "3333", "4444", "5555", "6666", "7777", "8888", "9999",
        "1234", "12345", "123456", "4321", "111111", "000000", "1212", "112233",
    };

    public static PinValidationResult Validate(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return new PinValidationResult(false, "pin_required");
        }

        if (pin.Length < MinLength || pin.Length > MaxLength)
        {
            return new PinValidationResult(false, "pin_length");
        }

        if (!pin.All(character => character is >= '0' and <= '9'))
        {
            return new PinValidationResult(false, "pin_digits_only");
        }

        return PinValidationResult.Ok;
    }

    /// <summary>Advisory only: weak PINs are allowed but the UI suggests changing them.</summary>
    public static bool IsCommon(string? pin) =>
        !string.IsNullOrWhiteSpace(pin) && CommonPins.Contains(pin);
}
