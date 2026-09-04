// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexus.UI.Core;

internal sealed class CastMemoryManager<TFrom, TTo>(TFrom[] values) : MemoryManager<TTo>
    where TFrom : struct
    where TTo : struct
{
    private readonly TFrom[] _values = values;

    public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_values.AsSpan());

    protected override void Dispose(bool disposing)
    {
        //
    }

    public override unsafe MemoryHandle Pin(int elementIndex = 0)
    {
        if ((uint)elementIndex > (uint)(_values.Length * Unsafe.SizeOf<TFrom>()))
            throw new ArgumentOutOfRangeException(nameof(elementIndex));

        var handle = GCHandle.Alloc(_values, GCHandleType.Pinned);
        var pointer = (byte*)handle.AddrOfPinnedObject() + elementIndex;

        return new MemoryHandle(pointer, handle);
    }

    public override void Unpin()
    {
    }
}
