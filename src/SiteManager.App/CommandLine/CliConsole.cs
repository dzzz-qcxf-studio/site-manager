using System.Runtime.InteropServices;
using System.IO;

namespace SiteManager.App.CommandLine;

internal static class CliConsole
{
    private const uint AttachParentConsole = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    public static void AttachToParent()
    {
        if (!OperatingSystem.IsWindows() || GetConsoleWindow() != IntPtr.Zero)
        {
            return;
        }

        if (!AttachConsole(AttachParentConsole))
        {
            return;
        }

        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (IOException)
        {
            // A caller without standard handles can still use the CLI through
            // injected writers in tests; leave the process alive and let the
            // runner report through the available stream.
        }
    }

    public static void DetachForGui()
    {
        if (OperatingSystem.IsWindows() && GetConsoleWindow() != IntPtr.Zero)
        {
            FreeConsole();
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
