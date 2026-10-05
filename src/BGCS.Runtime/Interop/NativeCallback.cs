namespace BGCS.Runtime
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Represents a native callback that can be passed to interop functions requiring a callback to C# code.
    /// Copies share one lifetime lease so the underlying GC handle is released at most once.
    /// </summary>
    /// <typeparam name = "T">The delegate type of the callback.</typeparam>
    public readonly struct NativeCallback<T> : IDisposable, IEquatable<NativeCallback<T>> where T : Delegate
    {
        private readonly CallbackLease? m_lease;
        /// <summary>
        /// Initializes a new instance of the <see cref = "NativeCallback{T}"/> struct.
        /// </summary>
        /// <param name = "callback">The delegate to keep alive for native code.</param>
        public NativeCallback(T? callback)
        {
            this.m_lease = callback == null ? null : new(callback);
        }

        /// <summary>
        /// Gets the managed delegate represented by this callback lease.
        /// </summary>
        public T? callback => this.m_lease?.callback;
        /// <summary>
        /// Gets the shared GC handle, or the default handle after disposal.
        /// </summary>
        public GCHandle handle => this.m_lease?.handle ?? default;
        /// <summary>
        /// Gets a value indicating whether the callback is null.
        /// </summary>
        public bool isNull => this.callback == null;
        /// <summary>
        /// Gets a value indicating whether the shared GC handle is allocated.
        /// </summary>
        public bool isAllocated => this.m_lease?.isAllocated == true;
        /// <summary>
        /// Gets a value indicating whether the callback lease is empty or disposed.
        /// </summary>
        public bool isDisposed => !this.isAllocated;

        /// <summary>
        /// Releases the shared callback lease. Repeated calls and calls through copied values are safe.
        /// </summary>
        public void Dispose() => this.m_lease?.Dispose();
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is NativeCallback<T> callback && Equals(callback);
        /// <inheritdoc/>
        public bool Equals(NativeCallback<T> other) => ReferenceEquals(this.callback, other.callback);
        /// <inheritdoc/>
        public override int GetHashCode() => this.callback?.GetHashCode() ?? 0;
        /// <summary>
        /// Compares the current managed delegates referenced by two callback leases.
        /// </summary>
        /// <param name="left">
        /// The first borrowed callback lease value.
        /// </param>
        /// <param name="right">
        /// The second borrowed callback lease value.
        /// </param>
        /// <returns>
        /// True when both leases currently reference the same delegate, including two empty or disposed leases.
        /// </returns>
        public static bool operator ==(
            NativeCallback<T> left,
            NativeCallback<T> right
        ) => left.Equals(right);
        /// <summary>
        /// Compares the current managed delegates referenced by two callback leases for inequality.
        /// </summary>
        /// <param name="left">
        /// The first borrowed callback lease value.
        /// </param>
        /// <param name="right">
        /// The second borrowed callback lease value.
        /// </param>
        /// <returns>
        /// True when the leases currently reference different managed delegates.
        /// </returns>
        public static bool operator !=(
            NativeCallback<T> left,
            NativeCallback<T> right
        ) => !left.Equals(right);
        /// <summary>
        /// Returns the managed delegate currently retained by a callback lease without transferring lifetime ownership.
        /// </summary>
        /// <param name="callback">
        /// The callback lease value to read.
        /// </param>
        /// <returns>
        /// The retained delegate, or null when the lease is empty or already disposed.
        /// </returns>
        public static implicit operator T?(NativeCallback<T> callback) => callback.callback;
        private sealed class CallbackLease : IDisposable
        {
            private readonly object m_sync = new();
            private GCHandle m_handle;
            internal CallbackLease(T callback)
            {
                this.callback = callback;
                this.m_handle = GCHandle.Alloc(callback);
            }

            ~CallbackLease()
            {
                Dispose();
            }

            internal T? callback { get; private set; }

            internal GCHandle handle
            {
                get
                {
                    lock (this.m_sync)
                    {
                        return this.m_handle;
                    }
                }
            }

            internal bool isAllocated
            {
                get
                {
                    lock (this.m_sync)
                    {
                        return this.m_handle.IsAllocated;
                    }
                }
            }

            public void Dispose()
            {
                lock (this.m_sync)
                {
                    if (!this.m_handle.IsAllocated)
                    {
                        return;
                    }

                    this.m_handle.Free();
                    this.m_handle = default;
                    this.callback = null;
                }

                GC.SuppressFinalize(this);
            }
        }
    }
}
