using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RedSocial.Application.Interfaces.Services;

namespace RedSocial.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly IHostEnvironment _environment;
    private readonly StorageSettings _settings;

    public LocalFileStorage(IHostEnvironment environment, IOptions<StorageSettings> settings)
    {
        _environment = environment;
        _settings = settings.Value;
    }

    public async Task<string> SaveAsync(
        Stream content,
        string category,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveRoot();
        var safeCategory = SanitizeSegment(category);
        var safeExtension = NormalizeExtension(extension);
        var fileName = $"{Guid.NewGuid():N}{safeExtension}";
        var directory = Path.Combine(root, "uploads", safeCategory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await content.CopyToAsync(output, cancellationToken);

        return $"/uploads/{safeCategory}/{fileName}";
    }

    public string ResolveRoot()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LocalRoot))
            return _settings.LocalRoot;

        if (_environment.IsDevelopment())
            return Path.Combine(_environment.ContentRootPath, "wwwroot");

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            return Path.Combine(home, "data", "redsocial-media");

        return Path.Combine(_environment.ContentRootPath, "data", "redsocial-media");
    }

    private static string SanitizeSegment(string value)
    {
        var sanitized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(sanitized) ? "uploads" : sanitized;
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim().ToLowerInvariant();
        return normalized.StartsWith('.') ? normalized : $".{normalized}";
    }
}
