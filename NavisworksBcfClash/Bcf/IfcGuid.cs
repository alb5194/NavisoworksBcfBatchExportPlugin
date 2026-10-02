using System;
using System.Text.RegularExpressions;

namespace NavisworksBcfClash.Bcf
{
    /// <summary>Conversion between System.Guid and the 22-char IFC base64 GlobalId.</summary>
    public static class IfcGuid
    {
        private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
        private static readonly Regex IfcPattern = new Regex(@"^[0-3][0-9A-Za-z_$]{21}$", RegexOptions.Compiled);

        public static bool IsIfcGuid(string value) => value != null && IfcPattern.IsMatch(value);

        public static string FromGuid(Guid guid)
        {
            // IFC uses the GUID in big-endian "string order"
            byte[] b = guid.ToByteArray();
            byte[] bytes =
            {
                b[3], b[2], b[1], b[0], b[5], b[4], b[7], b[6],
                b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]
            };

            var result = new char[22];
            // First byte -> 2 chars, then 5 groups of 3 bytes -> 4 chars each (22 total).
            result[0] = Chars[bytes[0] >> 6];
            int value = (bytes[0] & 0x3F);
            result[1] = Chars[value];
            int pos = 2;
            for (int i = 1; i < 16; i += 3)
            {
                int n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
                result[pos++] = Chars[(n >> 18) & 0x3F];
                result[pos++] = Chars[(n >> 12) & 0x3F];
                result[pos++] = Chars[(n >> 6) & 0x3F];
                result[pos++] = Chars[n & 0x3F];
            }
            return new string(result);
        }

        /// <summary>Normalizes a property value that may be an IFC GlobalId or a regular GUID string.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();
            if (IsIfcGuid(value))
                return value;

            return Guid.TryParse(value, out var g) ? FromGuid(g) : null;
        }
    }
}
