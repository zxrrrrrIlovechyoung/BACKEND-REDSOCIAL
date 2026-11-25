namespace RedSocial.Application.Interfaces.Services;

public interface IFileStorage
{
    Task<string> SaveAsync(
        Stream content,
        string category,
        string extension,
        CancellationToken cancellationToken = default);
}
