using System.Runtime.InteropServices;

namespace Endo;

/// <summary>
/// Single entry point for the unified Endo executable. Arguments present => console/CLI mode
/// (attach to the caller's console, or allocate one if launched detached); no arguments => GUI
/// mode. Both modes drive the exact same CommandEngine underneath (Endo.Cli.CliHost /
/// Endo.Gui.App) — the GUI is a presentation layer over the same command dispatch, not a second
/// implementation.
/// </summary>
internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [STAThread]
    private static int Main(string[] args) => args.Length > 0 ? RunCli(args) : RunGui();

    private static int RunCli(string[] args)
    {
        var attachedExistingConsole = AttachConsole(AttachParentProcess);
        if (!attachedExistingConsole)
        {
            AllocConsole();
        }

        try
        {
            return Cli.CliHost.Run(args);
        }
        finally
        {
            Console.Out.Flush();
            Console.Error.Flush();
            if (!attachedExistingConsole)
            {
                FreeConsole();
            }
        }
    }

    private static int RunGui()
    {
        var app = new Gui.App();
        app.InitializeComponent();
        return app.Run();
    }
}
