using System;
using System.IO;
using System.Text;

namespace CarMecaSim.Mp.Net
{
    /// <summary>Little-endian packet writer. Frame = [u8 type][u8 flags][payload...] (length framed by Steam message).</summary>
    public sealed class PacketWriter : IDisposable
    {
        private readonly MemoryStream _ms;
        private readonly BinaryWriter _w;

        public PacketWriter(PacketType type, byte flags = 0)
        {
            _ms = new MemoryStream(64);
            _w = new BinaryWriter(_ms, Encoding.UTF8);
            _w.Write((byte)type);
            _w.Write(flags);
        }

        public PacketWriter Byte(byte v) { _w.Write(v); return this; }
        public PacketWriter Bool(bool v) { _w.Write((byte)(v ? 1 : 0)); return this; }
        public PacketWriter Int(int v) { _w.Write(v); return this; }
        public PacketWriter UInt(uint v) { _w.Write(v); return this; }
        public PacketWriter Long(long v) { _w.Write(v); return this; }
        public PacketWriter ULong(ulong v) { _w.Write(v); return this; }
        public PacketWriter Float(float v) { _w.Write(v); return this; }
        public PacketWriter Vec3(float x, float y, float z) { _w.Write(x); _w.Write(y); _w.Write(z); return this; }

        public PacketWriter Str(string s)
        {
            s ??= string.Empty;
            var bytes = Encoding.UTF8.GetBytes(s);
            _w.Write((ushort)bytes.Length);
            _w.Write(bytes);
            return this;
        }

        public PacketWriter Bytes(byte[] b)
        {
            b ??= Array.Empty<byte>();
            _w.Write(b.Length);
            _w.Write(b);
            return this;
        }

        public byte[] ToArray() { _w.Flush(); return _ms.ToArray(); }
        public void Dispose() { _w.Dispose(); _ms.Dispose(); }
    }

    /// <summary>Little-endian packet reader matching PacketWriter.</summary>
    public sealed class PacketReader
    {
        private readonly BinaryReader _r;
        public PacketType Type { get; }
        public byte Flags { get; }

        public PacketReader(byte[] data)
        {
            _r = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);
            Type = (PacketType)_r.ReadByte();
            Flags = _r.ReadByte();
        }

        public byte Byte() => _r.ReadByte();
        public bool Bool() => _r.ReadByte() != 0;
        public int Int() => _r.ReadInt32();
        public uint UInt() => _r.ReadUInt32();
        public long Long() => _r.ReadInt64();
        public ulong ULong() => _r.ReadUInt64();
        public float Float() => _r.ReadSingle();

        public void Vec3(out float x, out float y, out float z)
        { x = _r.ReadSingle(); y = _r.ReadSingle(); z = _r.ReadSingle(); }

        public string Str()
        {
            int len = _r.ReadUInt16();
            return Encoding.UTF8.GetString(_r.ReadBytes(len));
        }

        public byte[] Bytes()
        {
            int len = _r.ReadInt32();
            return _r.ReadBytes(len);
        }

        public bool HasMore => _r.BaseStream.Position < _r.BaseStream.Length;
    }
}
