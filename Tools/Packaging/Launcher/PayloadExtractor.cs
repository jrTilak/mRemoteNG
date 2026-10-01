using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace mRemoteNG.Packaging;

internal static class PayloadExtractor
{
    private const string ExecutableName = "mRemoteNG.exe";
    private const string CompletionMarker = ".payload.sha256";

    internal static string GetOrExtract(Stream payloadZip, string cacheRoot)
    {
        if (!payloadZip.CanRead || !payloadZip.CanSeek)
            throw new ArgumentException("The packaged ZIP must be a readable, seekable stream.", nameof(payloadZip));

        payloadZip.Position = 0;
        string payloadHash = Convert.ToHexString(SHA256.HashData(payloadZip));
        payloadZip.Position = 0;
        cacheRoot = Path.GetFullPath(cacheRoot);
        EnsureNoReparsePoints(cacheRoot);
        Directory.CreateDirectory(cacheRoot);
        EnsureNoReparsePoints(cacheRoot);

        string pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheRoot.ToUpperInvariant())))[..16];
        string mutexName = (OperatingSystem.IsWindows() ? "Local\\" : "") + "mRemoteNG.Packaged." + pathHash + "." + payloadHash;
        using var extractionMutex = new Mutex(false, mutexName);
        bool acquired = false;
        try
        {
            try
            {
                acquired = extractionMutex.WaitOne(TimeSpan.FromMinutes(2));
            }
            catch (AbandonedMutexException)
            {
                // A previous launcher exited during extraction. Its staging tree is never used.
                acquired = true;
            }
            if (!acquired)
                throw new TimeoutException("Another launcher is still preparing this application. Try again after it finishes.");

            string packageDirectory = Path.Combine(cacheRoot, payloadHash);
            if (Directory.Exists(packageDirectory) || File.Exists(packageDirectory))
                return ValidateExistingPackage(packageDirectory, payloadHash);

            string stagingDirectory = Path.Combine(cacheRoot, payloadHash + ".staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                string stagingApp = Path.Combine(stagingDirectory, "app");
                Directory.CreateDirectory(stagingApp);
                EnsureNoReparsePoints(stagingApp);
                Extract(payloadZip, stagingApp);
                if (!File.Exists(Path.Combine(stagingApp, ExecutableName)))
                    throw new InvalidDataException("The ZIP must contain mRemoteNG.exe at its root.");

                File.WriteAllText(Path.Combine(stagingDirectory, CompletionMarker), payloadHash, Encoding.ASCII);
                try
                {
                    Directory.Move(stagingDirectory, packageDirectory);
                }
                catch (IOException) when (Directory.Exists(packageDirectory))
                {
                    // Local mutexes do not cross Windows sessions. A second session may have completed first.
                    return ValidateExistingPackage(packageDirectory, payloadHash);
                }
                return ValidateExistingPackage(packageDirectory, payloadHash);
            }
            finally
            {
                // Only this invocation's unpublished tree is removed. Existing settings are never overwritten.
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
        }
        finally
        {
            if (acquired)
                extractionMutex.ReleaseMutex();
        }
    }

    private static string ValidateExistingPackage(string packageDirectory, string expectedHash)
    {
        string appDirectory = Path.Combine(packageDirectory, "app");
        string marker = Path.Combine(packageDirectory, CompletionMarker);
        string executable = Path.Combine(appDirectory, ExecutableName);
        EnsureNoReparsePoints(marker);
        EnsureNoReparsePoints(executable);
        if (!Directory.Exists(appDirectory) || !File.Exists(marker) ||
            !File.Exists(executable) || !string.Equals(File.ReadAllText(marker), expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The extracted application is incomplete at '" + packageDirectory +
                "'. Back up any settings there, move that folder aside, and run the launcher again.");
        }
        return appDirectory;
    }

    private static void Extract(Stream payloadZip, string destination)
    {
        using var archive = new ZipArchive(payloadZip, ZipArchiveMode.Read, leaveOpen: true);
        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string directoryPrefix = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                (unixType != 0 && unixType != 0x8000 && unixType != 0x4000))
                throw new InvalidDataException("The ZIP contains an unsupported link or special file: " + entry.FullName);

            string normalized = entry.FullName.Replace('\\', '/');
            bool isDirectory = normalized.EndsWith('/') || unixType == 0x4000;
            string relative = isDirectory ? normalized.TrimEnd('/') : normalized;
            string[] segments = relative.Split('/');
            if (segments.Any(IsInvalidWindowsSegment) || !entryNames.Add(relative))
                throw new InvalidDataException("The ZIP contains an invalid or duplicate path: " + entry.FullName);

            string outputPath = Path.GetFullPath(Path.Combine(destination, Path.Combine(segments)));
            if (!outputPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ZIP contains a path outside its destination.");

            EnsureNoReparsePoints(outputPath);
            if (isDirectory)
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            EnsureNoReparsePoints(outputPath);
            using Stream input = entry.Open();
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static bool IsInvalidWindowsSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment) || segment is "." or ".." ||
            segment.EndsWith('.') || segment.EndsWith(' ') ||
            segment.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)))
            return true;

        string stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                "123456789¹²³".Contains(stem[3]));
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The application cache must not contain symbolic links or junctions: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
