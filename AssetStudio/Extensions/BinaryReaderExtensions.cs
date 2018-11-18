using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetStudio.Logging;
using AssetStudio.StudioClasses;
using SharpDX;

namespace AssetStudio.Extensions
{
    public static class BinaryReaderExtensions
    {
        public static string ReadHexByteArray(this BinaryReader reader, int length, bool resetPosition = true)
        {
            long savedPosition = reader.BaseStream.Position;

            byte[] bytes = reader.ReadBytes(length);

            if (resetPosition)
            {
                reader.BaseStream.Position = savedPosition;
            }

            return BitConverter.ToString(bytes).Replace("-", "");
        }

        public static void AlignStream(this BinaryReader reader)
        {
            long pos = reader.BaseStream.Position;
            long mod = pos % Constants.ByteAlignment;

            if (mod != 0)
            {
                reader.BaseStream.Position += Constants.ByteAlignment - mod;
            }
        }

        public static string ReadAlignedString(this BinaryReader reader)
        {
            int length = reader.ReadInt32();

            if (length > 0 && length < reader.BaseStream.Length - reader.BaseStream.Position)
            {
                byte[] stringData = reader.ReadBytes(length);
                string result = Encoding.UTF8.GetString(stringData);

                reader.AlignStream();

                return result;
            }

            return string.Empty;
        }

        public static string ReadStringToNull(this BinaryReader reader)
        {
            var bytes = new List<byte>();
            byte b;

            while (reader.BaseStream.Position != reader.BaseStream.Length && (b = reader.ReadByte()) != 0)
            {
                bytes.Add(b);
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public static Quaternion ReadQuaternion(this BinaryReader reader)
        {
            return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static Vector2 ReadVector2(this BinaryReader reader)
        {
            return new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }

        public static Vector3 ReadVector3(this BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static Vector4 ReadVector4(this BinaryReader reader)
        {
            return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static System.Drawing.RectangleF ReadRectangleF(this BinaryReader reader)
        {
            return new System.Drawing.RectangleF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static T[] ReadArray<T>(Func<T> del, int length)
        {
            var array = new T[length];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = del();
            }
            return array;
        }

        public static int[] ReadInt32Array(this BinaryReader reader, int length)
        {
            return ReadArray(reader.ReadInt32, length);
        }

        public static uint[] ReadUInt32Array(this BinaryReader reader, int length)
        {
            return ReadArray(reader.ReadUInt32, length);
        }

        public static float[] ReadSingleArray(this BinaryReader reader, int length)
        {
            return ReadArray(reader.ReadSingle, length);
        }

        public static Vector2[] ReadVector2Array(this BinaryReader reader, int length)
        {
            return ReadArray(reader.ReadVector2, length);
        }

        public static Vector4[] ReadVector4Array(this BinaryReader reader, int length)
        {
            return ReadArray(reader.ReadVector4, length);
        }
    }
}