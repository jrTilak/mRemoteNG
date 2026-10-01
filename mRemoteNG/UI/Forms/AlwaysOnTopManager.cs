using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace mRemoteNG.UI.Forms
{
    /// <summary>Owns the application's F8 registration and shared topmost state on the UI thread.</summary>
    internal sealed class AlwaysOnTopManager : IDisposable
    {
        internal const int HotKeyId = 0x5244;
        internal const uint NoRepeat = 0x4000;
        internal const uint VirtualKeyF8 = 0x77;
        private const int WmHotKey = 0x0312;
        private readonly Form _owner;
        private readonly IWindowHotKeyApi _api;
        private readonly Action<string> _logFailure;
        private readonly HashSet<Form> _windows = new();
        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
        private IntPtr _registeredHandle;
        private bool _disposed;

        internal AlwaysOnTopManager(Form owner, Action<string> logFailure = null)
            : this(owner, new WindowsWindowHotKeyApi(), logFailure)
        {
        }

        internal AlwaysOnTopManager(Form owner, IWindowHotKeyApi api, Action<string> logFailure = null)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(api);
            _owner = owner;
            _api = api;
            _logFailure = logFailure;
            _owner.HandleDestroyed += OnOwnerHandleDestroyed;
            Register(owner);
            RegisterHotKey();
        }

        internal bool IsEnabled { get; private set; }
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
                form.Disposed += OnWindowDisposed;
            }
            ApplyState(form);
        }

        internal void Unregister(Form form)
        {
            EnsureUiThread();
            if (form == null || !_windows.Remove(form)) return;
            form.HandleCreated -= OnWindowHandleCreated;
            form.Disposed -= OnWindowDisposed;
        }

        internal bool ProcessWindowMessage(ref Message message)
        {
            EnsureUiThread();
            if (_disposed || _registeredHandle == IntPtr.Zero || message.Msg != WmHotKey ||
                message.HWnd != _registeredHandle || message.WParam != new IntPtr(HotKeyId))
                return false;

            Toggle();
            message.Result = IntPtr.Zero;
            return true;
        }

        internal void Toggle()
        {
            EnsureUiThread();
            if (_disposed || _owner.IsDisposed || _owner.Disposing) return;
            IsEnabled = !IsEnabled;
            // Windows can change owned-window z-order with their owner. Apply the
            // owner's state first, then keep each detached form's managed flag in sync.
            ApplyState(_owner);
            foreach (Form form in _windows.ToArray())
                if (form != _owner) ApplyState(form);
        }

        private void ApplyState(Form form)
        {
            if (!form.IsDisposed && !form.Disposing && form.TopLevel && form.TopMost != IsEnabled)
                form.TopMost = IsEnabled;
        }

        private void OnWindowHandleCreated(object sender, EventArgs e)
        {
            ApplyState((Form)sender);
            if (sender == _owner) RegisterHotKey();
        }

        private void OnOwnerHandleDestroyed(object sender, EventArgs e) => UnregisterHotKey();

        private void OnWindowDisposed(object sender, EventArgs e)
        {
            if (sender == _owner) Dispose();
            else Unregister((Form)sender);
        }

        private void RegisterHotKey()
        {
            if (_disposed || _owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated) return;
            IntPtr handle = _owner.Handle;
            if (_registeredHandle == handle) return;
            UnregisterHotKey();

            if (_api.TryRegister(handle, HotKeyId, NoRepeat, VirtualKeyF8, out int error))
            {
                _registeredHandle = handle;
                SetStatusError(null);
            }
            else
            {
                SetStatusError($"F8 unavailable — error {error}");
            }
        }

        private void UnregisterHotKey()
        {
            if (_registeredHandle == IntPtr.Zero) return;
            // Keep the original HWND: reading owner.Handle during destruction
            // could create a replacement handle and unregister the wrong window.
            IntPtr handle = _registeredHandle;
            _registeredHandle = IntPtr.Zero;
            if (!_api.TryUnregister(handle, HotKeyId, out int error))
                SetStatusError($"F8 shortcut cleanup failed — error {error}.");
        }

        private void SetStatusError(string error)
        {
            if (StatusError == error) return;
            StatusError = error;
            if (error != null) _logFailure?.Invoke(error);
            FailureChanged?.Invoke(error);
        }

        private void EnsureUiThread()
        {
            if (Environment.CurrentManagedThreadId != _uiThreadId)
                throw new InvalidOperationException("Topmost windows and F8 must be managed on their owning UI thread.");
        }

        public void Dispose()
        {
            EnsureUiThread();
            if (_disposed) return;
            _disposed = true;
            UnregisterHotKey();
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
