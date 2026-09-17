namespace AsphaltPlantManager.Core.Backup;

public sealed class BackupValidationException : Exception
{
    public BackupValidationException(string message) : base(message)
    {
    }

    public BackupValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
