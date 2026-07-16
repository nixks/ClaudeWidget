namespace ClaudeWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ClaudeWidget_SingleInstance", out var createdNew);
        if (!createdNew) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
