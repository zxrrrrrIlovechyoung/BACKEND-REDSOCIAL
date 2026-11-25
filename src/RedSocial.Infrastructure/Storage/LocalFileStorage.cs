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

    public Task DeleteAsync(string? url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relativePath = GetRelativeUploadPath(url);
        if (relativePath is null)
            return Task.CompletedTask;

        // Se revisa también el wwwroot legado para limpiar archivos creados
        // antes de cambiar al almacenamiento compartido de App Service.
        var roots = new[]
        {
            ResolveRoot(),
            Path.Combine(_environment.ContentRootPath, "wwwroot")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var uploadsRoot = Path.GetFullPath(Path.Combine(root, "uploads"));
            var fullPath = Path.GetFullPath(Path.Combine(uploadsRoot, relativePath));
            var rootWithSeparator = uploadsRoot.EndsWith(Path.DirectorySeparatorChar)
                ? uploadsRoot
                : uploadsRoot + Path.DirectorySeparatorChar;

            // Solo se permite borrar dentro de uploads; nunca se usa directamente
            // una ruta recibida del cliente.
            if (fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        return Task.CompletedTask;
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

    private static string? GetRelativeUploadPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var value = url.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
            value = absoluteUri.AbsolutePath;

        var queryStart = value.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
            value = value[..queryStart];

        const string prefix = "/uploads/";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = Uri.UnescapeDataString(value[prefix.Length..])
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return string.IsNullOrWhiteSpace(relative) ? null : relative;
    }
}
