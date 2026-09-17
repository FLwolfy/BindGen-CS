namespace BGCS.Runtime
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Represents a native callback that can be passed to interop functions requiring a callback to C# code.
    /// Copies share one lifetime lease so the underlying GC handle is released at most once.
    /// </summary>
    /// <typeparam name="T">The delegate type of the callback.</typeparam>
    public readonly struct NativeCallback<T> : IDisposable, IEquatable<NativeCallback<T>> where T : Delegate
    {
        private readonly CallbackLease? lease;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeCallback{T}"/> struct.
        /// </summary>
        /// <param name="callback">The delegate to keep alive for native code.</param>
        public NativeCallback(T? callback)
        {
            lease = callback == null ? null : new(callback);
        }

        /// <summary>
        /// Gets the managed delegate represented by this callback lease.
        /// </summary>
        public T? Callback => lease?.Callback;

        /// <summary>
        /// Gets the shared GC handle, or the default handle after disposal.
        /// </summary>
        public GCHandle Handle => lease?.Handle ?? default;

        /// <summary>
        /// Gets a value indicating whether the callback is null.
        /// </summary>
        public bool IsNull => Callback == null;

        /// <summary>
        /// Gets a value indicating whether the shared GC handle is allocated.
        /// </summary>
        public bool IsAllocated => lease?.IsAllocated == true;

        /// <summary>
        /// Gets a value indicating whether the callback lease is empty or disposed.
        /// </summary>
        public bool IsDisposed => !IsAllocated;

        /// <summary>
        /// Releases the shared callback lease. Repeated calls and calls through copied values are safe.
        /// </summary>
        public void Dispose() => lease?.Dispose();

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is NativeCallback<T> callback && Equals(callback);

        /// <inheritdoc/>
        public bool Equals(NativeCallback<T> other) => ReferenceEquals(Callback, other.Callback);

        /// <inheritdoc/>
        public override int GetHashCode() => Callback?.GetHashCode() ?? 0;

        /// <summary>
        /// Determines whether two <see cref="NativeCallback{T}"/> instances reference the same delegate.
        /// </summary>
        public static bool operator ==(NativeCallback<T> left, NativeCallback<T> right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="NativeCallback{T}"/> instances reference different delegates.
        /// </summary>
        public static bool operator !=(NativeCallback<T> left, NativeCallback<T> right) => !left.Equals(right);

        /// <summary>
        /// Returns the managed delegate held by a callback lease.
        /// </summary>
        public static implicit operator T?(NativeCallback<T> callback) => callback.Callback;

        private sealed class CallbackLease : IDisposable
        {
            private readonly object sync = new();
            private GCHandle handle;

            internal CallbackLease(T callback)
            {
                Callback = callback;
                handle = GCHandle.Alloc(callback);
            }

            ~CallbackLease()
            {
                Dispose();
            }

            internal T? Callback { get; private set; }

            internal GCHandle Handle
            {
                get
                {
                    lock (sync)
                    {
                        return handle;
                    }
                }
            }

            internal bool IsAllocated
            {
                get
                {
                    lock (sync)
                    {
                        return handle.IsAllocated;
                    }
                }
            }

            public void Dispose()
            {
                lock (sync)
                {
                    if (!handle.IsAllocated)
                    {
                        return;
                    }
                    handle.Free();
                    handle = default;
                    Callback = null;
                }
                GC.SuppressFinalize(this);
            }
        }
    }
}
