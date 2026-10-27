namespace TcpDebugger;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("UnhandledException", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => LogCrash("ThreadException", e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            // 使用 PerMonitorV2 DPI 感知：布局按逻辑像素计算，由系统统一缩放，
            // 避免在 125%/150% 缩放下控件尺寸与设计值不符导致溢出。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 自测模式：TcpDebugger.exe --selftest（验证核心收发逻辑，结果写入 selftest.txt）
            if (args.Contains("--selftest"))
            {
                Environment.Exit(SelfTest.Run());
                return;
            }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            LogCrash("Startup", ex);
            throw;
        }
    }

    private static void LogCrash(string stage, Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {stage}{(ex == null ? "" : "\r\n" + ex)}\r\n\r\n");
        }
        catch { }
    }
}
