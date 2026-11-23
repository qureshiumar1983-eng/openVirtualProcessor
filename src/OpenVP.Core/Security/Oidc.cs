namespace OpenVP.Core.Security;

public sealed record OidcMapResult(bool Ok, string? Name, string? Display, Role Role, string? Error);

/// <summary>Turns the claims a single-sign-on provider sent into an OpenVP user name and role. Pure, so it is tested without a provider.</summary>
public static class OidcMapper
{
    public static OidcMapResult Map(IEnumerable<KeyValuePair<string, string>> claims, OidcSettings s)
    {
        var list = claims.ToList();
        IEnumerable<string> Values(string type) => list.Where(c => string.Equals(c.Key, type, StringComparison.OrdinalIgnoreCase)).SelectMany(c => Split(c.Value));
        string? First(params string[] types) { foreach (var t in types) { var v = Values(t).FirstOrDefault(x => x.Length > 0); if (v != null) return v; } return null; }

        var email = First("email", "upn", "preferred_username");
        if (s.AllowedDomains.Count > 0)
        {
            var domain = email != null && email.Contains('@') ? email[(email.LastIndexOf('@') + 1)..] : null;
            if (domain == null || !s.AllowedDomains.Any(d => string.Equals(d.Trim().TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase)))
                return new(false, null, null, Role.Viewer, "Your e-mail address isn't at a domain that may sign in here.");
        }

        var name = First(s.UserClaim, "preferred_username", "email", "name", "sub");
        if (string.IsNullOrWhiteSpace(name)) return new(false, null, null, Role.Viewer, "The provider sent no name for you.");
        if (UserStore.NameProblem(name) != null) name = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '@' or '-' or ' ').ToArray()).Trim();
        if (UserStore.NameProblem(name) != null) return new(false, null, null, Role.Viewer, "The name the provider sent can't be used as a user name.");

        var have = Values(s.RoleClaim).ToList();
        Role? best = null;
        foreach (var m in s.Mappings)
            if (!string.IsNullOrWhiteSpace(m.Value) && have.Any(h => string.Equals(h, m.Value.Trim(), StringComparison.OrdinalIgnoreCase)) && (best == null || m.Role > best)) best = m.Role;
        best ??= s.DefaultRole;
        if (best == null) return new(false, name, null, Role.Viewer, "Your account isn't in a group that may use OpenVP.");
        return new(true, name, First("name"), best.Value, null);
    }

    // A claim that is a JSON array arrives as the text of the array when it wasn't expanded: split that too.
    private static IEnumerable<string> Split(string v)
    {
        v = v.Trim();
        if (v.StartsWith('[') && v.EndsWith(']'))
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(v) ?? [v]; } catch (System.Text.Json.JsonException) { }
        }
        return [v];
    }
}
