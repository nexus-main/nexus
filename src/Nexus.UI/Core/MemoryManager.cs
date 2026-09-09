// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexus.UI.Core;

internal sealed class CastMemoryManager<TFrom, TTo>(Memory<TFrom> values) : MemoryManager<TTo>
    where TFrom : struct
    where TTo : struct
{
    private readonly Memory<TFrom> _values = values;
    private MemoryHandle _handle;

    public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_values.Span);

    protected override void Dispose(bool disposing)
    {
        //
    }

    public override unsafe MemoryHandle Pin(int elementIndex = 0)
    {
        if ((uint)elementIndex > (uint)(_values.Length * Unsafe.SizeOf<TFrom>()))
            throw new ArgumentOutOfRangeException(nameof(elementIndex));

        _handle = _values.Pin();
        var pointer = (byte*)_handle.Pointer + elementIndex;

        return new MemoryHandle(pointer, pinnable: this);
    }

    public override void Unpin()
    {
        _handle.Dispose();
    }
}
