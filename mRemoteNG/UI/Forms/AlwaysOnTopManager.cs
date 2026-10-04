using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace mRemoteNG.UI.Forms
{
    /// <summary>Owns the application's F8/F9 shortcuts and host visibility on the UI thread.</summary>
    internal sealed class AlwaysOnTopManager : IDisposable
    {
        internal const int HotKeyId = 0x5244;
        internal const int CloseHotKeyId = 0x5245;
        internal const uint NoRepeat = 0x4000;
        internal const uint VirtualKeyF8 = 0x77;
        internal const uint VirtualKeyF9 = 0x78;
        private const int WmHotKey = 0x0312;
        private readonly Form _owner;
        private readonly IWindowHotKeyApi _api;
        private readonly Action<string> _logFailure;
        private readonly Action _close;
        private readonly HashSet<Form> _windows = new();
        private readonly HashSet<Form> _restoreWindows = new();
        private readonly HashSet<int> _registeredKeys = new();
        private readonly Dictionary<int, string> _errors = new();
        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
        private IntPtr _registeredHandle;
        private Form _previousActiveWindow;
        private bool _changingVisibility;
        private bool _restoring;
        private bool _disposed;

        internal AlwaysOnTopManager(Form owner, Action<string> logFailure = null, Action close = null)
            : this(owner, new WindowsWindowHotKeyApi(), logFailure, close)
        {
        }

        internal AlwaysOnTopManager(Form owner, IWindowHotKeyApi api, Action<string> logFailure = null,
            Action close = null)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(api);
            _owner = owner;
            _api = api;
            _logFailure = logFailure;
            _close = close ?? owner.Close;
            _owner.HandleDestroyed += OnOwnerHandleDestroyed;
            Register(owner);
            RegisterHotKeys();
        }

        internal bool IsEnabled { get; private set; }
        internal bool IsHidden { get; private set; }
        internal string StatusError { get; private set; }
        internal event Action<string> FailureChanged;

        internal void Register(Form form)
        {
            EnsureUiThread();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(form);
            if (form.IsDisposed || form.Disposing) return;
            if (!form.TopLevel)
                throw new ArgumentException("Only top-level application windows can share the topmost state.", nameof(form));
            if (form.IsHandleCreated && form.InvokeRequired)
                throw new InvalidOperationException("The form and topmost manager must belong to the same UI thread.");

            if (_windows.Add(form))
            {
                form.HandleCreated += OnWindowHandleCreated;
                form.VisibleChanged += OnWindowVisibleChanged;
                form.Disposed += OnWindowDisposed;
            }
            ApplyState(form);
            SuppressHiddenWindow(form);
        }

        internal void Unregister(Form form)
        {
            EnsureUiThread();
            if (form == null || !_windows.Remove(form)) return;
            form.HandleCreated -= OnWindowHandleCreated;
            form.VisibleChanged -= OnWindowVisibleChanged;
            form.Disposed -= OnWindowDisposed;
            _restoreWindows.Remove(form);
        }

        internal bool ProcessWindowMessage(ref Message message)
        {
            EnsureUiThread();
            if (_disposed || _registeredHandle == IntPtr.Zero || message.Msg != WmHotKey ||
                message.HWnd != _registeredHandle)
                return false;

            int id = unchecked((int)message.WParam.ToInt64());
            if (!_registeredKeys.Contains(id)) return false;
            message.Result = IntPtr.Zero;
            if (id == CloseHotKeyId) _close();
            else Toggle();
            return true;
        }

        internal void Toggle()
        {
            EnsureUiThread();
            if (_disposed || !IsUsable(_owner) || _changingVisibility) return;
            if (IsHidden)
            {
                RestoreWindows();
                return;
            }
            if (!IsEnabled)
            {
                IsEnabled = true;
                ApplyState(_owner);
                foreach (Form form in _windows.ToArray())
                    if (form != _owner) ApplyState(form);
                RaiseWindows();
                return;
            }

            Form modal = FindVisibleModal();
            if (modal != null)
            {
                // Hiding a ShowDialog form can end its modal loop. Leave pending
                // decisions visible and unanswered, including security prompts.
                RaiseWindow(modal);
                return;
            }
            // A native modal prompt may disable its host without appearing in
            // Application.OpenForms. Keep that owner reachable as well.
            if (_windows.Any(form => IsUsable(form) && form.Visible &&
                (!form.Enabled || (form.IsHandleCreated && !form.CanFocus)))) return;
            // A hidden tool window needs a working global shortcut to return.
            if (!_registeredKeys.Contains(HotKeyId)) return;
            HideWindows();
        }

        private void HideWindows()
        {
            _previousActiveWindow = Form.ActiveForm;
            if (!_windows.Contains(_previousActiveWindow)) _previousActiveWindow = _owner;
            _restoreWindows.Clear();
            foreach (Form form in _windows)
                if (IsUsable(form) && form.Visible) _restoreWindows.Add(form);

            _changingVisibility = true;
            IsHidden = true;
            try
            {
                // Hide owned hosts explicitly before their owner changes native
                // visibility, so their managed Visible state stays in sync.
                foreach (Form form in _restoreWindows.ToArray())
                    if (form != _owner && IsUsable(form)) form.Hide();
                if (_restoreWindows.Contains(_owner)) _owner.Hide();
            }
            finally
            {
                _changingVisibility = false;
            }
        }

        private void RestoreWindows()
        {
            _changingVisibility = true;
            _restoring = true;
            IsHidden = false;
            try
            {
                if (_restoreWindows.Contains(_owner) && IsUsable(_owner)) _owner.Show();
                foreach (Form form in _restoreWindows.ToArray())
                    if (form != _owner && IsUsable(form)) form.Show();
            }
            finally
            {
                _restoring = false;
                _changingVisibility = false;
                _restoreWindows.Clear();
            }
            RaiseWindows(_previousActiveWindow);
        }

        private void RaiseWindows(Form preferred = null)
        {
            ApplyState(_owner);
            RaiseWindow(_owner);
            foreach (Form form in _windows.ToArray())
            {
                if (form == _owner) continue;
                ApplyState(form);
                RaiseWindow(form);
            }
            RaiseWindow(FindVisibleModal() ?? (IsUsable(preferred) && preferred.Visible ? preferred : _owner));
        }

        private static void RaiseWindow(Form form)
        {
            if (!IsUsable(form) || !form.Visible) return;
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            form.Activate();
        }

        private static Form FindVisibleModal() => Application.OpenForms.Cast<Form>().LastOrDefault(form =>
            IsUsable(form) && form.IsHandleCreated && !form.InvokeRequired && form.Visible && form.Modal);

        private static bool IsUsable(Form form) => form is { IsDisposed: false, Disposing: false, TopLevel: true };

        private void SuppressHiddenWindow(Form form)
        {
            if (!IsUsable(form) || !form.Visible) return;
            if (IsHidden)
            {
                // Defer an explicit Show request until F8 restores the app. A
                // float that was merely registered stays outside this snapshot.
                _restoreWindows.Add(form);
                form.Hide();
            }
            else if (_restoring && !_restoreWindows.Contains(form)) form.Hide();
        }

        private void ApplyState(Form form)
        {
            if (IsUsable(form) && form.TopMost != IsEnabled)
                form.TopMost = IsEnabled;
        }

        private void OnWindowHandleCreated(object sender, EventArgs e)
        {
            ApplyState((Form)sender);
            if (sender == _owner) RegisterHotKeys();
        }

        private void OnWindowVisibleChanged(object sender, EventArgs e) => SuppressHiddenWindow((Form)sender);

        private void OnOwnerHandleDestroyed(object sender, EventArgs e) => UnregisterHotKeys();

        private void OnWindowDisposed(object sender, EventArgs e)
        {
            if (sender == _owner) Dispose();
            else Unregister((Form)sender);
        }

        private void RegisterHotKeys()
        {
            if (_disposed || _owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated) return;
            IntPtr handle = _owner.Handle;
            if (_registeredHandle == handle) return;
            UnregisterHotKeys();
            _registeredHandle = handle;
            foreach ((int id, uint key, string name) in new[]
            {
                (HotKeyId, VirtualKeyF8, "F8"), (CloseHotKeyId, VirtualKeyF9, "F9")
            })
            {
                if (_api.TryRegister(handle, id, NoRepeat, key, out int error))
                {
                    _registeredKeys.Add(id);
                    _errors.Remove(id);
                }
                else _errors[id] = $"{name} unavailable — error {error}";
            }
            UpdateStatusError();
            if (IsHidden && !_registeredKeys.Contains(HotKeyId)) RestoreWindows();
        }

        private void UnregisterHotKeys()
        {
            if (_registeredHandle == IntPtr.Zero) return;
            // Keep the original HWND: reading owner.Handle during destruction
            // could create a replacement handle and unregister the wrong window.
            IntPtr handle = _registeredHandle;
            _registeredHandle = IntPtr.Zero;
            foreach (int id in _registeredKeys.ToArray())
            {
                _registeredKeys.Remove(id);
                if (!_api.TryUnregister(handle, id, out int error))
                    _errors[id] = $"{(id == HotKeyId ? "F8" : "F9")} shortcut cleanup failed — error {error}";
            }
            UpdateStatusError();
        }

        private void UpdateStatusError()
        {
            string error = _errors.Count == 0 ? null : string.Join(" | ", _errors.OrderBy(pair => pair.Key).Select(pair => pair.Value));
            if (StatusError == error) return;
            StatusError = error;
            if (error != null) _logFailure?.Invoke(error);
            FailureChanged?.Invoke(error);
        }

        private void EnsureUiThread()
        {
            if (Environment.CurrentManagedThreadId != _uiThreadId)
                throw new InvalidOperationException("Windows and F8/F9 must be managed on their owning UI thread.");
        }

        public void Dispose()
        {
            EnsureUiThread();
            if (_disposed) return;
            _disposed = true;
            UnregisterHotKeys();
            _owner.HandleDestroyed -= OnOwnerHandleDestroyed;
            foreach (Form form in _windows.ToArray()) Unregister(form);
        }
    }

    internal interface IWindowHotKeyApi
    {
        bool TryRegister(IntPtr window, int id, uint modifiers, uint key, out int error);
        bool TryUnregister(IntPtr window, int id, out int error);
    }

    internal sealed class WindowsWindowHotKeyApi : IWindowHotKeyApi
    {
        public bool TryRegister(IntPtr window, int id, uint modifiers, uint key, out int error)
        {
            bool success = RegisterHotKey(window, id, modifiers, key);
            error = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }

        public bool TryUnregister(IntPtr window, int id, out int error)
        {
            bool success = UnregisterHotKey(window, id);
            error = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
