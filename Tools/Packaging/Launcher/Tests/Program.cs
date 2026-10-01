using System.IO.Compression;
using System.Text;
using mRemoteNG.Packaging;

var tests = new List<(string Name, Action<string> Run)>
{
    ("Extracts complete application layout", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null), ("Assemblies/dependency.dll", "library", null),
            ("Themes/", "", null), ("Themes/default.theme", "theme", null));
        string app = PayloadExtractor.GetOrExtract(zip, root);
        Equal("executable", File.ReadAllText(Path.Combine(app, "mRemoteNG.exe")));
        Equal("library", File.ReadAllText(Path.Combine(app, "Assemblies", "dependency.dll")));
        Equal("theme", File.ReadAllText(Path.Combine(app, "Themes", "default.theme")));
        True(File.Exists(Path.Combine(Path.GetDirectoryName(app)!, ".payload.sha256")));
    }),
    ("Cached package preserves portable settings", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null));
        string app = PayloadExtractor.GetOrExtract(zip, root);
        File.WriteAllText(Path.Combine(app, "confCons.xml"), "user connections");
        Equal(app, PayloadExtractor.GetOrExtract(zip, root));
        Equal("user connections", File.ReadAllText(Path.Combine(app, "confCons.xml")));
    }),
    ("Changed payload gets a separate version folder", root =>
    {
        using var first = Zip(("mRemoteNG.exe", "first build", null));
        using var second = Zip(("mRemoteNG.exe", "second build", null));
        string firstApp = PayloadExtractor.GetOrExtract(first, root);
        string secondApp = PayloadExtractor.GetOrExtract(second, root);
        True(firstApp != secondApp);
        Equal("first build", File.ReadAllText(Path.Combine(firstApp, "mRemoteNG.exe")));
    }),
    ("Missing marker does not overwrite existing settings", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null));
        string app = PayloadExtractor.GetOrExtract(zip, root);
        File.WriteAllText(Path.Combine(app, "settings.xml"), "preserve me");
        File.Delete(Path.Combine(Path.GetDirectoryName(app)!, ".payload.sha256"));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
        Equal("preserve me", File.ReadAllText(Path.Combine(app, "settings.xml")));
    }),
    ("Wrong marker is rejected", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null));
        string app = PayloadExtractor.GetOrExtract(zip, root);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(app)!, ".payload.sha256"), "wrong payload");
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
    }),
    ("Missing cached executable is rejected", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null));
        string app = PayloadExtractor.GetOrExtract(zip, root);
        File.Delete(Path.Combine(app, "mRemoteNG.exe"));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
    }),
    ("Missing root executable fails and cleans staging", root =>
    {
        using var zip = Zip(("nested/mRemoteNG.exe", "wrong root", null));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
        Equal(0, Directory.EnumerateFileSystemEntries(root).Count());
    }),
    ("Corrupt archive fails and cleans staging", root =>
    {
        using var zip = new MemoryStream(Encoding.UTF8.GetBytes("not a ZIP file"));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
        Equal(0, Directory.EnumerateFileSystemEntries(root).Count());
    }),
    ("Windows case-insensitive duplicate files are rejected", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "one", null), ("MREMOTENG.EXE", "two", null));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
        Equal(0, Directory.EnumerateFileSystemEntries(root).Count());
    }),
    ("ZIP symbolic links are rejected", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null), ("link", "../outside", unchecked((int)0xa1ff0000)));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
        Equal(0, Directory.EnumerateFileSystemEntries(root).Count());
    }),
    ("ZIP reparse metadata is rejected", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null), ("link", "outside", (int)FileAttributes.ReparsePoint));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
    }),
    ("Concurrent extraction publishes only one complete tree", root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null), ("content.bin", new string('x', 100_000), null));
        byte[] bytes = zip.ToArray();
        string[] paths = new string[4];
        Parallel.For(0, paths.Length, index =>
        {
            using var input = new MemoryStream(bytes);
            paths[index] = PayloadExtractor.GetOrExtract(input, root);
        });
        True(paths.All(path => path == paths[0]));
        Equal(1, Directory.EnumerateDirectories(root).Count());
        Equal(100_000L, new FileInfo(Path.Combine(paths[0], "content.bin")).Length);
    })
};

foreach (string invalidPath in new[]
{
    "../escaped.txt", "folder/../../escaped.txt", "..\\escaped.txt", "folder\\..\\escaped.txt",
    "/absolute.txt", "\\\\server\\share\\file.txt", "C:/absolute.txt", "C:relative.txt",
    "file.txt:stream", "folder/./file.txt", "folder//file.txt", "trailing. ", "trailing.",
    "CON", "NUL.txt", "COM1.txt", "LPT¹.log", "CONIN$", "CONOUT$.txt", "bad\u0001name", "wild*.txt"
})
{
    string path = invalidPath;
    tests.Add(("Rejects unsafe ZIP path " + path, root =>
    {
        using var zip = Zip(("mRemoteNG.exe", "executable", null), (path, "unsafe", null));
        Throws<InvalidDataException>(() => PayloadExtractor.GetOrExtract(zip, root));
        Equal(0, Directory.EnumerateFileSystemEntries(root).Count());
    }));
}

if (!OperatingSystem.IsWindows())
{
    tests.Add(("Cache symbolic-link ancestor is rejected", root =>
    {
        string actual = Path.Combine(root, "actual");
        Directory.CreateDirectory(actual);
        string linked = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(linked, actual);
        using var zip = Zip(("mRemoteNG.exe", "executable", null));
        Throws<IOException>(() => PayloadExtractor.GetOrExtract(zip, Path.Combine(linked, "cache")));
        Equal(0, Directory.EnumerateFileSystemEntries(actual).Count());
    }));
}

int failures = 0;
foreach ((string name, Action<string> run) in tests)
{
    string root = Path.Combine(Path.GetTempPath(), "mRemoteNG-launcher-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        run(root);
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} extraction checks passed.");
return failures == 0 ? 0 : 1;

static MemoryStream Zip(params (string Name, string Contents, int? Attributes)[] files)
{
    var bytes = new MemoryStream();
    using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach ((string name, string contents, int? attributes) in files)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            if (attributes.HasValue)
                entry.ExternalAttributes = attributes.Value;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(contents);
        }
    }
    bytes.Position = 0;
    return bytes;
}

static void True(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Expected condition to be true.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
}
