using System.Windows;

namespace XDiskInspector.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = Environment.GetCommandLineArgs();
        if (ElevatedCleanupWorker.IsElevatedCleanupInvocation(args))
        {
            var exitCode = ElevatedCleanupWorker.Run(args);
            Shutdown(exitCode);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
