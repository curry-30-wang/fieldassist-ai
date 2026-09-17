namespace AsphaltPlantManager.Core.Records;

public interface IUnlockPasswordStore
{
    Task<bool> HasPasswordAsync(CancellationToken cancellationToken);
    Task SetPasswordAsync(string password, CancellationToken cancellationToken);
    Task<bool> VerifyAsync(string password, CancellationToken cancellationToken);
}
