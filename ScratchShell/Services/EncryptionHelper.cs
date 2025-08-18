using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ScratchShell.Services;

public static class EncryptionHelper
{
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var keys = SecureKeyStore.GetCloudEncryptionKeys();
        if (keys == null)
            return plainText;

        using var aes = Aes.Create();
        aes.Key = keys.Value.Key;
        aes.IV = keys.Value.IV; // ✅ new IV for each message

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var writer = new StreamWriter(cs))
        {
            writer.Write(plainText);
        }

        var cipherBytes = ms.ToArray();

        // ✅ prepend IV
        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);

        var result = Convert.ToBase64String(combined);

        // 🔹 Debug log
        System.Diagnostics.Debug.WriteLine("=== ENCRYPT ===");
        System.Diagnostics.Debug.WriteLine($"Key:       {Convert.ToBase64String(aes.Key)}");
        System.Diagnostics.Debug.WriteLine($"IV:        {Convert.ToBase64String(aes.IV)}");
        System.Diagnostics.Debug.WriteLine($"CipherText:{result}");

        return result;
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        var keys = SecureKeyStore.GetCloudEncryptionKeys();
        if (keys == null)
            return cipherText;

        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = keys.Value.Key;

        // ✅ extract IV
        var iv = new byte[16];
        var cipher = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);
        aes.IV = iv;

        // 🔹 Debug log
        System.Diagnostics.Debug.WriteLine("=== DECRYPT ===");
        System.Diagnostics.Debug.WriteLine($"Key:       {Convert.ToBase64String(aes.Key)}");
        System.Diagnostics.Debug.WriteLine($"IV:        {Convert.ToBase64String(aes.IV)}");
        System.Diagnostics.Debug.WriteLine($"Cipher:    {Convert.ToBase64String(cipher)}");

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cs);
        return reader.ReadToEnd();
    }

    public static bool IsEncryptionAvailable => SecureKeyStore.HasUserKeys;
}
