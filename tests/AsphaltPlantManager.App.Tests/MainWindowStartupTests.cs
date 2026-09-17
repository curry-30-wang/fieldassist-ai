using System.Windows;
using Xunit;

namespace AsphaltPlantManager.App.Tests;

public sealed class MainWindowStartupTests
{
    [Fact]
    public void Main_window_can_be_created_on_a_sta_thread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new AsphaltPlantManager.App.MainWindow(null!);
                Assert.NotNull(window);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF window creation did not finish.");

        Assert.Null(failure);
    }
}
