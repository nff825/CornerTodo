using System;

namespace CornerTodo
{
    /// <summary>Shared across replacement windows; used only on the UI thread.</summary>
    internal sealed class ApplicationInteraction
    {
        private int modalDepth;
        internal bool EditorOpen { get; set; }
        internal bool IsBusy { get { return EditorOpen || modalDepth != 0; } }
        internal event EventHandler ExitRequested;

        internal void RequestExit()
        {
            EventHandler handler = ExitRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        internal IDisposable EnterModal()
        {
            modalDepth++;
            return new ModalScope(this);
        }

        private sealed class ModalScope : IDisposable
        {
            private ApplicationInteraction owner;
            internal ModalScope(ApplicationInteraction owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                owner.modalDepth--; owner = null;
            }
        }
    }
}
