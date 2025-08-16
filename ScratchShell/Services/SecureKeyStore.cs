using System.IO;
using System.Security.Cryptography;

namespace ScratchShell.Services;

public static class SecureKeyStore
{
    private static readonly string AppFolder = Path.Combine(CommonService.GetUserPath(), "Secure");

    private static readonly string KeyPath = Path.Combine(AppFolder, "aes.key");
    private static readonly string IvPath = Path.Combine(AppFolder, "aes.iv");

    public static byte[] Key { get; private set; }
    public static byte[] IV { get; private set; }

    static SecureKeyStore()
    {
        Directory.CreateDirectory(AppFolder);

        if (File.Exists(KeyPath) && File.Exists(IvPath))
        {
            Key = Decrypt(File.ReadAllBytes(KeyPath));
            IV = Decrypt(File.ReadAllBytes(IvPath));
        }
        else
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();

            Key = aes.Key;
            IV = aes.IV;

            File.WriteAllBytes(KeyPath, Encrypt(Key));
            File.WriteAllBytes(IvPath, Encrypt(IV));
        }
    }

    private static byte[] Encrypt(byte[] data)
    {
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    private static byte[] Decrypt(byte[] data)
    {
        return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }
}