using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BGCS.Runtime;
using NativeLibrary = System.Runtime.InteropServices.NativeLibrary;

namespace CustomFunctionTable;

/// <summary>Adapts a producer-owned slot protocol to exact generated native symbol names.</summary>
public static unsafe class CustomSlotTable
{
    /// <summary>The explicit native signatures, calling convention and slot protocol agreed with this producer.</summary>
    public const string ContractIdentity = "custom-table/v1;Cdecl;pointer-sized-slots;7;table_add:i32(i32,i32)@6;table_multiply:i32(i32,i32)@2;table_hold:i32(callback(void*),void*)@4";

    /// <summary>Loads one module reference, validates its complete table protocol and transfers that reference to a copied export provider.</summary>
    /// <param name="path">The exact native producer library path.</param>
    /// <param name="released">An optional nonthrowing cleanup observer, called once after this module reference is freed.</param>
    /// <returns>An independent provider accepted by the generated FromProvider factory.</returns>
    public static ExternalFunctionTableProvider Open(string path, Action? released = null)
    {
        ModuleReference? lifetime = new(NativeLibrary.Load(path), released);
        try
        {
            nint module = lifetime.handle;
            var count = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(module, "custom_table_count");
            var pointerSize = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(module, "custom_table_pointer_size");
            var slotSize = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(module, "custom_table_slot_size");
            var get = (delegate* unmanaged[Cdecl]<nint*>)NativeLibrary.GetExport(module, "custom_table_get");
            var identity = (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(module, "custom_table_identity");
            if (count() != 7 || pointerSize() != (nuint)sizeof(nint) || slotSize() != (nuint)sizeof(nint))
                throw new ArgumentException("The native table extent, pointer width or callable slot width differs from this adapter.");
            byte* supplied = identity();
            byte[] expected = Encoding.UTF8.GetBytes(ContractIdentity);
            if (supplied == null) throw new ArgumentException("The native table identity is missing.");
            for (int index = 0; index < expected.Length; index++)
                if (supplied[index] == 0 || supplied[index] != expected[index]) throw new ArgumentException("The native table ABI identity differs from this adapter.");
            if (supplied[expected.Length] != 0) throw new ArgumentException("The native table ABI identity is not the complete agreed identity.");
            string suppliedIdentity = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(supplied, expected.Length));
            nint* table = get();
            if (table == null) throw new ArgumentException("The native producer returned no table storage.");
            // Reserved slots may be zero. Only named callable slots enter the checked provider.
            ExternalFunctionTableProvider provider = new(ContractIdentity, suppliedIdentity,
                ["table_add", "table_multiply", "table_hold"], [table[6], table[2], table[4]], lifetime);
            lifetime = null;
            return provider;
        }
        finally { lifetime?.Dispose(); }
    }

    private sealed class ModuleReference(nint native, Action? released) : IDisposable
    {
        private nint m_handle = native;
        internal nint handle => m_handle;
        public void Dispose()
        {
            nint previous = Interlocked.Exchange(ref m_handle, 0);
            if (previous == 0) return;
            NativeLibrary.Free(previous);
            released?.Invoke();
        }
    }
}
