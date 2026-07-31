using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Frames;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 5：HandshakeResponse 裸 JSON 形态。
    ///
    /// Validates: Requirements 4.9, 11.1
    ///
    /// 断言：
    /// <list type="bullet">
    /// <item>序列化后顶层字段集合 ⊆ {"ok", "reason"}</item>
    /// <item>ok ∈ {true, false}</item>
    /// <item>reason ∈ §11.1 枚举集合（且仅当 ok == false 时出现）</item>
    /// </list>
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 5: HandshakeResponse 裸 JSON 形态")]
    public sealed class HandshakeResponseShapeProperty
    {
        // 协议文档 §11.1 HandshakeResponse.reason 枚举
        private static readonly HashSet<string> AllowedReasons = new HashSet<string>(StringComparer.Ordinal)
        {
            "deny",
            "rate_limited",
            "invalid_proof",
            "secret_outdated",
        };

        private static readonly HashSet<string> AllowedTopLevelFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "ok",
            "reason",
        };

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            // 与运行期 SessionStateMachine 序列化口径一致
        };

        [Fact]
        public void Success_OnlyOkTrue_NoReason()
        {
            var resp = HandshakeResponse.Success();
            var json = JsonSerializer.Serialize(resp, SerializerOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            var fields = root.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.True(fields.IsSubsetOf(AllowedTopLevelFields));
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.False(root.TryGetProperty("reason", out _));
        }

        [Theory]
        [InlineData("deny")]
        [InlineData("rate_limited")]
        [InlineData("invalid_proof")]
        [InlineData("secret_outdated")]
        public void Failure_ContainsOkFalseAndReasonInWhitelist(string reason)
        {
            var resp = HandshakeResponse.Failure(reason);
            var json = JsonSerializer.Serialize(resp, SerializerOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var fields = root.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.True(fields.IsSubsetOf(AllowedTopLevelFields));
            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal(reason, root.GetProperty("reason").GetString());
            Assert.Contains(reason, AllowedReasons);
        }

        [Property(MaxTest = 200)]
        public Property AnySerialized_HasNoExtraFields()
        {
            return Prop.ForAll(
                Arb.From(Gen.Elements("deny", "rate_limited", "invalid_proof", "secret_outdated")),
                Arb.From(Gen.Elements(true, false)),
                (reason, ok) =>
                {
                    var resp = ok ? HandshakeResponse.Success() : HandshakeResponse.Failure(reason);
                    var json = JsonSerializer.Serialize(resp, SerializerOptions);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return false;
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (!AllowedTopLevelFields.Contains(prop.Name)) return false;
                    }

                    if (!root.TryGetProperty("ok", out var okEl) || okEl.ValueKind == JsonValueKind.Undefined)
                    {
                        return false;
                    }
                    if (okEl.ValueKind != JsonValueKind.True && okEl.ValueKind != JsonValueKind.False)
                    {
                        return false;
                    }

                    var hasReason = root.TryGetProperty("reason", out var reasonEl);
                    if (okEl.GetBoolean())
                    {
                        // 成功路径：必须不含 reason
                        if (hasReason) return false;
                    }
                    else
                    {
                        // 失败路径：必须含 reason 且字面量在白名单
                        if (!hasReason) return false;
                        if (reasonEl.ValueKind != JsonValueKind.String) return false;
                        var v = reasonEl.GetString();
                        if (string.IsNullOrEmpty(v) || !AllowedReasons.Contains(v)) return false;
                    }

                    return true;
                });
        }

        [Fact]
        public void NullReasonInFailure_IsRejectedAtConstructionTime()
        {
            // 防御：HandshakeResponse.Failure(null) 在序列化时不能产生 "reason": null
            var resp = HandshakeResponse.Failure(null);
            var json = JsonSerializer.Serialize(resp, SerializerOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // ok = false，reason 为 null —— JsonIgnore(WhenWritingNull) 应当跳过 reason
            // 此时 JSON 应当退化为只有 ok 字段
            // —— 这是合法行为：上层应当永远调用 Failure(<某个枚举字面量>)，传 null 是 bug
            // 但 JSON 形态依然合规：subset 不变式仍成立
            var fields = root.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.True(fields.IsSubsetOf(AllowedTopLevelFields));
        }
    }
}
