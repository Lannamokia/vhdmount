using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VHDMounter;
using VHDMounter.RustDeskBridge.Config;
using Xunit;

namespace VHDMounter.Tests
{
    /// <summary>
    /// IniLineParser 回归测试：人工编辑 vhdmonter_config.ini 时常见的几种 footgun。
    ///
    /// 历史 bug：在没有结尾换行符的旧模板末尾追加 <c>EnableRustDeskBridge=true</c> 时，
    /// 简单编辑器会把它和上一行的 value 粘成一行，<see cref="File.ReadAllLines"/>
    /// 把整段当一行，新键彻底没进 dict → BridgeServerHost 跳过启动。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("feature", "machine-log-bootstrap")]
    public sealed class IniLineParserTests : IDisposable
    {
        private readonly string _tempDir;

        public IniLineParserTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ini-parser-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { /* ignore */ }
        }

        private string WriteIni(string content, Encoding encoding = null)
        {
            var path = Path.Combine(_tempDir, $"config-{Guid.NewGuid():N}.ini");
            File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
            return path;
        }

        private static IDictionary<string, string> Parse(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IniLineParser.ParseInto(path, values, _ => { });
            return values;
        }

        // ---------- 基线：标准 INI ----------

        [Fact]
        public void StandardKvLines_Parsed()
        {
            var path = WriteIni(
                "[Settings]\n" +
                "ServerBaseUrl=http://example/\n" +
                "MachineId=MACHINE_001\n" +
                "EnableRustDeskBridge=true\n");
            var values = Parse(path);
            Assert.Equal("http://example/", values["ServerBaseUrl"]);
            Assert.Equal("MACHINE_001", values["MachineId"]);
            Assert.Equal("true", values["EnableRustDeskBridge"]);
        }

        [Fact]
        public void CommentsAndSectionHeaders_Skipped()
        {
            var path = WriteIni(
                "; 这是注释\n" +
                "# 兼容 # 注释\n" +
                "[Settings]\n" +
                "Key=Value\n");
            var values = Parse(path);
            Assert.Single(values);
            Assert.Equal("Value", values["Key"]);
        }

        [Fact]
        public void KeyComparison_IsCaseInsensitive()
        {
            var path = WriteIni("ENABLERustDeskBRIDGE=true\n");
            var values = Parse(path);
            Assert.Equal("true", values["enablerustdeskbridge"]);
        }

        // ---------- footgun 1：上一行没换行被粘连 ----------

        [Fact]
        public void GluedKeyWithoutNewline_IsSplit_AndNewKeyParsed()
        {
            // 真实再现：旧模板最后一行没有 LF，用户手工追加新键时编辑器把它粘成一行
            var path = WriteIni(
                "MachineLogUploadMaxSpoolBytes=52428800EnableRustDeskBridge=true\n");
            var values = Parse(path);
            Assert.Equal("52428800", values["MachineLogUploadMaxSpoolBytes"]);
            Assert.Equal("true", values["EnableRustDeskBridge"]);
        }

        [Fact]
        public void GluedKey_MultipleLevels_AreAllRecovered()
        {
            // 极端情况：连续两次粘连
            var path = WriteIni(
                "A=1B=2C=3\n");
            var values = Parse(path);
            Assert.Equal("1", values["A"]);
            Assert.Equal("2", values["B"]);
            Assert.Equal("3", values["C"]);
        }

        [Fact]
        public void GluedKey_DoesNotMatchInsideQuotedOrAlphanumValue()
        {
            // 反例：value 里就是 "abc123def"，不能被误切
            var path = WriteIni(
                "Token=abc123def\n" +
                "Path=C:/Program Files/App=v2\n"); // value 里嵌 '=' 但前面是空格 → 不切
            var values = Parse(path);
            Assert.Equal("abc123def", values["Token"]);
            // "Path" 的 value 整体保留（"=" 后无字母开头的 token-like 起点紧贴非字母数字）
            Assert.Equal("C:/Program Files/App=v2", values["Path"]);
        }

        // ---------- footgun 2：行内 ; / # 注释被吞进 value ----------

        [Fact]
        public void InlineSemicolonComment_StrippedFromValue()
        {
            var path = WriteIni("EnableRustDeskBridge=true ; 启用桥\n");
            var values = Parse(path);
            Assert.Equal("true", values["EnableRustDeskBridge"]);
        }

