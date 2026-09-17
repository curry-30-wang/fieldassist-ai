using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AsphaltPlantManager.App.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public async Task Initialization_does_not_capture_a_blocked_calling_synchronization_context()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "AsphaltPlantManager.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
        try
        {
            var templates = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "templates"));
            await using var root = new CompositionRoot(Path.Combine(dataDirectory, "plant.db"), templates);
            var initialization = root.InitializeAsync(CancellationToken.None);

#pragma warning disable xUnit1030
            var completed = await Task.WhenAny(initialization, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

            Assert.Same(initialization, completed);
            await initialization.ConfigureAwait(false);
#pragma warning restore xUnit1030
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Initializes_a_real_database_and_resolves_the_main_workspace()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "AsphaltPlantManager.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var templates = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "templates"));
            await using (var root = new CompositionRoot(Path.Combine(dataDirectory, "plant.db"), templates))
            {
                await root.InitializeAsync(CancellationToken.None);

                var workspace = root.Services.GetRequiredService<MainWindowViewModel>();
                Assert.Equal("首页", workspace.CurrentPage);
                Assert.Equal(7, workspace.NavigationItems.Count);
                Assert.True(workspace.NavigationItems.Single(item => item.Title == "首页").IsActive);
                Assert.Equal("表格与档案", workspace.NavigationItems.Single(item => item.Title == "新建表格").Group);
                Assert.Equal("销售结算", workspace.NavigationItems.Single(item => item.Title == "客户欠款").Group);
                Assert.False(workspace.NavigationItems.Single(item => item.Title == "档案").ShowGroupHeader);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}

file sealed class NonPumpingSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        // Deliberately discard callbacks: this models a UI thread blocked by GetResult().
    }
}
