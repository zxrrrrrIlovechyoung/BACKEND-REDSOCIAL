namespace RedSocial.Infrastructure.Storage;

public sealed class StorageSettings
{
    public const string Seccion = "Storage";

    public string Provider { get; set; } = "Local";
    public string LocalRoot { get; set; } = string.Empty;
}
