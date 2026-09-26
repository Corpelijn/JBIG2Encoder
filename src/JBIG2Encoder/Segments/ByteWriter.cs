using System;
using System.Buffers.Binary;

namespace JBIG2Encoder.Segments;

/// <summary>A minimal growable big-endian byte writer.</summary>
internal sealed class ByteWriter
{
    private byte[] _buffer;
    private int _length;

    public ByteWriter(int capacity = 256)
    {
        _buffer = new byte[Math.Max(capacity, 16)];
    }

    public int Length => _length;

    private Span<byte> Reserve(int count)
    {
        if (_length + count > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        }
        Span<byte> span = _buffer.AsSpan(_length, count);
        _length += count;
        return span;
    }

    public void WriteByte(int value) => Reserve(1)[0] = (byte)value;

    public void WriteSByte(int value) => Reserve(1)[0] = unchecked((byte)(sbyte)value);

    public void WriteUInt16(int value) => BinaryPrimitives.WriteUInt16BigEndian(Reserve(2), (ushort)value);

    public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32BigEndian(Reserve(4), value);

    public void WriteBytes(ReadOnlySpan<byte> data) => data.CopyTo(Reserve(data.Length));

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
}
