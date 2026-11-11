namespace NJTruckCharging.Security;

/// <summary>
/// The one rule for "is this an employee e-mail address": exactly one '@', a non-empty local part, printable ASCII only,
/// and a domain that is EXACTLY one of the company domains (case-insensitive). Subdomains (x.poweredison.com),
/// lookalikes (poweredison.co, poweredison.com.evil.io, Unicode homoglyphs) and trailing dots are all refused.
/// <para>The same rule exists in the Supabase Before User Created hook (CloudCopy/sql/07-before-user-created-hook.sql)
/// and in the Angular forms (core/employee-email.ts, user experience only). All three are tested against
/// NJTruckCharging.Web/src/app/core/employee-email.vectors.json.</para>
/// <para>The domains are deliberately fixed in code, not configuration: adding a domain is a reviewed code change,
/// and a mistyped environment variable can never widen access.</para>
/// </summary>
public static class EmployeeEmailPolicy
{
    public static readonly IReadOnlyList<string> CompanyDomains = ["poweredison.com", "ev-edison.com"];

    /// <summary>The lower-cased domain of a well-formed address, or null when the address is malformed.</summary>
    public static string? DomainOf(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > 254) return null;
        foreach (var ch in email)
            if (ch <= ' ' || ch > '~') return null;                 // no spaces, control or non-ASCII characters
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1) return null;
        return email[(at + 1)..].ToLowerInvariant();
    }

    public static bool IsAllowed(string? email) =>
        DomainOf(email) is { } domain && CompanyDomains.Contains(domain, StringComparer.Ordinal);
}
