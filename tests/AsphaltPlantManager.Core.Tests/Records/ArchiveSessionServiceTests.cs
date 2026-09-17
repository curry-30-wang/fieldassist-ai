using AsphaltPlantManager.Core.Records;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Records;

public sealed class ArchiveSessionServiceTests
{
    [Fact]
    public async Task Correct_password_unlocks_the_current_session_and_is_verified_once()
    {
        var passwords = new RecordingPasswordStore("correct password");
        var session = new ArchiveSessionService(passwords);

        (await session.UnlockAsync("correct password", CancellationToken.None)).Should().BeTrue();
        (await session.UnlockAsync("anything afterwards", CancellationToken.None)).Should().BeTrue();

        session.IsUnlocked.Should().BeTrue();
        passwords.VerifyCount.Should().Be(1);
    }

    [Fact]
    public async Task Incorrect_password_keeps_session_locked_without_disclosing_password_details()
    {
        var session = new ArchiveSessionService(new RecordingPasswordStore("correct password"));

        (await session.UnlockAsync("wrong password", CancellationToken.None)).Should().BeFalse();

        session.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_session_starts_locked_even_after_another_session_was_unlocked()
    {
        var passwords = new RecordingPasswordStore("correct password");
        var first = new ArchiveSessionService(passwords);
        await first.UnlockAsync("correct password", CancellationToken.None);
        var second = new ArchiveSessionService(passwords);

        second.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_unlocks_share_one_verification_and_a_late_wrong_password_cannot_relock_the_session()
    {
        var passwords = new BlockingPasswordStore();
        var session = new ArchiveSessionService(passwords);

        var first = session.UnlockAsync("correct", CancellationToken.None);
        await passwords.VerifyStarted.Task;
        var second = session.UnlockAsync("wrong", CancellationToken.None);
        passwords.CompleteVerification(true);

        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        passwords.VerifyCount.Should().Be(1);
        session.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_one_unlock_waiter_does_not_cancel_the_shared_verification_or_successful_session()
    {
        var passwords = new BlockingPasswordStore();
        var session = new ArchiveSessionService(passwords);
        var first = session.UnlockAsync("correct", CancellationToken.None);
        await passwords.VerifyStarted.Task;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelledWaiter = () => session.UnlockAsync("wrong", cancellation.Token);
        await cancelledWaiter.Should().ThrowAsync<OperationCanceledException>();
        passwords.CompleteVerification(true);

        (await first).Should().BeTrue();
        passwords.VerifyCount.Should().Be(1);
        session.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Correct_password_waiting_behind_an_in_flight_wrong_password_is_verified_separately_and_unlocks()
    {
        var passwords = new SequencedPasswordStore();
        var session = new ArchiveSessionService(passwords);

        var wrong = session.UnlockAsync("wrong", CancellationToken.None);
        await passwords.FirstVerificationStarted.Task;
        var correct = session.UnlockAsync("correct", CancellationToken.None);
        passwords.CompleteFirst(false);
        await passwords.SecondVerificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        passwords.CompleteSecond(true);

        (await wrong).Should().BeFalse();
        (await correct).Should().BeTrue();
        passwords.VerifyCount.Should().Be(2);
        session.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Wrong_password_waiting_behind_an_in_flight_correct_password_observes_the_unlocked_session_without_reverification()
    {
        var passwords = new SequencedPasswordStore();
        var session = new ArchiveSessionService(passwords);

        var correct = session.UnlockAsync("correct", CancellationToken.None);
        await passwords.FirstVerificationStarted.Task;
        var wrong = session.UnlockAsync("wrong", CancellationToken.None);
        passwords.CompleteFirst(true);

        (await correct).Should().BeTrue();
        (await wrong).Should().BeTrue();
        passwords.VerifyCount.Should().Be(1);
        session.IsUnlocked.Should().BeTrue();
    }

    private sealed class RecordingPasswordStore(string password) : IUnlockPasswordStore
    {
        public int VerifyCount { get; private set; }

        public Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetPasswordAsync(string newPassword, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> VerifyAsync(string candidatePassword, CancellationToken cancellationToken)
        {
            VerifyCount++;
            return Task.FromResult(candidatePassword == password);
        }
    }

    private sealed class BlockingPasswordStore : IUnlockPasswordStore
    {
        private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> VerifyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int VerifyCount { get; private set; }
        public Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetPasswordAsync(string newPassword, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> VerifyAsync(string candidatePassword, CancellationToken cancellationToken)
        {
            VerifyCount++;
            VerifyStarted.TrySetResult(true);
            return _result.Task.WaitAsync(cancellationToken);
        }
        public void CompleteVerification(bool result) => _result.TrySetResult(result);
    }

    private sealed class SequencedPasswordStore : IUnlockPasswordStore
    {
        private readonly TaskCompletionSource<bool> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstVerificationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SecondVerificationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int VerifyCount { get; private set; }
        public Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetPasswordAsync(string newPassword, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> VerifyAsync(string candidatePassword, CancellationToken cancellationToken)
        {
            VerifyCount++;
            return VerifyCount switch
            {
                1 => StartFirst(cancellationToken),
                2 => StartSecond(cancellationToken),
                _ => throw new InvalidOperationException("Unexpected verification.")
            };
        }
        public void CompleteFirst(bool result) => _first.TrySetResult(result);
        public void CompleteSecond(bool result) => _second.TrySetResult(result);
        private Task<bool> StartFirst(CancellationToken cancellationToken)
        {
            FirstVerificationStarted.TrySetResult(true);
            return _first.Task.WaitAsync(cancellationToken);
        }
        private Task<bool> StartSecond(CancellationToken cancellationToken)
        {
            SecondVerificationStarted.TrySetResult(true);
            return _second.Task.WaitAsync(cancellationToken);
        }
    }
}
