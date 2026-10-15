using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace LinuxControlPanel;

public partial class App : Application
{
    /// <summary>처리하지 못한 예외를 남기는 곳. 사용자에게도 이 경로를 알려 준다.</summary>
    public static string LogPath { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "LinuxControlPanel-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 어디서든 빠뜨린 예외가 창을 조용히 죽이지 않도록 마지막 그물을 쳐 둔다.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);

        // TargetInvocationException 처럼 감싸인 예외는 속을 꺼내야 원인이 보인다.
        var root = Unwrap(e.Exception);

        MessageBox.Show(
            $"예기치 못한 오류가 발생했습니다.{Environment.NewLine}{Environment.NewLine}" +
            $"{root.GetType().Name}: {root.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"자세한 내용: {LogPath}",
            "Linux Control Panel",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static Exception Unwrap(Exception exception) => exception switch
    {
        TargetInvocationException { InnerException: { } inner } => Unwrap(inner),
        AggregateException { InnerException: { } inner } => Unwrap(inner),
        _ => exception,
    };

    private static void Log(Exception exception)
    {
        try
        {
            System.IO.File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (System.IO.IOException)
        {
            // 로그를 못 남기는 상황까지 붙들고 있을 이유는 없다.
        }
    }
}
