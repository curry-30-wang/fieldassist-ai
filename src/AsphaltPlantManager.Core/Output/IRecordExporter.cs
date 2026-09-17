namespace AsphaltPlantManager.Core.Output;

public interface IRecordExporter
{
    string Format { get; }

    Task<string> ExportAsync(
        OutputRecordSnapshot snapshot,
        string destinationRoot,
        CancellationToken cancellationToken);
}
