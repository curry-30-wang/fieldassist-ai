namespace AsphaltPlantManager.Core.Records;

public sealed class ArchiveSessionService
{
    private readonly IUnlockPasswordStore _passwordStore;
    private readonly SemaphoreSlim _unlockGate = new(1, 1);
    private int _unlocked;

    public ArchiveSessionService(IUnlockPasswordStore passwordStore) => _passwordStore = passwordStore ?? throw new ArgumentNullException(nameof(passwordStore));

    public bool IsUnlocked => Volatile.Read(ref _unlocked) == 1;

    public async Task<bool> UnlockAsync(string password, CancellationToken cancellationToken)
    {
        if (IsUnlocked)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        await _unlockGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUnlocked)
            {
                return true;
            }

            var valid = await _passwordStore.VerifyAsync(password, CancellationToken.None).ConfigureAwait(false);
            if (valid)
            {
                Interlocked.Exchange(ref _unlocked, 1);
            }
            return valid;
        }
        finally
        {
            _unlockGate.Release();
        }
    }
}
