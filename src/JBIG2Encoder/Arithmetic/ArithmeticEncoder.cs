// Ported from jbig2enc (jbig2arith.cc / jbig2arith.h)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;

namespace JBIG2Encoder.Arithmetic;

/// <summary>
/// The JBIG2 MQ arithmetic encoder together with the adaptive contexts used for generic-region
/// bitmaps, the integer procedures (IAx) and symbol IDs (IAID). One instance produces one
/// arithmetic-coded data stream.
/// </summary>
internal sealed class ArithmeticEncoder
{
    // The integer procedure numbers (one adaptive context table each).
    internal const int IAAI = 0;
    internal const int IADH = 1;
    internal const int IADS = 2;
    internal const int IADT = 3;
    internal const int IADW = 4;
    internal const int IAEX = 5;
    internal const int IAFS = 6;
    internal const int IAIT = 7;
    internal const int IARDH = 8;
    internal const int IARDW = 9;
    internal const int IARDX = 10;
    internal const int IARDY = 11;
    internal const int IARI = 12;

    private const int MaxContexts = 65536;
    private const int IntProcCount = 13;
    private const int IntContextSize = 512;

    /// <summary>Context used for the TPGD "typical prediction" bit of template 0 (T.88 6.2.5.7).</summary>
    private const int TpgdContext = 0x9b25;

    #region State table (T.88 Table E.1)

    // Table E.1 lists 47 states; index 46 is a fixed-probability state that is never used by
    // an encoder, so it is omitted. Each state exists twice: for MPS = 0 (index n) and for
    // MPS = 1 (index n + 46).
    private static readonly ushort[] BaseQe =
    {
        0x5601, 0x3401, 0x1801, 0x0ac1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801,
        0x3001, 0x2401, 0x1c01, 0x1601, 0x5601, 0x5401, 0x5101, 0x4801, 0x3801, 0x3401,
        0x3001, 0x2801, 0x2401, 0x2201, 0x1c01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101,
        0x0ac1, 0x09c1, 0x08a1, 0x0521, 0x0441, 0x02a1, 0x0221, 0x0141, 0x0111, 0x0085,
        0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001,
    };

    private static readonly byte[] BaseNextMps =
    {
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10,
        11, 12, 13, 29, 15, 16, 17, 18, 19, 20,
        21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
        31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
        41, 42, 43, 44, 45, 45,
    };

    private static readonly byte[] BaseNextLps =
    {
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14,
        17, 18, 20, 21, 14, 14, 15, 16, 17, 18,
        19, 19, 20, 21, 22, 23, 24, 25, 26, 27,
        28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
        38, 39, 40, 41, 42, 43,
    };

    // States whose LPS transition also flips the MPS sense ("SWITCH" column of Table E.1).
    private static bool SwitchesOnLps(int state) => state == 0 || state == 6 || state == 14;

    private static readonly ushort[] Qe = new ushort[92];
    private static readonly byte[] NextMps = new byte[92];
    private static readonly byte[] NextLps = new byte[92];

    static ArithmeticEncoder()
    {
        for (int s = 0; s < 46; s++)
        {
            bool sw = SwitchesOnLps(s);

            // MPS = 0
            Qe[s] = BaseQe[s];
            NextMps[s] = BaseNextMps[s];
            NextLps[s] = (byte)(BaseNextLps[s] + (sw ? 46 : 0));

            // MPS = 1
            Qe[s + 46] = BaseQe[s];
            NextMps[s + 46] = (byte)(BaseNextMps[s] + 46);
            NextLps[s + 46] = (byte)(BaseNextLps[s] + (sw ? 0 : 46));
        }
    }

    #endregion

    #region Integer encoding table

    private readonly struct IntRange
    {
        public IntRange(int bot, int top, byte data, byte bits, int delta, byte intBits)
        {
            Bot = bot; Top = top; Data = data; Bits = bits; Delta = delta; IntBits = intBits;
        }

        public readonly int Bot;
        public readonly int Top;      // the range of numbers for which this entry is valid
        public readonly byte Data;    // the bits of data to write first (low bits first)...
        public readonly byte Bits;    // ...and how many of them are valid
        public readonly int Delta;    // amount subtracted from |value| before it is encoded
        public readonly byte IntBits; // number of bits used to encode the (adjusted) magnitude
    }

    private static readonly IntRange[] IntRanges =
    {
        new(0, 3, 0, 2, 0, 2),
        new(-1, -1, 9, 4, 0, 0),
        new(-3, -2, 5, 3, 2, 1),
        new(4, 19, 2, 3, 4, 4),
        new(-19, -4, 3, 3, 4, 4),
        new(20, 83, 6, 4, 20, 6),
        new(-83, -20, 7, 4, 20, 6),
        new(84, 339, 14, 5, 84, 8),
        new(-339, -84, 15, 5, 84, 8),
        new(340, 4435, 30, 6, 340, 12),
        new(-4435, -340, 31, 6, 340, 12),
        new(4436, 2000000000, 62, 6, 4436, 32),
        new(-2000000000, -4436, 63, 6, 4436, 32),
    };

