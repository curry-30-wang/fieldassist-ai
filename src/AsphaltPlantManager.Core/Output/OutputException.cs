namespace AsphaltPlantManager.Core.Output;

public sealed class OutputException : Exception
{
    public OutputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
