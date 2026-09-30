using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace mRemoteNG.UI.CaptureProtection
{
    /// <summary>Own and use this manager on the forms' UI thread. It never touches handles in a worker thread.</summary>
    public sealed class CaptureProtectionManager : IDisposable
    {
        private readonly Dictionary<Form, Registration> _registrations = new();
        private readonly Timer _timer;
        private readonly CaptureProtectionPolicy _policy;
        private readonly Action<string> _logFailure;
        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
        private bool _disposed;

        public CaptureProtectionManager(Action<string> logFailure = null, int intervalMilliseconds = 500)
            : this(new WindowsDisplayAffinityApi(), logFailure, intervalMilliseconds)
        {
        }

        internal CaptureProtectionManager(IDisplayAffinityApi api, Action<string> logFailure = null, int intervalMilliseconds = 500)
        {
            ArgumentNullException.ThrowIfNull(api);
            if (intervalMilliseconds < 250 || intervalMilliseconds > 5000)
                throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds), "Use an interval between 250 and 5000 milliseconds.");

            _policy = new CaptureProtectionPolicy(api);
            _logFailure = logFailure;
            _timer = new Timer { Interval = intervalMilliseconds };
            _timer.Tick += OnTimerTick;
        }

        public void Register(Form form, Action<CaptureProtectionStatus> statusChanged)
        {
            EnsureUiThread();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(form);
            if (form.IsDisposed || form.Disposing)
                return;
            if (!form.TopLevel)
                throw new ArgumentException("Register the top-level form containing the RDP control.", nameof(form));
            if (form.IsHandleCreated && form.InvokeRequired)
                throw new InvalidOperationException("The form and protection manager must belong to the same UI thread.");

            if (_registrations.TryGetValue(form, out Registration existing))
            {
                if (statusChanged != null)
                {
                    existing.StatusChanged = statusChanged;
                    statusChanged(existing.Status);
                }
                return;
            }

            var registration = new Registration(statusChanged ?? (_ => { }), form.WindowState);
            _registrations.Add(form, registration);
            form.HandleCreated += OnApplyRequested;
            form.Shown += OnApplyRequested;
            form.VisibleChanged += OnVisibleChanged;
            form.Resize += OnResize;
            form.HandleDestroyed += OnHandleDestroyed;
            form.Disposed += OnFormDisposed;
            registration.StatusChanged(CaptureProtectionStatus.Pending);
            Check(form, registration, reapply: true, allowInvisible: true);
            _timer.Start();
        }

        public void Unregister(Form form)
        {
            EnsureUiThread();
            if (form == null || !_registrations.Remove(form))
                return;
            form.HandleCreated -= OnApplyRequested;
            form.Shown -= OnApplyRequested;
            form.VisibleChanged -= OnVisibleChanged;
            form.Resize -= OnResize;
            form.HandleDestroyed -= OnHandleDestroyed;
            form.Disposed -= OnFormDisposed;
            if (_registrations.Count == 0)
                _timer.Stop();
        }

        private void OnApplyRequested(object sender, EventArgs e)
        {
            var form = (Form)sender;
            if (_registrations.TryGetValue(form, out Registration registration))
                Check(form, registration, reapply: true, allowInvisible: true);
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            if (((Form)sender).Visible)
                OnApplyRequested(sender, e);
        }

        private void OnResize(object sender, EventArgs e)
        {
            var form = (Form)sender;
            if (!_registrations.TryGetValue(form, out Registration registration))
                return;
            FormWindowState previousState = registration.WindowState;
            registration.WindowState = form.WindowState;
            if (previousState == FormWindowState.Minimized && form.WindowState != FormWindowState.Minimized)
                Check(form, registration, reapply: true);
        }

        private void OnHandleDestroyed(object sender, EventArgs e)
        {
            if (_registrations.TryGetValue((Form)sender, out Registration registration))
                Publish(registration, CaptureProtectionStatus.Pending);
        }

        private void OnFormDisposed(object sender, EventArgs e) => Unregister((Form)sender);

        private void OnTimerTick(object sender, EventArgs e) => CheckRegisteredForms();

        internal void CheckRegisteredForms()
        {
            EnsureUiThread();
            if (_disposed) return;
            // Snapshot because status callbacks can close a form and remove its registration.
            foreach (var pair in _registrations.ToArray())
                Check(pair.Key, pair.Value, reapply: false);
        }

        private void Check(Form form, Registration registration, bool reapply, bool allowInvisible = false)
        {
            if (_disposed || form.IsDisposed || form.Disposing || !form.IsHandleCreated || (!allowInvisible && !form.Visible))
                return;

            CaptureProtectionStatus status = !form.TopLevel || form.Opacity != 1 || !form.TransparencyKey.IsEmpty || form.AllowTransparency
                ? CaptureProtectionStatus.Failed(87, "The protected form must be top-level, opaque, and have no transparency key or transparency support.")
                : _policy.EnsureProtected(form.Handle, reapply);
            Publish(registration, status);
        }

        private void Publish(Registration registration, CaptureProtectionStatus status)
        {
            if (registration.LogGate.ShouldLog(status, DateTimeOffset.UtcNow))
                _logFailure?.Invoke($"{status.DisplayText}. {status.Detail}");
            if (registration.Status == status)
                return;
            registration.Status = status;
            registration.StatusChanged(status);
        }

        public void Dispose()
        {
            EnsureUiThread();
            if (_disposed)
                return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            foreach (Form form in _registrations.Keys.ToArray())
                Unregister(form);
            _timer.Dispose();
        }

        private void EnsureUiThread()
        {
            if (Environment.CurrentManagedThreadId != _uiThreadId)
                throw new InvalidOperationException("Capture protection must be accessed on its owning UI thread.");
        }

        private sealed class Registration
        {
            internal Registration(Action<CaptureProtectionStatus> statusChanged, FormWindowState state)
            {
                StatusChanged = statusChanged;
                WindowState = state;
            }

            internal Action<CaptureProtectionStatus> StatusChanged { get; set; }
            internal CaptureProtectionStatus Status { get; set; } = CaptureProtectionStatus.Pending;
            internal FormWindowState WindowState { get; set; }
            internal CaptureProtectionFailureLogGate LogGate { get; } = new();
        }
    }
}
