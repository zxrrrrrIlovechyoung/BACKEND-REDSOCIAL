using Microsoft.AspNetCore.Http;

namespace RedSocial.API.Services;

public sealed class AuthCookieSettings
{
    public const string Section = "AuthCookie";

    public bool Enabled { get; set; }
    public string Name { get; set; } = "moment_auth";
    public string Path { get; set; } = "/";
    public string? Domain { get; set; }
    public bool Secure { get; set; } = true;
    public string SameSite { get; set; } = "Lax";
    public int ExpireMinutes { get; set; } = 60;

    public SameSiteMode GetSameSiteMode() =>
        Enum.TryParse<SameSiteMode>(SameSite, ignoreCase: true, out var mode)
            ? mode
            : SameSiteMode.Lax;
}
