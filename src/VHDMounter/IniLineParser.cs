using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VHDMounter
{
    /// <summary>
    /// 共享 INI 行解析器（被 MachineLogClientConfiguration / BridgeConfig 复用）。
    ///
    /// <para>
    /// 历史上每个 Loader 各自实现一份"<c>foreach (line in ReadAllLines) line.Trim().Split('=',2)</c>"，
    /// 表面正确但真实部署里有几个稳定的 footgun：
    /// </para>
    /// <list type="number">
    /// <item><b>无尾换行被人工编辑</b>：旧模板末行 "<c>X=1</c>" 后没有 LF，Notepad / 简单
    ///       编辑器追加新键时往往把字节流拼成 "<c>X=1NewKey=true\n</c>"，
    ///       <see cref="File.ReadAllLines"/> 把它当成一行 → 新键值整段被当成 X 的 value，
    ///       新键彻底进不了 dict。本解析器按 <c>'='</c> 切完后再扫 value，发现里面再嵌一段
    ///       "<c>&lt;Letter&gt;[A-Za-z0-9_]*=...</c>" 形态的 token 时，把它当作"被粘连的下一行"
    ///       拆开。</item>
    /// <item><b>行内 <c>;</c> 注释被吞进 value</b>：<c>X=true ; 启用</c> → 老代码 value =
    ///       "<c>true ; 启用</c>"，<c>bool.TryParse</c> 返回 false。本解析器在
    ///       value 上剥行内 <c>;</c>。</item>
    /// <item><b>BOM / 零宽空格 / 全角 <c>＝</c></b>：BOM 现在 .NET 9 也不再总被
    ///       <c>String.Trim</c> 当 whitespace 剥；零宽空格 U+200B 从来不剥；全角等号
    ///       U+FF1D 不会被 <c>Split('=')</c> 切开。本解析器先把这三类规整化掉再切。</item>
    /// </list>
    ///
    /// <para>
    /// 这是个**有意为之的宽容 parser**，专门防御"运维手工 vim 一发"造成的难复现 bug。
    /// 严格遵守 spec 形态的配置文件不会受影响（行为与原实现等价）。
    /// </para>
    /// </summary>
    internal static class IniLineParser
    {
        /// <summary>
        /// 读 <paramref name="configPath"/> 全部内容、解析成 key/value 写入
        /// <paramref name="values"/>。后写覆盖前写（与原实现一致，便于"用户在文件末尾追加 override"
        /// 这种常见用法）。
        /// </summary>
        public static void ParseInto(
            string configPath,
            IDictionary<string, string> values,
            Action<string> diagnostics = null)
        {
            if (configPath == null) throw new ArgumentNullException(nameof(configPath));
            if (values == null) throw new ArgumentNullException(nameof(values));

            string[] rawLines;
            try
            {
                rawLines = File.ReadAllLines(configPath);
            }
            catch (Exception ex)
            {
                diagnostics?.Invoke($"读取配置文件失败: {configPath} -> {ex.Message}");
                return;
            }

            for (var i = 0; i < rawLines.Length; i++)
            {
                ParseLine(rawLines[i], values, diagnostics, configPath, i + 1);
            }
        }

        // ---------- 内部 ----------

        private static void ParseLine(
            string rawLine,
            IDictionary<string, string> values,
            Action<string> diagnostics,
            string configPath,
            int lineNumber)
        {
            if (rawLine == null) return;

            // 1) 规整化：去 BOM / 零宽空格 / 全角 = → ASCII =
            var normalized = NormalizeLine(rawLine);

            // 2) trim
            var line = normalized.Trim();
            if (string.IsNullOrWhiteSpace(line)) return;
            if (line.StartsWith(";", StringComparison.Ordinal)) return;
            if (line.StartsWith("#", StringComparison.Ordinal)) return; // 兼容 # 注释（部分编辑器默认）
            if (line.StartsWith("[", StringComparison.Ordinal)) return;

            // 3) 切第一个 '='
            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) return; // 没有等号 / 等号在最前面 → 不是合法 key=value

            var key = line.Substring(0, eqIdx).Trim();
            var rawValue = line.Substring(eqIdx + 1);
            if (string.IsNullOrEmpty(key)) return;

            // 4) value 上再剥行内 ; / # 注释（注释前必须有空白，否则可能是 URL 里的 ;）
            var inlineCommentIdx = FindInlineCommentStart(rawValue);
            if (inlineCommentIdx >= 0)
            {
                rawValue = rawValue.Substring(0, inlineCommentIdx);
            }

            var value = rawValue.Trim();

            // 5) 检测"上一行没换行被粘连"的 footgun：
            //    把 value 末尾里嵌套的另一段 "<Letter>[A-Za-z0-9_]*=..." 拆出来，
            //    认作粘连进来的下一行。例如旧文件末尾 "X=1" 没 LF，用户追加
            //    "EnableRustDeskBridge=true" → 实际字节流是
            //    "X=1EnableRustDeskBridge=true"。我们先把 X=1 的部分写进 dict、
            //    再把 EnableRustDeskBridge=true 当作一行重新解析（递归一层）。
            var glueIdx = FindGluedKeyStart(value);
            if (glueIdx > 0)
            {
                var head = value.Substring(0, glueIdx).TrimEnd();
                var tail = value.Substring(glueIdx);
                values[key] = head;
                diagnostics?.Invoke(
                    $"配置文件 {configPath} 行 {lineNumber} 检测到粘连：将 \"{tail}\" 当作独立行解析" +
                    "（上一行可能缺少结尾换行符）");
                ParseLine(tail, values, diagnostics, configPath, lineNumber);
                return;
            }

            values[key] = value;
        }

        private static string NormalizeLine(string s)
        {
            // BOM (U+FEFF) + 零宽空格 (U+200B / U+200C / U+200D) → 删除
            // 全角等号 (U+FF1D) → ASCII '='
            // 仅在出现这些字符时才分配新字符串，多数行直接 return 原串
            var hasJunk = false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\uFEFF' || c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFF1D')
                {
                    hasJunk = true;
                    break;
                }
            }
            if (!hasJunk) return s;

            var chars = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\uFEFF' || c == '\u200B' || c == '\u200C' || c == '\u200D')
                {
                    continue;
                }
                if (c == '\uFF1D')
                {
                    chars.Append('=');
                    continue;
                }
                chars.Append(c);
            }
            return chars.ToString();
        }

        private static int FindInlineCommentStart(string value)
        {
            // 仅当 ;/# 前是空白，才视作行内注释，避免砍 URL 里的 ;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if ((c == ';' || c == '#') && i > 0 && char.IsWhiteSpace(value[i - 1]))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 在 value 中查找"粘连进来的下一个 key=value"起点。
        ///
        /// 启发式（**保守**：宁可漏判也不误切 URL / 文件路径）：
        /// <list type="bullet">
        /// <item>位置 i 必须是字母 / 下划线（合法 INI 标识符首字符）</item>
        /// <item><c>value[0..i]</c> 必须**整段**只由 <c>[A-Za-z0-9_]</c> 组成 ——
        ///       即原 value 是一个纯 token（数字、标识符、十六进制等）。
        ///       这一条直接把 URL（含 <c>/ : ? .</c>）、Windows 路径（含 <c>\ :</c>）、
        ///       带空格的字面量从粘连判定中剔除</item>
        /// <item>从 i 开始连续 <c>[A-Za-z0-9_]</c> 直到第一个 <c>=</c>，且非空</item>
        /// <item><c>=</c> 后面非空</item>
        /// </list>
        /// 不命中返回 -1，命中返回切分点（即新 key 的首字符索引）。
        /// </summary>
        private static int FindGluedKeyStart(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (!IsKeyStart(c)) continue;

                // 粘连前提：i 前面整段都是合法的 key/value token 字符（数字 / 字母 / 下划线）。
                // 一旦出现空白 / '/' / ':' / '.' / '\\' / '?' 等，就肯定不是粘连，
                // 而是 URL / 文件路径 / 句子的一部分。
                if (!IsAllKeyCharsInPrefix(value, i)) continue;

                // 从 i 开始向后扫到 '='
                var j = i + 1;
                while (j < value.Length && IsKeyChar(value[j])) j++;
                if (j >= value.Length) return -1;
                if (value[j] != '=') continue;
                if (j == i) continue; // 没字符
                if (j + 1 >= value.Length) continue; // '=' 后没东西

                return i;
            }
            return -1;
        }

        private static bool IsAllKeyCharsInPrefix(string value, int endExclusive)
        {
            for (var k = 0; k < endExclusive; k++)
            {
                if (!IsKeyChar(value[k])) return false;
            }
            return true;
        }

        private static bool IsKeyStart(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
        }

        private static bool IsKeyChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
        }
    }
}
