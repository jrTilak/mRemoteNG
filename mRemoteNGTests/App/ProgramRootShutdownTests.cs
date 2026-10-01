using System;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.App;
using NUnit.Framework;

namespace mRemoteNGTests.App
{
    [TestFixture]
    [Platform("Win")]
    public class ProgramRootShutdownTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SuccessfulCloseDisposesTheFormWithoutFallback(bool createHandle)
        {
            OnFreshUiThread(() =>
            {
                using var main = new Form();
                if (createHandle) _ = main.Handle;
                int exits = 0;

                ProgramRoot.CloseAfterUiException(main, false, () => exits++);

                Assert.That(main.IsDisposed, Is.True);
                Assert.That(exits, Is.Zero);
            });
        }

        [Test]
        public void SwallowedWindowMessageExceptionStillRequestsThreadExit()
        {
            OnFreshUiThread(() =>
            {
                using var main = new Form();
                _ = main.Handle;
                var expectedError = new InvalidOperationException("Synthetic cleanup failure.");
                main.FormClosing += (_, _) => throw expectedError;
                Exception dispatchedError = null;
                ThreadExceptionEventHandler handler = (_, args) => dispatchedError = args.Exception;
                Application.ThreadException += handler;
                try
                {
                    int exits = 0;
                    ProgramRoot.CloseAfterUiException(main, false, () => exits++);

                    Assert.That(dispatchedError, Is.SameAs(expectedError),
                        "The failure must travel through real WM_CLOSE dispatch, not escape Close directly.");
                    Assert.That(main.IsDisposed, Is.False);
                    Assert.That(exits, Is.EqualTo(1));
                }
                finally
                {
                    Application.ThreadException -= handler;
                }
            });
        }

        [Test]
        public void CancelledCloseStillRequestsThreadExit()
        {
            OnFreshUiThread(() =>
            {
                using var main = new Form();
                _ = main.Handle;
                main.FormClosing += (_, args) => args.Cancel = true;
                int exits = 0;

                ProgramRoot.CloseAfterUiException(main, false, () => exits++);

                Assert.That(main.IsDisposed, Is.False);
                Assert.That(exits, Is.EqualTo(1));
            });
        }

        [Test]
        public void AlreadyClosingIsNotMistakenForCompletedShutdown()
        {
            OnFreshUiThread(() =>
            {
                using var main = new Form();
                _ = main.Handle;
                int closeEvents = 0;
                int exits = 0;
                main.FormClosing += (_, _) => closeEvents++;

                ProgramRoot.CloseAfterUiException(main, true, () => exits++);

                Assert.That(closeEvents, Is.Zero, "Do not reenter in-progress cleanup.");
                Assert.That(main.IsDisposed, Is.False);
                Assert.That(exits, Is.EqualTo(1));
            });
        }

        [Test]
        public void DisposedMainDoesNotRequestAnotherExit()
        {
            OnFreshUiThread(() =>
            {
                using var main = new Form();
                main.Dispose();
                int exits = 0;

                ProgramRoot.CloseAfterUiException(main, true, () => exits++);

                Assert.That(exits, Is.Zero);
            });
        }

        private static void OnFreshUiThread(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // Thread-scoped mode must be set before any HWND is created.
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, true);
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True, "UI regression check did not complete.");
            if (failure != null) throw failure;
        }
    }
}
