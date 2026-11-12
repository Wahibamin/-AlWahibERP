using System.Security.Cryptography;
namespace Wahib.Infrastructure;

internal static class Passwords
{
    internal static (byte[] Salt,byte[] Hash) Hash(string password)
    {
        if (password.Length<10) throw new ArgumentException("كلمة المرور يجب أن تكون 10 أحرف على الأقل.");
        var salt=RandomNumberGenerator.GetBytes(16);
        return (salt,Rfc2898DeriveBytes.Pbkdf2(password,salt,350_000,HashAlgorithmName.SHA256,32));
    }
    internal static bool Verify(string password,byte[] salt,byte[] hash)
    {
        var attempt=Rfc2898DeriveBytes.Pbkdf2(password,salt,350_000,HashAlgorithmName.SHA256,32);
        return CryptographicOperations.FixedTimeEquals(attempt,hash);
    }
}