    #endregion

    // Arithmetic coder registers.
    private uint _c;
    private uint _a;
    private int _ct;
    private byte _b;
    private int _bp;

    // Output.
    private byte[] _out = new byte[4096];
    private int _outLen;

    // Adaptive contexts.
    private readonly byte[] _context = new byte[MaxContexts];
    private readonly byte[][] _intContext;
    private byte[]? _iaidContext;

    public ArithmeticEncoder()
    {
        _intContext = new byte[IntProcCount][];
        for (int i = 0; i < IntProcCount; i++) _intContext[i] = new byte[IntContextSize];
        InitCoder();
    }

    /// <summary>Number of bytes produced so far (final once <see cref="Finish"/> has been called).</summary>
    public int Length => _outLen;

    public byte[] ToArray() => _out.AsSpan(0, _outLen).ToArray();

    public ReadOnlySpan<byte> AsSpan() => _out.AsSpan(0, _outLen);

    private void InitCoder()
    {
        _a = 0x8000;
        _c = 0;
        _ct = 12;
        _bp = -1;
        _b = 0;
    }

    private void Emit()
    {
        if (_outLen == _out.Length) Array.Resize(ref _out, _out.Length * 2);
        _out[_outLen++] = _b;
    }

    /// <summary>The BYTEOUT procedure of the standard.</summary>
    private void ByteOut()
    {
        bool useRightBlock;
        if (_b == 0xff)
        {
            useRightBlock = true;
        }
        else if (_c < 0x8000000)
        {
            useRightBlock = false;
        }
        else
        {
            _b += 1;
            if (_b != 0xff)
            {
                useRightBlock = false;
            }
            else
            {
                _c &= 0x7ffffff;
                useRightBlock = true;
            }
        }

        if (_bp >= 0) Emit();

        if (useRightBlock)
        {
            _b = (byte)(_c >> 20);
            _bp++;
            _c &= 0xfffff;
            _ct = 7;
        }
        else
        {
            _b = (byte)(_c >> 19);
            _bp++;
            _c &= 0x7ffff;
            _ct = 8;
        }
    }

    /// <summary>A merging of the ENCODE, CODELPS and CODEMPS procedures of the standard.</summary>
    internal void EncodeBit(byte[] context, int index, int d)
    {
        int state = context[index];
        int mps = state >= 46 ? 1 : 0;
        uint qe = Qe[state];

        if (d != mps)
        {
            // CODELPS
            _a -= qe;
            if (_a < qe) _c += qe; else _a = qe;
            context[index] = NextLps[state];
        }
        else
        {
            // CODEMPS
            _a -= qe;
            if ((_a & 0x8000) != 0)
            {
                _c += qe;
                return;
            }

            if (_a < qe) _a = qe; else _c += qe;
            context[index] = NextMps[state];
        }

        // RENORME
        do
        {
            _a <<= 1;
            _c <<= 1;
            _ct -= 1;
            if (_ct == 0) ByteOut();
        }
        while ((_a & 0x8000) == 0);
    }

    /// <summary>
    /// The FLUSH procedure of the standard: terminates the arithmetic-coded data and appends
    /// the 0xFF 0xAC marker.
    /// </summary>
    public void Finish()
    {
        // SETBITS
        uint tempc = _c + _a;
        _c |= 0xffff;
        if (_c >= tempc) _c -= 0x8000;

        _c <<= _ct;
        ByteOut();
        _c <<= _ct;
        ByteOut();
        Emit();
        if (_b != 0xff)
        {
            _b = 0xff;
            Emit();
        }
        _b = 0xac;
        Emit();
    }

    /// <summary>Encodes an integer with the given IAx procedure (T.88 Annex A.2).</summary>
    public void EncodeInt(int proc, int value)
    {
        if (value > 2000000000 || value < -2000000000) throw new ArgumentOutOfRangeException(nameof(value));

        byte[] context = _intContext[proc];
        uint prev = 1;

        int i = 0;
        while (!(IntRanges[i].Bot <= value && IntRanges[i].Top >= value)) i++;
        IntRange range = IntRanges[i];

        if (value < 0) value = -value;
        value -= range.Delta;

        byte data = range.Data;
        for (int j = 0; j < range.Bits; j++)
        {
            int v = data & 1;
            EncodeBit(context, (int)prev, v);
            data >>= 1;
            prev = NextPrev(prev, v);
        }

        // Move the magnitude bits to the top of the word.
        uint bits = (uint)value;
        if (range.IntBits != 0) bits <<= 32 - range.IntBits;
        for (int j = 0; j < range.IntBits; j++)
        {
            int v = (int)(bits >> 31);
            EncodeBit(context, (int)prev, v);
            bits <<= 1;
            prev = NextPrev(prev, v);
        }
    }

