using AsphaltPlantManager.Core.Templates;

namespace AsphaltPlantManager.Core.Records;

public sealed class ArchiveNumberService
{
    public string GetPrefix(TemplateDefinition template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(template.ArchivePrefix))
        {
            throw new InvalidOperationException("归档模板未配置有效的档案前缀。");
        }

        return template.ArchivePrefix.Trim().ToUpperInvariant();
    }
}
