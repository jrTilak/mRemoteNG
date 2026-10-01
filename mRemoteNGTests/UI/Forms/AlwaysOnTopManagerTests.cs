using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.UI.Forms;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    // Real WinForms lifecycle with a fake hotkey API; native F8 delivery and RDP
    // keyboard focus still require the Windows manual verification checklist.
    [TestFixture]
    [Platform("Win")]
    [Apartment(ApartmentState.STA)]
    public class AlwaysOnTopManagerTests
    {
        [Test]
        public void RegistrationWaitsForTheOwnerHandleAndReservesF8WithoutRepeatsOnce()
        {
            using var owner = new TestForm();
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);

            Assert.That(owner.IsHandleCreated, Is.False);
            Assert.That(api.Calls, Is.Empty);

            IntPtr handle = owner.Handle;
            manager.Register(owner);
            manager.Register(owner);

            Assert.That(api.Calls, Has.Count.EqualTo(1));
            Assert.That(api.Calls[0].Operation, Is.EqualTo("Register"));
            Assert.That(api.Calls[0].Window, Is.EqualTo(handle));
            Assert.That(api.Calls[0].Id, Is.InRange(0, 0xBFFF));
            Assert.That(api.Calls[0].Modifiers, Is.EqualTo(0x4000u));
            Assert.That(api.Calls[0].Key, Is.EqualTo(0x77u));
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
            Assert.That(api.Calls, Has.Count.EqualTo(3));
            Assert.That(api.Calls[1].Operation, Is.EqualTo("Unregister"));
            Assert.That(api.Calls[1].Window, Is.EqualTo(originalHandle));
            Assert.That(api.Calls[2].Operation, Is.EqualTo("Register"));
            Assert.That(api.Calls[2].Window, Is.EqualTo(owner.Handle));
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
            Assert.That(api.Calls, Has.Count.EqualTo(createHandle ? 2 : 0));
            if (createHandle)
            {
                Assert.That(api.Calls[1].Operation, Is.EqualTo("Unregister"));
                Assert.That(api.Calls[1].Window, Is.EqualTo(originalHandle));
            }
        }

        [Test]
        public void F8TogglesAllWindowsAndLateFloatingWindowsInheritTheCurrentState()
        {
            using var owner = new TestForm();
            using var floating = new TestForm { Owner = owner };
            using var lateFloating = new TestForm { Owner = owner };
            IntPtr ownerHandle = owner.Handle;
            _ = floating.Handle;
            var api = new FakeHotKeyApi();
            using var manager = new AlwaysOnTopManager(owner, api);
            manager.Register(floating);

            Message firstPress = F8Message(ownerHandle);
            Assert.That(manager.ProcessWindowMessage(ref firstPress), Is.True);
            manager.Register(lateFloating);
            _ = lateFloating.Handle;

            Assert.That(manager.IsEnabled, Is.True);
            Assert.That(owner.TopMost && floating.TopMost && lateFloating.TopMost, Is.True);

            Message secondPress = F8Message(ownerHandle);
            Assert.That(manager.ProcessWindowMessage(ref secondPress), Is.True);
            Assert.That(manager.IsEnabled, Is.False);
            Assert.That(owner.TopMost || floating.TopMost || lateFloating.TopMost, Is.False);
            Assert.That(api.Calls, Has.Count.EqualTo(1), "Detached windows must not reserve another hotkey.");
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
            wrongId.WParam = new IntPtr(AlwaysOnTopManager.HotKeyId + 1);
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

            Assert.That(manager.StatusError, Is.EqualTo("F8 unavailable — error 1409"));
            Message unregisteredMessage = F8Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref unregisteredMessage), Is.False);
            owner.RecreateNativeHandle();
            Assert.That(logs, Has.Count.EqualTo(1));
            Assert.That(changes, Is.Empty);

            api.RegistrationError = 0;
            owner.RecreateNativeHandle();

            Assert.That(manager.StatusError, Is.Null);
            Assert.That(changes, Is.EqualTo(new string[] { null }));
            Assert.That(api.Calls, Has.Count.EqualTo(3));
            Assert.That(api.Calls.TrueForAll(call => call.Operation == "Register"), Is.True,
                "Failed registrations must not be unregistered as if owned.");
            Message recoveredMessage = F8Message(owner.Handle);
            Assert.That(manager.ProcessWindowMessage(ref recoveredMessage), Is.True);
            Assert.That(owner.TopMost, Is.True);
        }

        private static Message F8Message(IntPtr handle) => Message.Create(handle, 0x0312,
            new IntPtr(AlwaysOnTopManager.HotKeyId), new IntPtr(0x77 << 16));

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
            internal List<(string Operation, IntPtr Window, int Id, uint Modifiers, uint Key)> Calls { get; } = new();

            public bool TryRegister(IntPtr window, int id, uint modifiers, uint key, out int error)
            {
                Calls.Add(("Register", window, id, modifiers, key));
                error = RegistrationError;
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