        [Fact]
        public void InlineHashComment_StrippedFromValue()
        {
            var path = WriteIni("MaxRetries=10 # 上限\n");
            var values = Parse(path);
            Assert.Equal("10", values["MaxRetries"]);
        }

        [Fact]
        public void InlineCommentMarker_WithoutPrecedingWhitespace_NotStripped()
        {
            // 防御：URL 的 query string 里有 '#' / ';' 不能被砍
            var path = WriteIni("ServerBaseUrl=http://example/path?a=1;b=2\n");
            var values = Parse(path);
            Assert.Equal("http://example/path?a=1;b=2", values["ServerBaseUrl"]);
        }

        // ---------- footgun 3：BOM / 零宽空格 / 全角等号 ----------

        [Fact]
        public void Utf8Bom_AtFileStart_DoesNotBreakFirstLine()
        {
            var path = WriteIni(
                "EnableRustDeskBridge=true\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var values = Parse(path);
            Assert.Equal("true", values["EnableRustDeskBridge"]);
        }

        [Fact]
        public void ZeroWidthSpace_InsideKey_IsIgnored()
        {
            // U+200B 在某些编辑器粘贴 / 浏览器复制的时候会偷偷塞进文本
            var content = "Enable\u200BRustDeskBridge=true\n";
            var path = WriteIni(content);
            var values = Parse(path);
            Assert.Equal("true", values["EnableRustDeskBridge"]);
        }

        [Fact]
        public void FullWidthEqualsSign_NormalizedToAscii()
        {
            // 中文输入法常见误触 → 全角 ＝
            var content = "EnableRustDeskBridge\uFF1Dtrue\n";
            var path = WriteIni(content);
            var values = Parse(path);
            Assert.Equal("true", values["EnableRustDeskBridge"]);
        }

        // ---------- footgun 4：value 前后空白 ----------

        [Fact]
        public void LeadingTrailingWhitespace_OnValue_IsTrimmed()
        {
            var path = WriteIni("Key=   spaced   \n");
            var values = Parse(path);
            Assert.Equal("spaced", values["Key"]);
        }

        // ---------- 集成：BridgeConfig.Load + 真实粘连场景 ----------

        [Fact]
        public void BridgeConfig_Load_RecoversGluedEnableRustDeskBridge()
        {
            // 这就是用户报告的字面场景：旧模板末尾没 LF，用户追加 EnableRustDeskBridge=true
            var path = WriteIni(
                "[Settings]\n" +
                "ServerBaseUrl=http://127.0.0.1:8080\n" +
                "MachineId=MACHINE_001\n" +
                "MachineLogUploadMaxSpoolBytes=52428800EnableRustDeskBridge=true\n");
            var diagnosed = new List<string>();
            var cfg = BridgeConfig.Load(path, msg => diagnosed.Add(msg));
            Assert.True(cfg.EnableRustDeskBridge,
                "BridgeConfig.Load 必须能从粘连行中识别 EnableRustDeskBridge=true，否则 BridgeServerHost 不会启动。" +
                " 诊断输出：" + string.Join(" | ", diagnosed));
            // 同时确保被粘住的前一项也被正确还原
            // (BridgeConfig 不暴露 MachineLogUploadMaxSpoolBytes，跳过那一项的断言；
            //  IniLineParserTests 上面已覆盖。)
        }

        [Fact]
        public void BridgeConfig_Load_HonorsExplicitFalse()
        {
            // 防回归：parser 改宽容了，但显式 false 必须仍是 false
            var path = WriteIni(
                "[Settings]\n" +
                "EnableRustDeskBridge=false\n");
            var cfg = BridgeConfig.Load(path, _ => { });
            Assert.False(cfg.EnableRustDeskBridge);
        }

        [Fact]
        public void BridgeConfig_Load_LastWriteWins()
        {
            // 后写覆盖前写（运维"在末尾追加 override"约定，需保持）
            var path = WriteIni(
                "EnableRustDeskBridge=false\n" +
                "EnableRustDeskBridge=true\n");
            var cfg = BridgeConfig.Load(path, _ => { });
            Assert.True(cfg.EnableRustDeskBridge);
        }
    }
}
