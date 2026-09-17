namespace AsphaltPlantManager.Core.Templates;

public interface ITemplateCatalog
{
    Task<TemplateDefinition> GetAsync(string id, int? version, CancellationToken cancellationToken);

    Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken cancellationToken);
}
