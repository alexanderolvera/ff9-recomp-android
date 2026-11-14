// Unity 5.2 serialized files (format 15, little-endian data, with or without type trees): the object table,
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
    public readonly List<TypeTree?> Trees = new();
    public bool HasTypeTrees;

    /// Format 15: the object's type ID is the class ID (negative for script types), matched against
    /// the type list.
    public TypeTree? TreeOf(ObjectInfo o) {
        for (int i = 0; i < Types.Count; i++) if (Types[i].ClassId == o.TypeIndex) return Trees[i];
        return null;
    }
    readonly Mem data;
    readonly long baseOffset;

    /// data: backing memory; baseOffset: where this serialized file starts in it (bundles hold one or more).
    public SerializedFile(string name, Mem data, long baseOffset = 0) {
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
        if (h.Memory.Length <= baseOffset + 16 || new BinReader(data, baseOffset + 16).U8() != 0) throw new InvalidDataException($"{name}: big-endian data");
        var r = new BinReader(data, baseOffset + 20);
        UnityVersion = r.CString();
        Platform = r.I32();
        HasTypeTrees = r.U8() != 0;
        int nt = r.I32();
        for (int i = 0; i < nt; i++) {
            int cid = r.I32();
            byte[]? sh = null;
            if (cid < 0) sh = r.Bytes(16);
            r.Skip(16);
            Types.Add((cid, sh));
            Trees.Add(HasTypeTrees ? TypeTree.Read(r) : null);
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

    public static SerializedFile Open(string path) => new(Path.GetFileName(path), Mem.Map(path));

    public Mem Memory => data;

    public BinReader Reader(long pathId) {
        var o = Objects[pathId];
        return new BinReader(data, o.Offset) { End = o.Offset + o.Size };
    }

    public BinReader Reader(ObjectInfo o) => new(data, o.Offset) { End = o.Offset + o.Size };
}

/// A type tree (Unity 5 blob format): flattened nodes with depths, names and types.
public sealed class TypeTree {
    public sealed class Node {
        public string Type = "", Name = "";
        public int Depth, ByteSize, Flags;
        public bool IsArray;
        public readonly List<Node> Children = new();
        public bool Align => (Flags & 0x4000) != 0;
        public override string ToString() => $"{Type} {Name}";
    }

    public Node Root = null!;

    const string Common = "AABB\0AnimationClip\0AnimationCurve\0AnimationState\0Array\0Base\0BitField\0bitset\0bool\0char\0ColorRGBA\0Component\0data\0deque\0double\0dynamic_array\0FastPropertyName\0first\0float\0Font\0GameObject\0Generic Mono\0GradientNEW\0GUID\0GUIStyle\0int\0list\0long long\0map\0Matrix4x4f\0MdFour\0MonoBehaviour\0MonoScript\0m_ByteSize\0m_Curve\0m_EditorClassIdentifier\0m_EditorHideFlags\0m_Enabled\0m_ExtensionPtr\0m_GameObject\0m_Index\0m_IsArray\0m_IsStatic\0m_MetaFlag\0m_Name\0m_ObjectHideFlags\0m_PrefabInternal\0m_PrefabParentObject\0m_Script\0m_StaticEditorFlags\0m_Type\0m_Version\0Object\0pair\0PPtr<Component>\0PPtr<GameObject>\0PPtr<Material>\0PPtr<MonoBehaviour>\0PPtr<MonoScript>\0PPtr<Object>\0PPtr<Prefab>\0PPtr<Sprite>\0PPtr<TextAsset>\0PPtr<Texture>\0PPtr<Texture2D>\0PPtr<Transform>\0Prefab\0Quaternionf\0Rectf\0RectInt\0RectOffset\0second\0set\0short\0size\0SInt16\0SInt32\0SInt64\0SInt8\0staticvector\0string\0TextAsset\0TextMesh\0Texture\0Texture2D\0Transform\0TypelessData\0UInt16\0UInt32\0UInt64\0UInt8\0unsigned int\0unsigned long long\0unsigned short\0vector\0Vector2f\0Vector3f\0Vector4f\0m_ScriptingClassIdentifier\0Gradient\0";

    static string Str(byte[] local, int off) {
        if (off < 0) {
            int o = off & 0x7FFFFFFF;
            int e = Common.IndexOf('\0', o);
            return o < Common.Length ? Common[o..e] : "?";
        }
        int end = Array.IndexOf(local, (byte)0, off);
        return Encoding.UTF8.GetString(local, off, (end < 0 ? local.Length : end) - off);
    }

    public static TypeTree Read(BinReader r) {
        int n = r.I32(), strSize = r.I32();
        var raw = new (int ver, int depth, bool arr, int type, int name, int size, int flags)[n];
        for (int i = 0; i < n; i++) {
            int ver = r.U16(); int depth = r.U8(); bool arr = r.U8() != 0;
            int type = r.I32(), name = r.I32(), size = r.I32(); r.I32(); int flags = r.I32();
            raw[i] = (ver, depth, arr, type, name, size, flags);
        }
        var strs = r.Bytes(strSize);
        var t = new TypeTree();
        var stack = new List<Node>();
        for (int i = 0; i < n; i++) {
            var x = raw[i];
            var node = new Node { Type = Str(strs, x.type), Name = Str(strs, x.name), Depth = x.depth, ByteSize = x.size, Flags = x.flags, IsArray = x.arr };
            if (x.depth == 0) t.Root = node;
            else stack[x.depth - 1].Children.Add(node);
            if (stack.Count > x.depth) stack[x.depth] = node; else stack.Add(node);
            while (stack.Count > x.depth + 1) stack.RemoveAt(stack.Count - 1);
        }
        return t;
    }
}

/// Backing memory for serialized files: a pinned byte array or a memory-mapped file.
public sealed unsafe class Mem : IDisposable {
    public byte* Ptr;
    public long Length;
    System.Runtime.InteropServices.GCHandle pin;
    System.IO.MemoryMappedFiles.MemoryMappedFile? mmf;
    System.IO.MemoryMappedFiles.MemoryMappedViewAccessor? view;

    public Mem(byte[] data) {
        pin = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
        Ptr = (byte*)pin.AddrOfPinnedObject();
        Length = data.Length;
    }

    public static Mem Map(string path) {
        var m = new Mem();
        long len = new FileInfo(path).Length;
        m.mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
        m.view = m.mmf.CreateViewAccessor(0, len, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
        byte* p = null;
        m.view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        m.Ptr = p + m.view.PointerOffset;
        m.Length = len;
        return m;
    }

    Mem() { }

    public void Dispose() {
        if (pin.IsAllocated) pin.Free();
        if (view != null) { view.SafeMemoryMappedViewHandle.ReleasePointer(); view.Dispose(); mmf!.Dispose(); view = null; }
        Ptr = null;
    }
}

public sealed unsafe class BinReader {
    readonly Mem m;
    public long Pos;
    public long End = long.MaxValue;
    public bool BigEndian;

    public BinReader(Mem m, long pos) { this.m = m; Pos = pos; }

    public Mem Memory => m;
    public long Remaining => End - Pos;
    public void Skip(long n) => Pos += n;
    public void Align4() => Pos = (Pos + 3) & ~3L;

    public byte U8() => m.Ptr[Pos++];
    public bool Bool() => m.Ptr[Pos++] != 0;
    public sbyte I8() => (sbyte)m.Ptr[Pos++];

    public ushort U16() {
        ushort v = *(ushort*)(m.Ptr + Pos);
        if (BigEndian) v = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        Pos += 2; return v;
    }

    public short I16() => (short)U16();

    public uint U32() {
        uint v = *(uint*)(m.Ptr + Pos);
        if (BigEndian) v = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        Pos += 4; return v;
    }

    public int I32() => (int)U32();

    public ulong U64() {
        ulong v = *(ulong*)(m.Ptr + Pos);
        if (BigEndian) v = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        Pos += 8; return v;
    }

    public long I64() => (long)U64();
    public float F32() { float v = *(float*)(m.Ptr + Pos); Pos += 4; return v; }
    public double F64() { double v = *(double*)(m.Ptr + Pos); Pos += 8; return v; }

    public byte[] Bytes(int n) {
        var b = new byte[n];
        new ReadOnlySpan<byte>(m.Ptr + Pos, n).CopyTo(b);
        Pos += n; return b;
    }

    /// Bytes at an absolute position, without moving.
    public byte[] BytesAt(long pos, int n) {
        var b = new byte[n];
        new ReadOnlySpan<byte>(m.Ptr + pos, n).CopyTo(b);
        return b;
    }

    public ReadOnlySpan<byte> Span(long pos, int n) => new(m.Ptr + pos, n);

    public string CString() {
        long e = Pos;
        while (m.Ptr[e] != 0) e++;
        string s = Encoding.UTF8.GetString(m.Ptr + Pos, (int)(e - Pos));
        Pos = e + 1; return s;
    }

    /// Unity string: int32 length, UTF-8 bytes, aligned to 4.
    public string String() {
        int n = I32();
        string s = Encoding.UTF8.GetString(m.Ptr + Pos, n);
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
