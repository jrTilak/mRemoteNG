using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using mRemoteNG.App.Branding;
using NUnit.Framework;

namespace mRemoteNGTests.App.Branding
{
    [TestFixture]
    public class StartMenuShortcutManagerTests
    {
        private string _folder;
        private string _executable;
        private string _icon;
        private FakeShortcutStore _store;
        private StartMenuShortcutManager _manager;

        [SetUp]
        public void SetUp()
        {
            string root = Path.Combine(Path.GetTempPath(), "mRemoteNG-branding-tests");
            _folder = Path.Combine(root, "Programs", "mRemoteNG");
            _executable = Path.Combine(root, "FirstInstall", "Client.exe");
            _icon = Path.Combine(root, "Branding", "Client.ico");
            _store = new FakeShortcutStore();
            _manager = new StartMenuShortcutManager(_store);
        }

        [Test]
        public void EnablingCreatesShortcutWithNormalizedTitleAndSelectedIcon()
        {
            _manager.Apply(_folder, _executable, "  Team Desktop  ", _icon, show: true);

            Assert.That(_store.Links[Link("Team Desktop")], Is.EqualTo(_executable));
            Assert.That(_store.LastWrite, Is.EqualTo((Link("Team Desktop"), _executable, "Team Desktop", _icon)));
            Assert.That(_store.EnumeratedFolders, Is.EqualTo(new[] { _folder }));
            Assert.That(_store.Mutations, Is.EqualTo(new[] { "write:" + Link("Team Desktop") }));
        }

        [Test]
        public void RenameCreatesReplacementBeforeRemovingOldEntriesForThisInstallation()
        {
            _store.Links[Link("Old title")] = _executable;
            _store.Links[Link("Older title")] = _executable.ToUpperInvariant();
            AddUnrelatedEntries();

            _manager.Apply(_folder, _executable, "नयाँ Desktop", _icon, show: true);

            Assert.That(_store.Mutations[0], Is.EqualTo("write:" + Link("नयाँ Desktop")));
            Assert.That(_store.Mutations.Skip(1), Is.EquivalentTo(new[]
            {
                "delete:" + Link("Old title"), "delete:" + Link("Older title")
            }));
            Assert.That(_store.Links[Link("नयाँ Desktop")], Is.EqualTo(_executable));
            AssertUnrelatedEntriesPreserved();
        }

        [Test]
        public void ExistingOwnShortcutCanRefreshItsIconWithoutBeingDeleted()
        {
            _store.Links[Link("Team Desktop")] = _executable;

            _manager.Apply(_folder, _executable, "Team Desktop", _icon, show: true);

            Assert.That(_store.LastWrite, Is.EqualTo((Link("Team Desktop"), _executable, "Team Desktop", _icon)));
            Assert.That(_store.Mutations, Is.EqualTo(new[] { "write:" + Link("Team Desktop") }));
        }

        [Test]
        public void CaseOnlyTitleChangeDoesNotDeleteTheReplacementOnWindows()
        {
            _store.Links[Link("TEAM DESKTOP")] = _executable;

            _manager.Apply(_folder, _executable, "Team Desktop", _icon, show: true);

            Assert.That(_store.Mutations, Is.EqualTo(new[] { "write:" + Link("Team Desktop") }));
            Assert.That(_store.Links, Has.Count.EqualTo(1));
        }

        [Test]
        public void DisablingDeletesOnlyThisInstallationsEntriesInTheManagedFolder()
        {
            _store.Links[Link("Current title")] = _executable;
            _store.Links[Link("Old title")] = _executable;
            string anotherFolder = Path.Combine(Path.GetDirectoryName(_folder), "Another App", "Client.lnk");
            string nestedFolder = Path.Combine(_folder, "User Links", "Client.lnk");
            _store.Links[anotherFolder] = _executable;
            _store.Links[nestedFolder] = _executable;
            AddUnrelatedEntries();

            _manager.Apply(_folder, _executable, "Current title", _icon, show: false);

            Assert.That(_store.Mutations, Is.EquivalentTo(new[]
            {
                "delete:" + Link("Current title"), "delete:" + Link("Old title")
            }));
            Assert.That(_store.EnumeratedFolders, Is.EqualTo(new[] { _folder }));
            Assert.That(_store.Links.ContainsKey(anotherFolder), Is.True);
            Assert.That(_store.Links.ContainsKey(nestedFolder), Is.True);
            AssertUnrelatedEntriesPreserved();
        }

