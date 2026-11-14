// Unity 5.2 serialized files (format 15, little-endian data, no type trees): the object table,
// script types and external references. Object data is read by the class readers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FF9Engine.Assets;

public sealed class ObjectInfo {
    public long PathId;
    public long Offset;     // absolute offset in the file (or in the bundle's data)
    public int Size;
    public int TypeIndex;
    public int ClassId;
    public short ScriptIndex;
}

public sealed class SerializedFile {
    public readonly string Name;
    public readonly string UnityVersion;
    public readonly int Platform;
    public readonly List<(int ClassId, byte[]? ScriptHash)> Types = new();
    public readonly Dictionary<long, ObjectInfo> Objects = new();
    public readonly List<ObjectInfo> ObjectList = new();
    public readonly List<string> Externals = new();
    readonly byte[] data;
    readonly long baseOffset;

    /// data: whole buffer; baseOffset: where this serialized file starts in it (bundles hold several).
    public SerializedFile(string name, byte[] data, long baseOffset = 0) {
        Name = name;
        this.data = data;
        this.baseOffset = baseOffset;
        var h = new BinReader(data, baseOffset);
        h.BigEndian = true;
        h.U32();                     // metadata size
        h.U32();                     // file size
        uint version = h.U32();
        uint dataOffset = h.U32();
        if (version != 15) throw new InvalidDataException($"{name}: serialized file version {version}");
        if (data[baseOffset + 16] != 0) throw new InvalidDataException($"{name}: big-endian data");
        var r = new BinReader(data, baseOffset + 20);
        UnityVersion = r.CString();
        Platform = r.I32();
        if (r.U8() != 0) throw new InvalidDataException($"{name}: type trees present");
        int nt = r.I32();
        for (int i = 0; i < nt; i++) {
            int cid = r.I32();
            byte[]? sh = null;
            if (cid < 0) sh = r.Bytes(16);
            r.Skip(16);
            Types.Add((cid, sh));
        }
        int no = r.I32();
        for (int i = 0; i < no; i++) {
            r.Align4();
            var o = new ObjectInfo();
            o.PathId = r.I64();
            o.Offset = baseOffset + dataOffset + r.U32();
            o.Size = (int)r.U32();
            o.TypeIndex = r.I32();
            o.ClassId = r.U16();
            o.ScriptIndex = r.I16();
            r.U8();                  // stripped
            Objects[o.PathId] = o;
            ObjectList.Add(o);
        }
        int ns = r.I32();
        for (int i = 0; i < ns; i++) { r.I32(); r.Align4(); r.I64(); }
        int ne = r.I32();
        for (int i = 0; i < ne; i++) {
            r.CString();
            r.Skip(16);
            r.I32();
            Externals.Add(r.CString());
        }
    }

    public static SerializedFile Open(string path) => new(Path.GetFileName(path), File.ReadAllBytes(path));

    public BinReader Reader(long pathId) {
        var o = Objects[pathId];
        return new BinReader(data, o.Offset) { End = o.Offset + o.Size };
    }

    public BinReader Reader(ObjectInfo o) => new(data, o.Offset) { End = o.Offset + o.Size };
}

public sealed class BinReader {
    readonly byte[] d;
    public long Pos;
    public long End = long.MaxValue;
    public bool BigEndian;

    public BinReader(byte[] d, long pos) { this.d = d; Pos = pos; }

    public byte[] Data => d;
    public long Remaining => End - Pos;
    public void Skip(long n) => Pos += n;
    public void Align4() => Pos = (Pos + 3) & ~3L;

    public byte U8() => d[Pos++];
    public bool Bool() => d[Pos++] != 0;
    public sbyte I8() => (sbyte)d[Pos++];

    public ushort U16() {
        ushort v = BigEndian ? (ushort)(d[Pos] << 8 | d[Pos + 1]) : BitConverter.ToUInt16(d, (int)Pos);
        Pos += 2; return v;
    }

    public short I16() => (short)U16();

    public uint U32() {
        uint v = BitConverter.ToUInt32(d, (int)Pos);
        if (BigEndian) v = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        Pos += 4; return v;
    }

    public int I32() => (int)U32();

    public ulong U64() {
        ulong v = BitConverter.ToUInt64(d, (int)Pos);
        if (BigEndian) v = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        Pos += 8; return v;
    }

    public long I64() => (long)U64();
    public float F32() { float v = BitConverter.ToSingle(d, (int)Pos); Pos += 4; return v; }
    public double F64() { double v = BitConverter.ToDouble(d, (int)Pos); Pos += 8; return v; }

    public byte[] Bytes(int n) {
        var b = new byte[n];
        Buffer.BlockCopy(d, (int)Pos, b, 0, n);
        Pos += n; return b;
    }

    public string CString() {
        int e = Array.IndexOf(d, (byte)0, (int)Pos);
        string s = Encoding.UTF8.GetString(d, (int)Pos, e - (int)Pos);
        Pos = e + 1; return s;
    }

    /// Unity string: int32 length, UTF-8 bytes, aligned to 4.
    public string String() {
        int n = I32();
        string s = Encoding.UTF8.GetString(d, (int)Pos, n);
        Pos += n; Align4(); return s;
    }

    public (int FileId, long PathId) PPtr() => (I32(), I64());

    public UnityEngine.Vector2 V2() => new(F32(), F32());
    public UnityEngine.Vector3 V3() => new(F32(), F32(), F32());
    public UnityEngine.Vector4 V4() => new(F32(), F32(), F32(), F32());
    public UnityEngine.Quaternion Quat() => new(F32(), F32(), F32(), F32());
    public UnityEngine.Color Color() => new(F32(), F32(), F32(), F32());
    public UnityEngine.Rect Rect() => new(F32(), F32(), F32(), F32());
}
