using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VHDMounter.RustDeskBridge.Json
{
    /// <summary>
    /// RFC 8785 JSON Canonicalization Scheme (JCS) 实现。
    /// 与 VHDSelectServer/jcs.js 共享同一份 test-fixtures/jcs-vectors.json
    /// 跨语言夹具，保证两端输出逐字节相等。
    ///
    /// 关键约束：
    /// - 对象键按 UTF-16 code unit 字典序排序（§3.2.3）
    /// - 字符串短转义：\b \t \n \f \r \" \\ 为字面量；其它 0x00–0x1F 用 \u00xx；
    ///   非 ASCII 字符按原文 UTF-8 输出（§3.2.2.2）
    /// - 数字按 RFC 8785 §3.2.2.3 = ECMAScript Number.prototype.toString() 语义输出，
    ///   等价于 .NET 的 "R" 格式 + 小写指数 + 删除指数前导零，对整数则不输出小数点
    /// </summary>
    internal static class JcsCanonicalizer
    {
        /// <summary>把任意 JSON 字符串规范化为 RFC 8785 字节串（UTF-8）。</summary>
        public static byte[] Canonicalize(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Canonicalize(doc.RootElement);
        }

        /// <summary>把 <see cref="JsonElement"/> 规范化为 RFC 8785 字节串（UTF-8）。</summary>
        public static byte[] Canonicalize(JsonElement element)
        {
            using var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, new UTF8Encoding(false)))
            {
                WriteValue(element, writer);
                writer.Flush();
            }
            return ms.ToArray();
        }

        private static void WriteValue(JsonElement element, StreamWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    WriteObject(element, writer);
                    break;
                case JsonValueKind.Array:
                    WriteArray(element, writer);
                    break;
                case JsonValueKind.String:
                    WriteString(element.GetString() ?? string.Empty, writer);
                    break;
                case JsonValueKind.Number:
                    WriteNumber(element, writer);
                    break;
                case JsonValueKind.True:
                    writer.Write("true");
                    break;
                case JsonValueKind.False:
                    writer.Write("false");
                    break;
                case JsonValueKind.Null:
                    writer.Write("null");
                    break;
                default:
                    throw new InvalidDataException($"JCS 不支持的 JsonValueKind: {element.ValueKind}");
            }
        }

        private static void WriteObject(JsonElement element, StreamWriter writer)
        {
            var properties = new List<JsonProperty>();
            foreach (var property in element.EnumerateObject())
            {
                properties.Add(property);
            }

            // RFC 8785 §3.2.3：按 UTF-16 code unit 字典序排序
            properties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            writer.Write('{');
            for (var i = 0; i < properties.Count; i++)
            {
                if (i > 0) writer.Write(',');
                WriteString(properties[i].Name, writer);
                writer.Write(':');
                WriteValue(properties[i].Value, writer);
            }
            writer.Write('}');
        }

        private static void WriteArray(JsonElement element, StreamWriter writer)
        {
            writer.Write('[');
            var first = true;
            foreach (var item in element.EnumerateArray())
            {
                if (!first) writer.Write(',');
                first = false;
                WriteValue(item, writer);
            }
            writer.Write(']');
        }

        private static void WriteString(string value, StreamWriter writer)
        {
            writer.Write('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '\\': writer.Write("\\\\"); break;
                    case '"': writer.Write("\\\""); break;
                    case '\b': writer.Write("\\b"); break;
                    case '\t': writer.Write("\\t"); break;
                    case '\n': writer.Write("\\n"); break;
                    case '\f': writer.Write("\\f"); break;
                    case '\r': writer.Write("\\r"); break;
                    default:
                        if (c < 0x20)
                        {
                            writer.Write("\\u");
                            writer.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            writer.Write(c);
                        }
                        break;
                }
            }
            writer.Write('"');
        }

        private static void WriteNumber(JsonElement element, StreamWriter writer)
        {
            // System.Text.Json 对 Number 不做归一化，原始字面量仍保留在 GetRawText 里。
            // 但 GetRawText 可能返回 "1.50" 之类非规范形式，因此用 double + R 格式做归一化。
            //
            // RFC 8785 §3.2.2.3 等价于 ECMAScript Number.prototype.toString()：
            //   - 整数没有小数点；
            //   - 0 写为 "0"；
            //   - 小数末尾不带 0；
            //   - 指数形式仅在 |x| < 1e-6 或 |x| >= 1e21 时使用，且小写 e、无指数前导零。
            //
            // .NET 的 "R" / "G17" 都可能在 "1e-7" 上写成 "1E-07"，需要后处理。
            if (element.TryGetInt64(out var asInt))
            {
                writer.Write(asInt.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var asDouble = element.GetDouble();
            writer.Write(FormatEcmaScriptNumber(asDouble));
        }

        /// <summary>按 ECMAScript Number.prototype.toString() 语义格式化 double。</summary>
        public static string FormatEcmaScriptNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidDataException("JCS 不允许 NaN / Infinity 数字");
            }

            if (value == 0.0) return "0";

            // .NET "R" 在多数情况下已经接近 ECMAScript 语义；遗留差异：
            //  - "1E+21" vs "1e+21"（大小写）
            //  - "1E-07" vs "1e-7"（指数前导零）
            //  - 整数恒带 ".0"（因 double）—— 用 long 路径已经规避
            var formatted = value.ToString("R", CultureInfo.InvariantCulture);
            return NormalizeExponent(formatted);
        }

        private static string NormalizeExponent(string formatted)
        {
            var ePos = formatted.IndexOfAny(new[] { 'E', 'e' });
            if (ePos < 0) return formatted;

            var mantissa = formatted.Substring(0, ePos);
            var exponent = formatted.Substring(ePos + 1);

            var sign = string.Empty;
            if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
            {
                sign = exponent[0] == '-' ? "-" : "+";
                exponent = exponent.Substring(1);
            }

            // 删除前导零
            exponent = exponent.TrimStart('0');
            if (exponent.Length == 0) exponent = "0";

            return mantissa + "e" + sign + exponent;
        }
    }
}
