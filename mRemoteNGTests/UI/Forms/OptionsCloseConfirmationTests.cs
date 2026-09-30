using System;
using System.Windows.Forms;
using mRemoteNG.UI.Forms;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms
{
    [TestFixture]
    public class OptionsCloseConfirmationTests
    {
        [Test]
        public void UnchangedOptionsDoNotPromptOrSave()
        {
            bool allowed = FrmOptions.ConfirmPendingChanges(false,
                () => throw new InvalidOperationException("Unexpected prompt."),
                () => throw new InvalidOperationException("Unexpected save."),
                () => throw new InvalidOperationException("Unexpected discard."));

            Assert.That(allowed, Is.True);
        }

        [TestCase(DialogResult.Cancel)]
        [TestCase(DialogResult.None)]
        public void CancelPreservesEditsAndRefusesShutdown(DialogResult response)
        {
            bool allowed = FrmOptions.ConfirmPendingChanges(true, () => response,
                () => throw new InvalidOperationException("Cancel must not save."),
                () => throw new InvalidOperationException("Cancel must not discard."));

            Assert.That(allowed, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SaveFailureRefusesShutdownAndSaveSuccessAllowsIt(bool saved)
        {
            int saveCalls = 0;
            bool allowed = FrmOptions.ConfirmPendingChanges(true, () => DialogResult.Yes,
                () => { saveCalls++; return saved; },
                () => throw new InvalidOperationException("Saving must not discard edits."));

            Assert.That(allowed, Is.EqualTo(saved));
            Assert.That(saveCalls, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitDiscardAllowsShutdownWithoutSaving()
        {
            int discardCalls = 0;
            bool allowed = FrmOptions.ConfirmPendingChanges(true, () => DialogResult.No,
                () => throw new InvalidOperationException("Discard must not save."),
                () => discardCalls++);

            Assert.That(allowed, Is.True);
            Assert.That(discardCalls, Is.EqualTo(1));
        }
    }
}
