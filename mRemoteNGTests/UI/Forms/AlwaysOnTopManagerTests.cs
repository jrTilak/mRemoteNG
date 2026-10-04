using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.UI.Forms;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    // Real WinForms lifecycle with a fake hotkey API; native F8/F9 delivery and RDP
    // keyboard focus still require the Windows manual verification checklist.
    [TestFixture]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class AlwaysOnTopManagerTests
    {
        [Test]
        public void RegistrationWaitsForTheOwnerHandleAndReservesBothKeysWithoutRepeatsOnce()
        {
            using var owner = new TestForm();
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);

            Assert.That(owner.IsHandleCreated, Is.False);
            Assert.That(api.Calls, Is.Empty);

            IntPtr handle = owner.Handle;
            manager.Register(owner);
            manager.Register(owner);

            Assert.That(api.Calls, Has.Count.EqualTo(2));
            foreach (var call in api.Calls)
            {
                Assert.That(call.Operation, Is.EqualTo("Register"));
                Assert.That(call.Window, Is.EqualTo(handle));
                Assert.That(call.Id, Is.InRange(0, 0xBFFF));
                Assert.That(call.Modifiers, Is.EqualTo(0x4000u));
            }
            Assert.That(api.Calls.Select(call => call.Id), Is.Unique);
            Assert.That(api.Calls.Select(call => call.Key), Is.EqualTo(new[] { 0x77u, 0x78u }));
        }

        [Test]
        public void HandleRecreationUnregistersTheOriginalHandleBeforeRegisteringItsReplacement()
        {
            using var owner = new TestForm();
            IntPtr originalHandle = owner.Handle;
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);

            owner.RecreateNativeHandle();

            Assert.That(owner.HandleCreationCount, Is.EqualTo(2));
            Assert.That(api.Calls, Has.Count.EqualTo(6));
            foreach (var call in api.Calls.Skip(2).Take(2))
            {
                Assert.That(call.Operation, Is.EqualTo("Unregister"));
                Assert.That(call.Window, Is.EqualTo(originalHandle));
            }
            foreach (var call in api.Calls.Skip(4))
            {
                Assert.That(call.Operation, Is.EqualTo("Register"));
                Assert.That(call.Window, Is.EqualTo(owner.Handle));
            }
            // Windows can reuse an HWND value, so do not require different values.
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OwnerDisposalDoesNotCreateAHandleAndUnregistersAtMostOnce(bool createHandle)
        {
            using var owner = new TestForm();
            IntPtr originalHandle = createHandle ? owner.Handle : IntPtr.Zero;
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);
            int originalCreationCount = owner.HandleCreationCount;

            owner.Dispose();
            manager.Dispose();

            Assert.That(owner.HandleCreationCount, Is.EqualTo(originalCreationCount));
            Assert.That(owner.IsHandleCreated, Is.False);
            Assert.That(api.Calls, Has.Count.EqualTo(createHandle ? 4 : 0));
            if (createHandle)
            {
                Assert.That(api.Calls.Skip(2).Select(call => call.Operation), Is.All.EqualTo("Unregister"));
                Assert.That(api.Calls.Skip(2).Select(call => call.Window), Is.All.EqualTo(originalHandle));
            }
        }

        [Test]
        public void F8PinsThenHidesThenRestoresOnlyPreviouslyVisibleHosts()
        {
            using var owner = new TestForm();
            using var floating = new TestForm { Owner = owner };
            using var lateFloating = new TestForm { Owner = owner };
            IntPtr ownerHandle = owner.Handle;
            _ = floating.Handle;
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);
            manager.Register(floating);
            owner.Show();
            floating.Show();

            Message firstPress = F8Message(ownerHandle);
            Assert.That(manager.ProcessWindowMessage(ref firstPress), Is.True);
            manager.Register(lateFloating);
            _ = lateFloating.Handle;

            Assert.That(manager.IsEnabled, Is.True);
            Assert.That(owner.TopMost && floating.TopMost && lateFloating.TopMost, Is.True);
            Assert.That(owner.Visible && floating.Visible, Is.True);
            Assert.That(lateFloating.Visible, Is.False);

            Message secondPress = F8Message(ownerHandle);
            Assert.That(manager.ProcessWindowMessage(ref secondPress), Is.True);
            Assert.That(manager.IsHidden, Is.True);
            Assert.That(owner.Visible || floating.Visible || lateFloating.Visible, Is.False);
            Assert.That(owner.IsDisposed || floating.IsDisposed, Is.False, "Hiding must not close session hosts.");

            Message thirdPress = F8Message(ownerHandle);
            Assert.That(manager.ProcessWindowMessage(ref thirdPress), Is.True);
            Assert.That(manager.IsHidden, Is.False);
            Assert.That(manager.IsEnabled, Is.True);
            Assert.That(owner.Visible && floating.Visible, Is.True);
            Assert.That(lateFloating.Visible, Is.False, "A previously hidden float stays hidden.");
            Assert.That(owner.TopMost && floating.TopMost && lateFloating.TopMost, Is.True);
            Assert.That(api.Calls, Has.Count.EqualTo(2), "Detached windows must not reserve additional hotkeys.");
        }

        [Test]
        public void AFloatExplicitlyShownWhileHiddenIsDeferredUntilTheNextRestore()
        {
            using var owner = new TestForm();
            owner.Show();
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            manager.Toggle();
            manager.Toggle();
            using var floating = new TestForm { Owner = owner };
            manager.Register(floating);
            floating.Show();

            Assert.That(floating.Visible, Is.False);
            Assert.That(floating.TopMost, Is.True);
            manager.Toggle();
            Assert.That(owner.Visible, Is.True);
            Assert.That(floating.Visible, Is.True, "An explicit Show request must remain reachable after F8 restores the app.");
        }

        [Test]
        public void AFloatOnlyRegisteredWhileHiddenRemainsHiddenAfterRestore()
        {
            using var owner = new TestForm();
            owner.Show();
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            manager.Toggle();
            manager.Toggle();
            using var floating = new TestForm { Owner = owner };
            manager.Register(floating);

            manager.Toggle();

            Assert.That(owner.Visible, Is.True);
            Assert.That(floating.Visible, Is.False);
            Assert.That(floating.TopMost, Is.True);
        }

        [Test]
        public void F9InvokesTheProvidedClosePathWhileHidden()
        {
            using var owner = new TestForm();
            owner.Show();
            int closeCalls = 0;
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi(), close: () => closeCalls++);
            manager.Toggle();
            manager.Toggle();

            Message close = F9Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref close), Is.True);
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(manager.IsHidden, Is.True);
            Assert.That(owner.Visible, Is.False, "F9 must not expose hosts before invoking shutdown.");
        }

        [Test]
        public void F9DefaultsToTheNormalOwnerClosePath()
        {
            using var owner = new TestForm();
            owner.Show();
            bool closingCalled = false;
            owner.FormClosing += (_, _) => closingCalled = true;
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);

            Message close = F9Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref close), Is.True);
            Assert.That(closingCalled, Is.True);
            Assert.That(owner.IsDisposed, Is.True);
            Assert.That(api.Calls.Count(call => call.Operation == "Unregister"), Is.EqualTo(2));
        }

        [Test]
        public void F8DoesNotHideOrAnswerAnOpenModalDecision()
        {
            using var owner = new TestForm();
            using var dialog = new TestForm();
            owner.Show();
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            manager.Toggle();
            bool remainedVisible = false, remainedUnanswered = false, stayedShown = false;
            dialog.Shown += (_, _) =>
            {
                manager.Toggle();
                remainedVisible = dialog.Visible && owner.Visible;
                remainedUnanswered = dialog.DialogResult == DialogResult.None;
                stayedShown = !manager.IsHidden;
                dialog.DialogResult = DialogResult.Cancel;
            };

            Assert.That(dialog.ShowDialog(owner), Is.EqualTo(DialogResult.Cancel));
            Assert.That(remainedVisible, Is.True);
            Assert.That(remainedUnanswered, Is.True);
            Assert.That(stayedShown, Is.True);
        }

        [Test]
        public void F8DoesNotHideADisabledHostWhenNoManagedModalIsAvailable()
        {
            using var owner = new TestForm();
            owner.Show();
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            manager.Toggle();
            owner.Enabled = false;

            manager.Toggle();

            Assert.That(owner.Visible, Is.True);
            Assert.That(manager.IsHidden, Is.False);
        }

        [Test]
        public void F8DoesNotHideAHostDisabledByANativeModalLoop()
        {
            using var owner = new TestForm();
            owner.Show();
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            manager.Toggle();
            EnableWindow(owner.Handle, false);
            try
            {
                Assert.That(owner.CanFocus, Is.False);
                manager.Toggle();
                Assert.That(owner.Visible, Is.True);
                Assert.That(manager.IsHidden, Is.False);
            }
            finally
            {
                EnableWindow(owner.Handle, true);
            }
        }

        [Test]
        public void MessagesForAnotherWindowIdOrMessageTypeDoNotToggleTopmost()
        {
            using var owner = new TestForm();
            using var anotherWindow = new TestForm();
            IntPtr ownerHandle = owner.Handle;
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            Message wrongWindow = F8Message(anotherWindow.Handle);
            Message wrongId = F8Message(ownerHandle);
            wrongId.WParam = new IntPtr(AlwaysOnTopManager.HotKeyId + 20);
            Message wrongType = F8Message(ownerHandle);
            wrongType.Msg = 0x0100; // WM_KEYDOWN is not WM_HOTKEY.

            Assert.That(manager.ProcessWindowMessage(ref wrongWindow), Is.False);
            Assert.That(manager.ProcessWindowMessage(ref wrongId), Is.False);
            Assert.That(manager.ProcessWindowMessage(ref wrongType), Is.False);
            Assert.That(manager.IsEnabled, Is.False);
            Assert.That(owner.TopMost, Is.False);
        }

        [Test]
        public void RegistrationFailureIsDeduplicatedAndClearedAfterHandleRecreationRecovers()
        {
            using var owner = new TestForm();
            _ = owner.Handle;
            var api = new FakeHotKeyApi { RegistrationError = 1409 };
            var logs = new List<string>();
            using var manager = new AlwaysOnTopManager(owner, api, logs.Add);
            var changes = new List<string>();
            manager.FailureChanged += changes.Add;

            Assert.That(manager.StatusError, Is.EqualTo("F8 unavailable — error 1409 | F9 unavailable — error 1409"));
            Message unregisteredMessage = F8Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref unregisteredMessage), Is.False);
            owner.RecreateNativeHandle();
            Assert.That(logs, Has.Count.EqualTo(1));
            Assert.That(changes, Is.Empty);

            api.RegistrationError = 0;
            owner.RecreateNativeHandle();

            Assert.That(manager.StatusError, Is.Null);
            Assert.That(changes, Is.EqualTo(new string[] { null }));
            Assert.That(api.Calls, Has.Count.EqualTo(6));
            Assert.That(api.Calls.TrueForAll(call => call.Operation == "Register"), Is.True,
                "Failed registrations must not be unregistered as if owned.");
            Message recoveredMessage = F8Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref recoveredMessage), Is.True);
            Assert.That(owner.TopMost, Is.True);
        }

        [TestCase(AlwaysOnTopManager.HotKeyId, "F8")]
        [TestCase(AlwaysOnTopManager.CloseHotKeyId, "F9")]
        public void FailureOfOneShortcutLeavesTheOtherOperationalAndOnlyUnregistersOwnedKeys(int failedId, string name)
        {
            using var owner = new TestForm();
            _ = owner.Handle;
            var api = new FakeHotKeyApi();
            api.KeyErrors[failedId] = 1409;
            int closes = 0;
            using var manager = new AlwaysOnTopManager(owner, api, close: () => closes++);

            Assert.That(manager.StatusError, Is.EqualTo($"{name} unavailable — error 1409"));
            Message pin = F8Message(owner.Handle);
            Message close = F9Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref pin), Is.EqualTo(failedId != AlwaysOnTopManager.HotKeyId));
            Assert.That(manager.ProcessWindowMessage(ref close), Is.EqualTo(failedId != AlwaysOnTopManager.CloseHotKeyId));
            Assert.That(closes, Is.EqualTo(failedId == AlwaysOnTopManager.HotKeyId ? 1 : 0));

            manager.Dispose();
            var unregister = api.Calls.Where(call => call.Operation == "Unregister").ToArray();
            Assert.That(unregister, Has.Length.EqualTo(1));
            Assert.That(unregister[0].Id, Is.Not.EqualTo(failedId));
        }

        [Test]
        public void LosingF8DuringHiddenHandleRecreationRestoresTheHostsInsteadOfStrandingThem()
        {
            using var owner = new TestForm();
            using var floating = new TestForm { Owner = owner };
            owner.Show();
            floating.Show();
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);
            manager.Register(floating);
            manager.Toggle();
            manager.Toggle();
            api.KeyErrors[AlwaysOnTopManager.HotKeyId] = 1409;

            owner.RecreateNativeHandle();

            Assert.That(manager.IsHidden, Is.False);
            Assert.That(owner.Visible && floating.Visible, Is.True);
            Assert.That(owner.TopMost && floating.TopMost, Is.True);
            Assert.That(manager.StatusError, Is.EqualTo("F8 unavailable — error 1409"));
            manager.Toggle();
            Assert.That(manager.IsHidden, Is.False, "A failed F8 registration cannot leave a tool window hidden.");
        }

        [Test]
        public void SuccessfulHandleRecreationWhileHiddenKeepsTheRestoreShortcutAndVisibilitySnapshot()
        {
            using var owner = new TestForm();
            owner.Show();
            using var manager = new AlwaysOnTopManager(owner, new FakeHotKeyApi());
            manager.Toggle();
            manager.Toggle();

            owner.RecreateNativeHandle();

            Assert.That(owner.Visible, Is.False);
            Assert.That(manager.IsHidden, Is.True);
            Message restore = F8Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref restore), Is.True);
            Assert.That(owner.Visible, Is.True);
        }

        private static Message F8Message(IntPtr handle) => Message.Create(handle, 0x0312,
            new IntPtr(AlwaysOnTopManager.HotKeyId), new IntPtr(0x77 << 16));

        private static Message F9Message(IntPtr handle) => Message.Create(handle, 0x0312,
            new IntPtr(AlwaysOnTopManager.CloseHotKeyId), new IntPtr(0x78 << 16));

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);

        private sealed class TestForm : Form
        {
            internal TestForm() => ShowInTaskbar = false;
            internal int HandleCreationCount { get; private set; }
            internal void RecreateNativeHandle() => RecreateHandle();

            protected override void OnHandleCreated(EventArgs e)
            {
                HandleCreationCount++;
                base.OnHandleCreated(e);
            }
        }

        private sealed class FakeHotKeyApi : IWindowHotKeyApi
        {
            internal int RegistrationError { get; set; }
            internal Dictionary<int, int> KeyErrors { get; } = new();
            internal List<(string Operation, IntPtr Window, int Id, uint Modifiers, uint Key)> Calls { get; } = new();

            public bool TryRegister(IntPtr window, int id, uint modifiers, uint key, out int error)
            {
                Calls.Add(("Register", window, id, modifiers, key));
                error = KeyErrors.TryGetValue(id, out int keyError) ? keyError : RegistrationError;
                return error == 0;
            }

            public bool TryUnregister(IntPtr window, int id, out int error)
            {
                Calls.Add(("Unregister", window, id, 0, 0));
                error = 0;
                return true;
            }
        }
    }
}
