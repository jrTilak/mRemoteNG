using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace mRemoteNG.Packaging;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("mRemoteNG.PackagedPayload.zip")
                ?? throw new InvalidOperationException("The packaged application is missing from this launcher.");
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
                throw new InvalidOperationException("Windows did not provide a local application-data folder.");

            string appDirectory = PayloadExtractor.GetOrExtract(payload, Path.Combine(localData, "mRemoteNG", "Packaged"));
            var startInfo = new ProcessStartInfo(Path.Combine(appDirectory, "Capture2Text.exe"))
            {
                WorkingDirectory = appDirectory,
                UseShellExecute = false
            };
            foreach (string argument in args)
                startInfo.ArgumentList.Add(argument);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start the packaged application.");
            return 0;
        }
        catch (Exception exception)
        {
            MessageBoxW(IntPtr.Zero, "The packaged application could not be opened.\n\n" + exception.Message,
                string.Empty, 0x00000010);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
