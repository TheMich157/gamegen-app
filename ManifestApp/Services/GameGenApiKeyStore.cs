using System.Diagnostics.CodeAnalysis;

namespace ManifestApp.Services;

internal static class GameGenApiKeyStore
{
    private const string Resource = "gamegen-app/gamegen-api";

    /// <summary>Older builds stored keys here; TryRetrieve upgrades automatically.</summary>
    private const string ResourceLegacy = "manifestapp/gamegen-api";

    private const string User = "credential_v1";

    internal static bool TryRetrieve([NotNullWhen(true)] out string? apiKey)
    {
        apiKey = null;

        if (TryRetrieveForResource(Resource, out apiKey))
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return true;
            apiKey = null;
        }

        if (!TryRetrieveForResource(ResourceLegacy, out apiKey) || string.IsNullOrWhiteSpace(apiKey))
            return false;

        try
        {
            Replace(apiKey.Trim());
            apiKey = apiKey.Trim();
        }
        catch
        {
            // Key is usable even if migrating the locker entry fails.
        }

        return true;
    }

    private static bool TryRetrieveForResource(string resourceTag, [NotNullWhen(true)] out string? apiKey)
    {
        apiKey = null;
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            foreach (var c in vault.RetrieveAll())
            {
                if (!string.Equals(c.UserName, User, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(c.Resource, resourceTag, StringComparison.Ordinal))
                    continue;

                c.RetrievePassword();
                if (string.IsNullOrWhiteSpace(c.Password))
                    return false;

                apiKey = c.Password;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal static void Replace(string plainTextKey)
    {
        if (string.IsNullOrWhiteSpace(plainTextKey))
            throw new ArgumentException("API key cannot be empty.", nameof(plainTextKey));

        var vault = new Windows.Security.Credentials.PasswordVault();

        // PasswordVault.RetrieveAll() throws COMException (E_ELEMENT_NOT_FOUND, 0x80070490)
        // when the vault is empty — which is the first-launch case for every new user. Wrap
        // the cleanup pass so a fresh vault doesn't blow up the API-key save flow.
        try
        {
            foreach (var c in vault.RetrieveAll().ToList())
            {
                if (!string.Equals(c.UserName, User, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(c.Resource, Resource, StringComparison.Ordinal)
                    && !string.Equals(c.Resource, ResourceLegacy, StringComparison.Ordinal))
                    continue;

                try
                {
                    vault.Remove(c);
                }
                catch
                {
                    // best-effort erase
                }
            }
        }
        catch
        {
            // Vault is empty (or unreadable) — nothing to clean up before adding the new entry.
        }

        vault.Add(new Windows.Security.Credentials.PasswordCredential(Resource, User, plainTextKey));
    }
}
