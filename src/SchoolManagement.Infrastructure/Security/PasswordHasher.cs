namespace SchoolManagement.Infrastructure.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>bcrypt with work factor 12 (spec 5.5).</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

/// <summary>Minimum length, character mix and a check against the most common breached passwords (spec 5.5).</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "12345678", "123456789", "1234567890", "qwerty123",
        "11111111", "00000000", "abc12345", "iloveyou", "admin123", "welcome1", "letmein1",
        "bangladesh", "dhaka1234", "school123", "p@ssw0rd", "passw0rd", "qwertyuiop"
    };

    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            errors.Add($"Password must be at least {MinLength} characters.");
        else
        {
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
                errors.Add("Password must contain both letters and digits.");
            if (CommonPasswords.Contains(password))
                errors.Add("Password is too common.");
        }
        return errors;
    }
}
