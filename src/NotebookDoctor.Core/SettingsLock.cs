using System.Security.Cryptography;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// B38: senha das configurações (modo empresa). Guarda só o hash PBKDF2-SHA256 com sal aleatório, no formato
/// "pbkdf2-sha256$iterações$sal$hash" (sal e hash em Base64). A mesma linha serve para a política do Windows.
/// </summary>
public static class PasswordHash
{
    public const string Prefix = "pbkdf2-sha256";
    /// <summary>Recomendação da OWASP para PBKDF2-HMAC-SHA256.</summary>
    public const int Iterations = 600_000;
    public const int MinLength = 6;
    private const int SaltBytes = 16, HashBytes = 32;
    // Arquivo editado com um número absurdo travaria o app a cada verificação
    private const int MinIterations = 1_000, MaxIterations = 5_000_000;

    public static string Create(string password, int iterations = Iterations, byte[]? salt = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (!TryParse(stored, out int iterations, out byte[] salt, out byte[] expected)) return false;
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IsValidFormat(string? stored) => TryParse(stored, out _, out _, out _);

    /// <summary>Confere a senha nova. Devolve o motivo em português, ou null se estiver boa.</summary>
    public static string? CheckNew(string password, string confirm)
    {
        if (password.Length < MinLength) return $"Use pelo menos {MinLength} caracteres.";
        if (password.Trim().Length == 0) return "A senha não pode ser só espaços.";
        if (password != confirm) return "As duas senhas não são iguais.";
        return null;
    }

    private static bool TryParse(string? stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0; salt = hash = [];
        var parts = (stored ?? "").Trim().Split('$');
        if (parts.Length != 4 || parts[0] != Prefix) return false;
        if (!int.TryParse(parts[1], out iterations) || iterations is < MinIterations or > MaxIterations) return false;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }
        return salt.Length >= 8 && hash.Length >= 16;
    }
}

public enum UnlockResult { Ok, Wrong, Blocked }

/// <summary>
/// B38: depois da senha certa, as configurações ficam liberadas por alguns minutos (para não pedir a cada clique).
/// Depois de 5 erros seguidos, espera 30 segundos antes de aceitar outra tentativa.
/// </summary>
public sealed class LockSession
{
    public static readonly TimeSpan UnlockFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    public const int MaxTries = 5;

    private DateTime _unlockedUntil = DateTime.MinValue, _blockedUntil = DateTime.MinValue;
    private int _fails;

    public bool IsUnlocked(DateTime now) => now < _unlockedUntil;

    public TimeSpan UnlockedLeft(DateTime now) => IsUnlocked(now) ? _unlockedUntil - now : TimeSpan.Zero;

    public TimeSpan BlockedFor(DateTime now) => now < _blockedUntil ? _blockedUntil - now : TimeSpan.Zero;

    public UnlockResult TryUnlock(string password, string stored, DateTime now)
    {
        if (BlockedFor(now) > TimeSpan.Zero) return UnlockResult.Blocked;
        if (PasswordHash.Verify(password, stored))
        {
            _fails = 0;
            _unlockedUntil = now + UnlockFor;
            return UnlockResult.Ok;
        }
        if (++_fails >= MaxTries)
        {
            _fails = 0;
            _blockedUntil = now + Cooldown;
            return UnlockResult.Blocked;
        }
        return UnlockResult.Wrong;
    }

    /// <summary>Libera sem pedir senha: logo depois de a pessoa definir a senha nova.</summary>
    public void Grant(DateTime now) => _unlockedUntil = now + UnlockFor;

    public void Lock() => _unlockedUntil = DateTime.MinValue;

    /// <summary>Texto do cartão em Configurações.</summary>
    public static string Describe(bool hasPassword, bool fromPolicy, TimeSpan unlockedLeft)
    {
        if (!hasPassword) return Loc.T("Sem senha: qualquer pessoa que use este computador pode mudar as configurações.");
        string who = Loc.T(fromPolicy
            ? "Senha definida pela política da empresa (registro do Windows). Só um administrador pode trocá-la."
            : "Configurações protegidas por senha.");
        if (unlockedLeft <= TimeSpan.Zero) return Loc.F("{0} Bloqueado.", who);
        int min = Math.Max(1, (int)Math.Ceiling(unlockedLeft.TotalMinutes));
        return Loc.F("{0} Liberado por mais {1}.", who, Pt.N(min, "minuto", "minutos"));
    }
}