        [Test]
        public void DisablingWithoutOwnedEntriesPerformsNoWritesOrDeletes()
        {
            AddUnrelatedEntries();

            _manager.Apply(_folder, _executable, "Team Desktop", _icon, show: false);

            Assert.That(_store.Mutations, Is.Empty);
            AssertUnrelatedEntriesPreserved();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingUnrelatedOrMalformedDesiredFilenameBlocksAllMutations(bool malformed)
        {
            _store.Links[Link("Old title")] = _executable;
            string unrelatedTarget = malformed ? null : Path.Combine(Path.GetTempPath(), "OtherInstall", "Client.exe");
            _store.Links[Link("Team Desktop")] = unrelatedTarget;

            Assert.That(() => _manager.Apply(_folder, _executable, "Team Desktop", _icon, show: true),
                Throws.TypeOf<IOException>());

            Assert.That(_store.Mutations, Is.Empty);
            Assert.That(_store.Links[Link("Old title")], Is.EqualTo(_executable));
            Assert.That(_store.Links[Link("Team Desktop")], Is.EqualTo(unrelatedTarget));
        }

        [Test]
        public void FailedReplacementWriteKeepsOldShortcutAvailable()
        {
            _store.Links[Link("Old title")] = _executable;
            _store.FailWrite = true;

            Assert.That(() => _manager.Apply(_folder, _executable, "New title", _icon, show: true),
                Throws.TypeOf<IOException>());

            Assert.That(_store.Links[Link("Old title")], Is.EqualTo(_executable));
            Assert.That(_store.Links.ContainsKey(Link("New title")), Is.False);
            Assert.That(_store.Mutations, Is.EqualTo(new[] { "write:" + Link("New title") }));
        }

        [Test]
        public void FailureToInspectAnExistingShortcutStopsBeforeAnyMutation()
        {
            _store.Links[Link("Old title")] = _executable;
            _store.Links[Link("Unreadable")] = _executable;
            _store.FailReadPath = Link("Unreadable");

            Assert.That(() => _manager.Apply(_folder, _executable, "New title", _icon, show: true),
                Throws.TypeOf<UnauthorizedAccessException>());

            Assert.That(_store.Mutations, Is.Empty);
            Assert.That(_store.Links.ContainsKey(Link("Old title")), Is.True);
        }

        [Test]
        public void FailedOldShortcutDeletionReportsFailureAndKeepsWorkingReplacement()
        {
            _store.Links[Link("Old title")] = _executable;
            _store.FailDeletePath = Link("Old title");

            Assert.That(() => _manager.Apply(_folder, _executable, "New title", _icon, show: true),
                Throws.TypeOf<UnauthorizedAccessException>());

            Assert.That(_store.Links[Link("New title")], Is.EqualTo(_executable));
            Assert.That(_store.Links[Link("Old title")], Is.EqualTo(_executable));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("../Other App")]
        [TestCase("CON")]
        public void InvalidTitleFailsBeforeReadingOrChangingShortcuts(string title)
        {
            Assert.That(() => _manager.Apply(_folder, _executable, title, _icon, show: true), Throws.ArgumentException);
            Assert.That(_store.EnumeratedFolders, Is.Empty);
            Assert.That(_store.Mutations, Is.Empty);
        }

        private string Link(string name) => Path.Combine(_folder, name + ".lnk");

        private void AddUnrelatedEntries()
        {
            _store.Links[Link("Other installation")] = Path.Combine(Path.GetTempPath(), "OtherInstall", "Client.exe");
            _store.Links[Link("Unrelated app")] = Path.Combine(Path.GetTempPath(), "OtherApp.exe");
            _store.Links[Link("Malformed")] = null;
        }

        private void AssertUnrelatedEntriesPreserved()
        {
            Assert.That(_store.Links.ContainsKey(Link("Other installation")), Is.True);
            Assert.That(_store.Links.ContainsKey(Link("Unrelated app")), Is.True);
            Assert.That(_store.Links.ContainsKey(Link("Malformed")), Is.True);
        }

        private sealed class FakeShortcutStore : IStartMenuShortcutStore
        {
            internal readonly Dictionary<string, string> Links = new(StringComparer.OrdinalIgnoreCase);
            internal readonly List<string> EnumeratedFolders = new();
            internal readonly List<string> Mutations = new();
            internal (string Path, string Executable, string Title, string Icon) LastWrite;
            internal bool FailWrite;
            internal string FailReadPath;
            internal string FailDeletePath;

            public IEnumerable<string> Enumerate(string folder)
            {
                EnumeratedFolders.Add(folder);
                return Links.Keys.Where(path => string.Equals(Path.GetDirectoryName(path), folder,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            }

            public string ReadTarget(string path)
            {
                if (path == FailReadPath) throw new UnauthorizedAccessException("Cannot inspect shortcut.");
                return Links[path];
            }

            public void Write(string path, string executable, string title, string iconPath)
            {
                Mutations.Add("write:" + path);
                if (FailWrite) throw new IOException("Cannot save shortcut.");
                Links[path] = executable;
                LastWrite = (path, executable, title, iconPath);
            }

            public void Delete(string path)
            {
                Mutations.Add("delete:" + path);
                if (path == FailDeletePath) throw new UnauthorizedAccessException("Cannot remove shortcut.");
                Links.Remove(path);
            }
        }
    }
}
