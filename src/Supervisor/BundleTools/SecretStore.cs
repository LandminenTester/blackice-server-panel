using System.Security.Cryptography;
using System.Text;
namespace ReplayPanel.Supervisor.BundleTools;

/// <summary>Encrypts provider tokens at rest (AES-GCM, key file in the private state folder). Plain values never leave the supervisor.</summary>
public sealed class SecretStore(string stateDir)
{
    private readonly string keyFile = Path.Combine(stateDir, "keys", "bundle-secrets.key");
    private byte[]? key;
    private readonly object sync = new();

    private byte[] Key()
    {
        lock (sync)
        {
            if (key is not null) return key;
            Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
            if (File.Exists(keyFile))
            {
                key = File.ReadAllBytes(keyFile);
                if (key.Length != 32) throw new InvalidOperationException("bundleTools.secrets");
                return key;
            }
            var created = RandomNumberGenerator.GetBytes(32);
            var temp = keyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, created);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, keyFile, true);
            key = created;
            return key;
        }
    }

    public string Protect(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key(), 16);
        aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedValue)
    {
        var raw = Convert.FromBase64String(protectedValue);
        if (raw.Length < 28) throw new InvalidOperationException("bundleTools.secrets");
        var data = new byte[raw.Length - 28];
        using var aes = new AesGcm(Key(), 16);
        try { aes.Decrypt(raw.AsSpan(0, 12), raw.AsSpan(28), raw.AsSpan(12, 16), data); }
        catch (CryptographicException) { throw new InvalidOperationException("bundleTools.secrets"); }
        return Encoding.UTF8.GetString(data);
    }
}
