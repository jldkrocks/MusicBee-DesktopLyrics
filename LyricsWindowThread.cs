using System;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // Own the lyrics window and its message loop. Never join this thread from
    // MusicBee's UI: a pending API request may itself need that UI to respond.
    internal sealed class LyricsWindowThread : IDisposable
    {
        private readonly object _gate = new object();
        private Form _form;
        private bool _closed;
        internal void Start(Func<Form> create, Action<Form> ready, Action<Exception> failed)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = create())
                    {
                        var handle = form.Handle;
                        lock (_gate)
                        {
                            if (_closed) return;
                            _form = form;
                        }
                        ready(form);
                        lock (_gate) { if (_closed) return; }
                        Application.Run(form);
                    }
                }
                catch (Exception ex) { failed(ex); }
                finally { lock (_gate) { _form = null; _closed = true; } }
            }) { IsBackground = true, Name = "DesktopLyrics UI" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        public void Dispose()
        {
            Form form;
            lock (_gate) { _closed = true; form = _form; }
            if (form == null || form.IsDisposed) return;
            try { form.BeginInvoke(new Action(() =>
            {
                // Plugin shutdown/toggle used to dispose directly. Do not let an
                // owned editor's unsaved prompt strand this background UI loop.
                if (!form.IsDisposed) form.Dispose();
                Application.ExitThread();
            })); }
            catch (InvalidOperationException) { }
        }
    }
}
