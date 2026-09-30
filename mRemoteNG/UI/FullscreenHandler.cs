using System.Windows.Forms;

namespace mRemoteNG.UI
{
    // Keep the protected application chrome accessible for every fullscreen command.
    public class FullscreenHandler(Form handledForm)
    {
        public bool Value
        {
            get => handledForm.WindowState == FormWindowState.Maximized;
            set => handledForm.WindowState = value ? FormWindowState.Maximized : FormWindowState.Normal;
        }
    }
}
