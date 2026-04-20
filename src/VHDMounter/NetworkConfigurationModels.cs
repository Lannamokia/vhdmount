using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace VHDMounter
{
    internal enum NetworkConfigurationMode
    {
        Unknown = 0,
        Dhcp,
        StaticIpv4,
    }

    internal enum NetworkEditorViewStatus
    {
        Editing = 0,
        Applying,
        Applied,
        Error,
    }

    internal sealed class NetworkIpv4Configuration
    {
        public NetworkIpv4Configuration(bool isDhcp, string ipAddress, string subnetMask, string gateway, string primaryDns, string secondaryDns)
        {
            IsDhcp = isDhcp;
            IpAddress = ipAddress ?? string.Empty;
            SubnetMask = subnetMask ?? string.Empty;
            Gateway = gateway ?? string.Empty;
            PrimaryDns = primaryDns ?? string.Empty;
            SecondaryDns = secondaryDns ?? string.Empty;
        }

        public bool IsDhcp { get; }

        public string IpAddress { get; }

        public string SubnetMask { get; }

        public string Gateway { get; }

        public string PrimaryDns { get; }

        public string SecondaryDns { get; }

        public NetworkConfigurationMode Mode => IsDhcp ? NetworkConfigurationMode.Dhcp : NetworkConfigurationMode.StaticIpv4;

        public string ModeText => Mode == NetworkConfigurationMode.Dhcp ? "DHCP" : "静态";

        public IReadOnlyList<string> GetFieldValues()
        {
            return new[]
            {
                IpAddress,
                SubnetMask,
                Gateway,
                PrimaryDns,
                SecondaryDns,
            };
        }
    }

    internal sealed class NetworkAdapterInfo
    {
        public NetworkAdapterInfo(string interfaceId, string displayName, string description, NetworkIpv4Configuration currentConfiguration)
        {
            InterfaceId = interfaceId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            CurrentConfiguration = currentConfiguration ?? new NetworkIpv4Configuration(true, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        public string InterfaceId { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public NetworkIpv4Configuration CurrentConfiguration { get; }

        public string CurrentModeText => CurrentConfiguration.ModeText;

        public string CurrentIpv4Text => string.IsNullOrWhiteSpace(CurrentConfiguration.IpAddress) ? "未配置" : CurrentConfiguration.IpAddress;

        public string DetailText => $"已连接 | {CurrentModeText} | {Description}";

        public NetworkAdapterInfo WithConfiguration(NetworkIpv4Configuration configuration)
        {
            return new NetworkAdapterInfo(InterfaceId, DisplayName, Description, configuration);
        }
    }

    internal sealed class NetworkConfigurationApplyResult
    {
        public NetworkConfigurationApplyResult(bool success, string message, NetworkIpv4Configuration effectiveConfiguration = null)
        {
            Success = success;
            Message = message ?? string.Empty;
            EffectiveConfiguration = effectiveConfiguration;
        }

        public bool Success { get; }

        public string Message { get; }

        public NetworkIpv4Configuration EffectiveConfiguration { get; }
    }

    internal sealed class NetworkEditorSegmentDisplay
    {
        public NetworkEditorSegmentDisplay(string text, bool isActive, bool isFieldActive, string separatorText)
        {
            Text = string.IsNullOrWhiteSpace(text) ? "___" : text.PadLeft(3, '0');
            IsActive = isActive;
            IsFieldActive = isFieldActive;
            SeparatorText = separatorText ?? string.Empty;
        }

        public string Text { get; }

        public bool IsActive { get; }

        public bool IsFieldActive { get; }

        public string SeparatorText { get; }
    }

    internal sealed class NetworkEditorFieldDisplay
    {
        public NetworkEditorFieldDisplay(string label, bool isActive, IReadOnlyList<NetworkEditorSegmentDisplay> segments)
        {
            Label = label ?? string.Empty;
            IsActive = isActive;
            Segments = segments ?? Array.Empty<NetworkEditorSegmentDisplay>();
        }

        public string Label { get; }

        public bool IsActive { get; }

        public IReadOnlyList<NetworkEditorSegmentDisplay> Segments { get; }
    }

    internal sealed class NetworkIpv4EditorState
    {
        private static readonly string[] FieldLabels =
        {
            "IP 地址",
            "子网掩码",
            "网关",
            "主要 IPv4 DNS",
            "备用 IPv4 DNS",
        };

        private string[] baselineSegments;
        private string[] currentSegments;

        public NetworkIpv4EditorState(NetworkIpv4Configuration configuration)
        {
            LoadConfiguration(configuration);
        }

        public int ActiveSegmentIndex { get; private set; }

        public bool IsAwaitingOverwrite { get; private set; }

        public bool HasUnsavedChanges => !baselineSegments.SequenceEqual(currentSegments, StringComparer.Ordinal);

        public void LoadConfiguration(NetworkIpv4Configuration configuration)
        {
            baselineSegments = BuildSegmentPool(configuration);
            currentSegments = (string[])baselineSegments.Clone();
            ActiveSegmentIndex = 0;
            IsAwaitingOverwrite = true;
        }

        public void AdvanceSegment()
        {
            ActiveSegmentIndex = (ActiveSegmentIndex + 1) % currentSegments.Length;
            IsAwaitingOverwrite = true;
        }

        public string GetActiveSegmentDescriptor()
        {
            var fieldIndex = ActiveSegmentIndex / 4;
            var segmentIndex = (ActiveSegmentIndex % 4) + 1;
            return $"当前输入段：{FieldLabels[fieldIndex]} 第 {segmentIndex} 段";
        }

        public IReadOnlyList<NetworkEditorFieldDisplay> BuildFieldDisplays()
        {
            var fields = new List<NetworkEditorFieldDisplay>(FieldLabels.Length);
            for (var fieldIndex = 0; fieldIndex < FieldLabels.Length; fieldIndex++)
            {
                var fieldActive = fieldIndex == ActiveSegmentIndex / 4;
                var segments = new List<NetworkEditorSegmentDisplay>(4);
                for (var segmentOffset = 0; segmentOffset < 4; segmentOffset++)
                {
                    var absoluteIndex = (fieldIndex * 4) + segmentOffset;
                    segments.Add(new NetworkEditorSegmentDisplay(
                        currentSegments[absoluteIndex],
                        absoluteIndex == ActiveSegmentIndex,
                        fieldActive,
                        segmentOffset < 3 ? "." : string.Empty));
                }

                fields.Add(new NetworkEditorFieldDisplay(FieldLabels[fieldIndex], fieldActive, segments));
            }

            return fields;
        }

        public bool TryEnterDigit(int digit, out string error)
        {
            error = string.Empty;
            if (digit < 0 || digit > 9)
            {
                error = "只允许输入 0 到 9。";
                return false;
            }

            var digitText = digit.ToString(CultureInfo.InvariantCulture);
            var currentText = currentSegments[ActiveSegmentIndex] ?? string.Empty;
            var candidateText = IsAwaitingOverwrite ? digitText : currentText + digitText;

            if (candidateText.Length > 3)
            {
                error = "当前输入段最多只能输入 3 位数字。";
                return false;
            }

            if (!int.TryParse(candidateText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 255)
            {
                error = "当前输入段只能输入 0 到 255。";
                return false;
            }

            currentSegments[ActiveSegmentIndex] = candidateText;
            IsAwaitingOverwrite = false;
            return true;
        }

        public bool TryBuildConfiguration(out NetworkIpv4Configuration configuration, out string error)
        {
            configuration = null;

            if (!TryBuildFieldValue(0, allowBlank: false, out var ipAddress, out error))
            {
                return false;
            }

            if (!TryBuildFieldValue(1, allowBlank: false, out var subnetMask, out error))
            {
                return false;
            }

            if (!IsValidSubnetMask(subnetMask))
            {
                error = "子网掩码必须是连续的 IPv4 掩码。";
                return false;
            }

            if (!TryBuildFieldValue(2, allowBlank: true, out var gateway, out error))
            {
                return false;
            }

            if (!TryBuildFieldValue(3, allowBlank: true, out var primaryDns, out error))
            {
                return false;
            }

            if (!TryBuildFieldValue(4, allowBlank: true, out var secondaryDns, out error))
            {
                return false;
            }

            configuration = new NetworkIpv4Configuration(
                false,
                ipAddress,
                subnetMask,
                gateway,
                primaryDns,
                secondaryDns);

            error = string.Empty;
            return true;
        }

        private bool TryBuildFieldValue(int fieldIndex, bool allowBlank, out string value, out string error)
        {
            var segments = currentSegments.Skip(fieldIndex * 4).Take(4).ToArray();
            var populatedSegments = segments.Count(segment => !string.IsNullOrWhiteSpace(segment));

            if (populatedSegments == 0)
            {
                if (!allowBlank)
                {
                    value = string.Empty;
                    error = $"{FieldLabels[fieldIndex]} 不能为空。";
                    return false;
                }

                value = string.Empty;
                error = string.Empty;
                return true;
            }

            if (populatedSegments != 4)
            {
                value = string.Empty;
                error = $"{FieldLabels[fieldIndex]} 的 4 个输入段必须全部填写。";
                return false;
            }

            var normalizedSegments = new string[4];
            for (var index = 0; index < 4; index++)
            {
                if (!int.TryParse(segments[index], NumberStyles.None, CultureInfo.InvariantCulture, out var octet) || octet < 0 || octet > 255)
                {
                    value = string.Empty;
                    error = $"{FieldLabels[fieldIndex]} 的第 {index + 1} 段必须在 0 到 255 之间。";
                    return false;
                }

                normalizedSegments[index] = octet.ToString(CultureInfo.InvariantCulture);
            }

            value = string.Join(".", normalizedSegments);
            if (!IPAddress.TryParse(value, out var ipAddress) || ipAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                error = $"{FieldLabels[fieldIndex]} 不是合法的 IPv4 地址。";
                value = string.Empty;
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static string[] BuildSegmentPool(NetworkIpv4Configuration configuration)
        {
            configuration ??= new NetworkIpv4Configuration(true, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            var fieldValues = configuration.GetFieldValues();
            var segments = new List<string>(20);
            foreach (var fieldValue in fieldValues)
            {
                var split = string.IsNullOrWhiteSpace(fieldValue)
                    ? new[] { string.Empty, string.Empty, string.Empty, string.Empty }
                    : fieldValue.Split('.').Take(4).Concat(Enumerable.Repeat(string.Empty, 4)).Take(4).ToArray();
                segments.AddRange(split.Select(value => value ?? string.Empty));
            }

            return segments.ToArray();
        }

        private static bool IsValidSubnetMask(string subnetMask)
        {
            if (string.IsNullOrWhiteSpace(subnetMask) || !IPAddress.TryParse(subnetMask, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            var value = ((uint)bytes[0] << 24) |
                        ((uint)bytes[1] << 16) |
                        ((uint)bytes[2] << 8) |
                        bytes[3];

            var inverted = ~value;
            return (inverted & (inverted + 1)) == 0;
        }
    }

    internal sealed class TimedPressSequenceTracker
    {
        private readonly TimeSpan interval;
        private int consecutiveCount;
        private DateTime lastPressUtc = DateTime.MinValue;

        public TimedPressSequenceTracker(TimeSpan interval)
        {
            this.interval = interval;
        }

        public int RegisterPress(DateTime utcNow)
        {
            if (lastPressUtc != DateTime.MinValue && utcNow - lastPressUtc <= interval)
            {
                consecutiveCount++;
            }
            else
            {
                consecutiveCount = 1;
            }

            lastPressUtc = utcNow;
            return consecutiveCount;
        }

        public void Reset()
        {
            consecutiveCount = 0;
            lastPressUtc = DateTime.MinValue;
        }
    }
}