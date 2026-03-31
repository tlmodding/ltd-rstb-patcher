using Force.Crc32;
using System.Text;

namespace RSTBPatcher.Core;

public static class Extensions
{
    extension(string value)
    {
        public uint ToCRC32()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return Crc32Algorithm.Compute(bytes);
        }
    }
}
