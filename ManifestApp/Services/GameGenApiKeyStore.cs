using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using ManifestApp.Core;

namespace ManifestApp.Services;

/// <summary>
/// Stores the GameGen API key on disk using Windows DPAPI, scoped to the current Windows user.
///
/// <para>This used to use <c>Windows.Security.Credentials.PasswordVault</c>, but PasswordVault
/// requires a package identity to be reliable. For unpackaged WinUI 3 apps the Vault service
/// frequently returns <c>0x80070425 / ERROR_SERVICE_CANNOT_ACCEPT_CTRL</c> ("Cannot open Vault"),
/// taking down the API-key save flow on a brand-new install.</para>
///
/// <para>DPAPI (<see cref="ProtectedData"/>) is a process- and service-free file API: the
/// ciphertext can only be decrypted by the same Windows user account that created it.</para>
/// </summary>
internal static class GameGenApiKeyStore
{
    /// <summary>Domain-separation entropy mixed into DPAPI so a stolen ciphertext from one
    /// app on this machine can't be decrypted by a different app on the same machine.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("gamegen-app/gamegen-api/credential_v1");

    private static string FilePath => Path.Combine(AppPaths.LocalRoot, "gamegen_api_key.bin");

    internal static bool TryRetrieve([NotNullWhen(true)] out string? apiKey)
    {
        apiKey = null;

        // ── Primary: DPAPI-encrypted file ──────────────────────────────────────
        try
        {
            if (File.Exists(FilePath))
            {
                var ciphertext = File.ReadAllBytes(FilePath);
                if (ciphertext.Length > 0)
                {
                    var plaintext = ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
                    var s = Encoding.UTF8.GetString(plaintext);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        apiKey = s.Trim();
                        return true;
                    }
                }
            }
        }
        catch
        {
            // File missing / corrupt / DPAPI rejected — fall through to legacy.
        }

        // ── Legacy: PasswordVault entry from v2.0.x ────────────────────────────
        // Migrate forward if anything is still hiding in the Credential Locker.
        if (TryRetrieveFromPasswordVault(out var legacy) && !string.IsNullOrWhiteSpace(legacy))
        {
            try { Replace(legacy.Trim()); } catch { /* migration is best-effort */ }
            apiKey = legacy.Trim();
            return true;
        }

        return false;
    }

    internal static void Replace(string plainTextKey)
    {
        if (string.IsNullOrWhiteSpace(plainTextKey))
            throw new ArgumentException("API key cannot be empty.", nameof(plainTextKey));

        AppPaths.EnsureLayout();

        var plaintext  = Encoding.UTF8.GetBytes(plainTextKey.Trim());
        var ciphertext = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

        // Atomic-ish write: write to a temp file then rename so a crash mid-write
        // can't leave a half-encrypted file that would later fail Unprotect.
        var tmp = FilePath + ".tmp";
        File.WriteAllBytes(tmp, ciphertext);
        File.Move(tmp, FilePath, overwrite: true);

        // Best-effort cleanup of any legacy Vault entry now that DPAPI owns the truth.
        TryDeleteLegacyVaultEntry();
    }

    // ── Legacy migration helpers ──────────────────────────────────────────────

    private const string LegacyVaultResource       = "gamegen-app/gamegen-api";
    private const string LegacyVaultResourceOlder  = "manifestapp/gamegen-api";
    private const string LegacyVaultUser           = "credential_v1";

    private static bool TryRetrieveFromPasswordVault([NotNullWhen(true)] out string? apiKey)
    {
        apiKey = null;
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            foreach (var c in vault.RetrieveAll())
            {
                if (!string.Equals(c.UserName, LegacyVaultUser, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(c.Resource, LegacyVaultResource, StringComparison.Ordinal)
                    && !string.Equals(c.Resource, LegacyVaultResourceOlder, StringComparison.Ordinal))
                    continue;

                c.RetrievePassword();
                if (!string.IsNullOrWhiteSpace(c.Password))
                {
                    apiKey = c.Password;
                    return true;
                }
            }
        }
        catch
        {
            // Vault unavailable / empty — no legacy data to migrate.
        }
        return false;
    }

    private static void TryDeleteLegacyVaultEntry()
    {
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            foreach (var c in vault.RetrieveAll().ToList())
            {
                if (!string.Equals(c.UserName, LegacyVaultUser, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(c.Resource, LegacyVaultResource, StringComparison.Ordinal)
                    && !string.Equals(c.Resource, LegacyVaultResourceOlder, StringComparison.Ordinal))
                    continue;

                try { vault.Remove(c); } catch { /* ignore */ }
            }
        }
        catch
        {
            // Vault unreachable — there's nothing to clean up, that's fine.
        }
    }
}