    private static uint NextPrev(uint prev, int bit) =>
        (prev & 0x100) != 0 ? (((prev << 1) | (uint)bit) & 0x1ff) | 0x100 : (prev << 1) | (uint)bit;

    /// <summary>Encodes the out-of-band value for the given IAx procedure.</summary>
    public void EncodeOob(int proc)
    {
        byte[] context = _intContext[proc];
        EncodeBit(context, 1, 1);
        EncodeBit(context, 3, 0);
        EncodeBit(context, 6, 0);
        EncodeBit(context, 12, 0);
    }

    /// <summary>Encodes a symbol ID using <paramref name="symCodeLength"/> bits (IAID).</summary>
    public void EncodeIaid(int symCodeLength, int value)
    {
        _iaidContext ??= new byte[1 << symCodeLength];
        uint mask = (uint)((1 << (symCodeLength + 1)) - 1);

        uint bits = (uint)value;
        if (symCodeLength != 0) bits <<= 32 - symCodeLength;
        uint prev = 1;
        for (int i = 0; i < symCodeLength; i++)
        {
            int tval = (int)(prev & mask);
            int v = (int)(bits >> 31);
            EncodeBit(_iaidContext, tval, v);
            prev = (prev << 1) | (uint)v;
            bits <<= 1;
        }
    }

    /// <summary>
    /// Encodes a bitmap as a generic region using template 0 with the default adaptive-template
    /// pixels (A1=(3,-1), A2=(-3,-1), A3=(2,-2), A4=(-2,-2)).
    /// </summary>
    /// <param name="bitmap">The bitmap; padding bits at the end of every row must be zero (always true for <see cref="BinaryBitmap"/>).</param>
    /// <param name="duplicateLineRemoval">When true, TPGD (typical prediction of duplicate lines) is used.</param>
    public void EncodeBitmap(BinaryBitmap bitmap, bool duplicateLineRemoval)
    {
        ReadOnlySpan<uint> data = bitmap.Words;
        int mx = bitmap.Width;
        int my = bitmap.Height;
        int wordsPerRow = bitmap.WordsPerRow;
        byte[] context = _context;

        int ltp = 0, sltp = 0;

        for (int y = 0; y < my; y++)
        {
            // The w* values hold words from each of the three rows that form the context: w1 is two rows
            // up, w2 one row up and w3 is the current row. The next bit to roll into the context is
            // kept at the top of each word.
            uint w1 = 0, w2 = 0;

            if (y >= 2) w1 = data[(y - 2) * wordsPerRow];
            if (y >= 1)
            {
                w2 = data[(y - 1) * wordsPerRow];

                if (duplicateLineRemoval)
                {
                    // It's possible that the last row was the same as this row.
                    if (data.Slice(y * wordsPerRow, wordsPerRow).SequenceEqual(data.Slice((y - 1) * wordsPerRow, wordsPerRow)))
                    {
                        sltp = ltp ^ 1;
                        ltp = 1;
                    }
                    else
                    {
                        sltp = ltp;
                        ltp = 0;
                    }
                }
            }

            if (duplicateLineRemoval)
            {
                EncodeBit(context, TpgdContext, sltp);
                if (ltp != 0) continue;
            }

            uint w3 = data[y * wordsPerRow];

            // The top bits are the start of the context rows.
            uint c1 = w1 >> 29;
            uint c2 = w2 >> 28;
            w1 <<= 3;
            w2 <<= 4;
            uint c3 = 0;

            for (int x = 0; x < mx; x++)
            {
                int tval = (int)((c1 << 11) | (c2 << 4) | c3);
                int v = (int)(w3 >> 31);

                EncodeBit(context, tval, v);

                c1 <<= 1;
                c2 <<= 1;
                c3 <<= 1;
                c1 |= w1 >> 31;
                c2 |= w2 >> 31;
                c3 |= (uint)v;

                int m = x & 31;
                if (m == 28 && y >= 2)
                {
                    // Need to roll in another word from two lines up.
                    int wordNo = (x >> 5) + 1;
                    w1 = wordNo >= wordsPerRow ? 0 : data[(y - 2) * wordsPerRow + wordNo];
                }
                else
                {
                    w1 <<= 1;
                }

                if (m == 27 && y >= 1)
                {
                    // Need to roll in another word from the last line.
                    int wordNo = (x >> 5) + 1;
                    w2 = wordNo >= wordsPerRow ? 0 : data[(y - 1) * wordsPerRow + wordNo];
                }
                else
                {
                    w2 <<= 1;
                }

                if (m == 31)
                {
                    // Need to roll in another word from this line.
                    int wordNo = (x >> 5) + 1;
                    w3 = wordNo >= wordsPerRow ? 0 : data[y * wordsPerRow + wordNo];
                }
                else
                {
                    w3 <<= 1;
                }

                c1 &= 31;
                c2 &= 127;
                c3 &= 15;
            }
        }
    }
}
