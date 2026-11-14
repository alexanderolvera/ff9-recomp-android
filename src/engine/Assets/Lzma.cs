// LZMA decoder (the "alone" format: 5 property bytes, 64-bit size, range-coded stream), for
// UnityWeb asset bundles.
using System;

namespace FF9Engine.Assets;

public static class Lzma {
    sealed class RangeDecoder {
        readonly byte[] buf; int pos;
        public uint Range = 0xFFFFFFFF, Code;
        public RangeDecoder(byte[] b, int p) { buf = b; pos = p + 1; for (int i = 0; i < 4; i++) Code = (Code << 8) | Next(); }
        byte Next() => pos < buf.Length ? buf[pos++] : (byte)0;
        void Norm() { if (Range < (1u << 24)) { Range <<= 8; Code = (Code << 8) | Next(); } }

        public int Bit(ref ushort p) {
            uint bound = (Range >> 11) * p;
            int r;
            if (Code < bound) { Range = bound; p += (ushort)((2048 - p) >> 5); r = 0; }
            else { Range -= bound; Code -= bound; p -= (ushort)(p >> 5); r = 1; }
            Norm();
            return r;
        }

        public uint Direct(int n) {
            uint r = 0;
            for (; n > 0; n--) {
                Range >>= 1;
                uint t = (Code - Range) >> 31;
                Code -= Range & (t - 1);
                r = (r << 1) | (1 - t);
                Norm();
            }
            return r;
        }
    }

    static int Tree(RangeDecoder rd, ushort[] p, int off, int bits) {
        int m = 1;
        for (int i = 0; i < bits; i++) m = (m << 1) | rd.Bit(ref p[off + m]);
        return m - (1 << bits);
    }

    static int Reverse(RangeDecoder rd, ushort[] p, int off, int bits) {
        int m = 1, sym = 0;
        for (int i = 0; i < bits; i++) { int b = rd.Bit(ref p[off + m]); m = (m << 1) | b; sym |= b << i; }
        return sym;
    }

    sealed class LenDecoder {
        ushort choice = 1024, choice2 = 1024;
        readonly ushort[] low = Init(16 << 4), mid = Init(16 << 3), high = Init(256);
        public int Decode(RangeDecoder rd, int posState) {
            if (rd.Bit(ref choice) == 0) return Tree(rd, low, posState << 3, 3);
            if (rd.Bit(ref choice2) == 0) return 8 + Tree(rd, mid, posState << 3, 3);
            return 16 + Tree(rd, high, 0, 8);
        }
    }

    static ushort[] Init(int n) { var a = new ushort[n]; Array.Fill(a, (ushort)1024); return a; }

    /// Decodes an LZMA-alone stream at src[off..]. outSize -1: take it from the header.
    public static byte[] Decode(byte[] src, int off, long outSize = -1) {
        int props = src[off];
        int lc = props % 9, lp = props / 9 % 5, pb = props / 45;
        uint dictSize = BitConverter.ToUInt32(src, off + 1);
        long size = BitConverter.ToInt64(src, off + 5);
        if (outSize < 0) outSize = size;
        var outp = new byte[outSize];
        var rd = new RangeDecoder(src, off + 13);
        var isMatch = Init(12 << 4); var isRep = Init(12); var isRepG0 = Init(12); var isRepG1 = Init(12); var isRepG2 = Init(12);
        var isRep0Long = Init(12 << 4);
        var literal = Init(0x300 << (lc + lp));
        var posSlot = Init(4 << 6);
        var posDec = Init(115);
        var align = Init(16);
        var lenDec = new LenDecoder(); var repLenDec = new LenDecoder();
        int state = 0;
        uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
        long op = 0;
        while (op < outSize) {
            int posState = (int)(op & ((1 << pb) - 1));
            if (rd.Bit(ref isMatch[(state << 4) + posState]) == 0) {
                byte prev = op > 0 ? outp[op - 1] : (byte)0;
                int litState = (int)(((op & ((1 << lp) - 1)) << lc) + (prev >> (8 - lc)));
                int baseIdx = 0x300 * litState;
                int sym = 1;
                if (state >= 7) {
                    int matchByte = outp[op - rep0 - 1];
                    while (sym < 0x100) {
                        int mb = (matchByte >> 7) & 1;
                        matchByte <<= 1;
                        int b = rd.Bit(ref literal[baseIdx + ((1 + mb) << 8) + sym]);
                        sym = (sym << 1) | b;
                        if (mb != b) break;
                    }
                }
                while (sym < 0x100) sym = (sym << 1) | rd.Bit(ref literal[baseIdx + sym]);
                outp[op++] = (byte)sym;
                state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                continue;
            }
            int len;
            if (rd.Bit(ref isRep[state]) != 0) {
                if (op == 0) throw new InvalidOperationException("LZMA: rep at start");
                if (rd.Bit(ref isRepG0[state]) == 0) {
                    if (rd.Bit(ref isRep0Long[(state << 4) + posState]) == 0) {
                        state = state < 7 ? 9 : 11;
                        outp[op] = outp[op - rep0 - 1]; op++;
                        continue;
                    }
                } else {
                    uint dist;
                    if (rd.Bit(ref isRepG1[state]) == 0) dist = rep1;
                    else {
                        if (rd.Bit(ref isRepG2[state]) == 0) dist = rep2;
                        else { dist = rep3; rep3 = rep2; }
                        rep2 = rep1;
                    }
                    rep1 = rep0; rep0 = dist;
                }
                len = repLenDec.Decode(rd, posState);
                state = state < 7 ? 8 : 11;
            } else {
                rep3 = rep2; rep2 = rep1; rep1 = rep0;
                len = lenDec.Decode(rd, posState);
                state = state < 7 ? 7 : 10;
                int lenState = Math.Min(len, 3);
                int slot = Tree(rd, posSlot, lenState << 6, 6);
                if (slot < 4) rep0 = (uint)slot;
                else {
                    int numDirect = (slot >> 1) - 1;
                    uint dist = (uint)((2 | (slot & 1)) << numDirect);
                    if (slot < 14) dist += (uint)Reverse(rd, posDec, (int)dist - slot - 1, numDirect);
                    else { dist += rd.Direct(numDirect - 4) << 4; dist += (uint)Reverse(rd, align, 0, 4); }
                    rep0 = dist;
                    if (rep0 == 0xFFFFFFFF) break;   // end marker
                }
            }
            len += 2;
            for (int i = 0; i < len && op < outSize; i++, op++) outp[op] = outp[op - rep0 - 1];
        }
        return outp;
    }
}
