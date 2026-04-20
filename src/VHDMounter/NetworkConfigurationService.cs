using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace VHDMounter
{
    internal sealed class NetworkConfigurationService
    {
        public Task<IReadOnlyList<NetworkAdapterInfo>> GetConnectedAdaptersAsync()
        {
            return Task.Run<IReadOnlyList<NetworkAdapterInfo>>(GetConnectedAdapters);
        }

        public Task<NetworkAdapterInfo> GetAdapterAsync(string interfaceId)
        {
            return Task.Run(() => GetAdapter(interfaceId));
        }

        public Task<NetworkConfigurationApplyResult> ApplyDhcpAsync(string interfaceId)
        {
            return Task.Run(() => ApplyDhcp(interfaceId));
        }

        public Task<NetworkConfigurationApplyResult> ApplyStaticConfigurationAsync(string interfaceId, NetworkIpv4Configuration configuration)
        {
            return Task.Run(() => ApplyStaticConfiguration(interfaceId, configuration));
        }

        private static IReadOnlyList<NetworkAdapterInfo> GetConnectedAdapters()
        {
            var wmiConfigurations = LoadWmiConfigurations();
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsCandidateAdapter)
                .Select(networkInterface => BuildAdapterInfo(networkInterface, wmiConfigurations))
                .Where(adapter => adapter != null)
                .OrderBy(adapter => adapter.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return adapters;
        }

        private static NetworkAdapterInfo GetAdapter(string interfaceId)
        {
            if (string.IsNullOrWhiteSpace(interfaceId))
            {
                return null;
            }

            var wmiConfigurations = LoadWmiConfigurations();
            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(candidate => string.Equals(candidate.Id, interfaceId, StringComparison.OrdinalIgnoreCase));

            return networkInterface == null ? null : BuildAdapterInfo(networkInterface, wmiConfigurations);
        }

        private static NetworkConfigurationApplyResult ApplyDhcp(string interfaceId)
        {
            try
            {
                using var adapter = OpenConfigurationObject(interfaceId);
                if (adapter == null)
                {
                    return new NetworkConfigurationApplyResult(false, "未找到可配置的目标网卡。", null);
                }

                var dhcpResult = InvokeMethod(adapter, "EnableDHCP", null, "切换 DHCP");
                if (!dhcpResult.Success)
                {
                    return dhcpResult;
                }

                var gatewayResult = InvokeMethod(
                    adapter,
                    "SetGateways",
                    parameters =>
                    {
                        parameters["DefaultIPGateway"] = null;
                        parameters["GatewayCostMetric"] = null;
                    },
                    "清除网关");
                if (!gatewayResult.Success)
                {
                    return gatewayResult;
                }

                var dnsResult = InvokeMethod(
                    adapter,
                    "SetDNSServerSearchOrder",
                    parameters => parameters["DNSServerSearchOrder"] = null,
                    "恢复自动 DNS");
                if (!dnsResult.Success)
                {
                    return dnsResult;
                }

                var refreshedAdapter = GetAdapter(interfaceId);
                return new NetworkConfigurationApplyResult(true, "已切换为 DHCP。", refreshedAdapter?.CurrentConfiguration);
            }
            catch (Exception ex)
            {
                return new NetworkConfigurationApplyResult(false, $"切换 DHCP 失败：{ex.Message}", null);
            }
        }

        private static NetworkConfigurationApplyResult ApplyStaticConfiguration(string interfaceId, NetworkIpv4Configuration configuration)
        {
            if (configuration == null)
            {
                return new NetworkConfigurationApplyResult(false, "缺少要应用的静态 IPv4 配置。", null);
            }

            try
            {
                using var adapter = OpenConfigurationObject(interfaceId);
                if (adapter == null)
                {
                    return new NetworkConfigurationApplyResult(false, "未找到可配置的目标网卡。", null);
                }

                var enableStaticResult = InvokeMethod(
                    adapter,
                    "EnableStatic",
                    parameters =>
                    {
                        parameters["IPAddress"] = new[] { configuration.IpAddress };
                        parameters["SubnetMask"] = new[] { configuration.SubnetMask };
                    },
                    "应用静态 IPv4");
                if (!enableStaticResult.Success)
                {
                    return enableStaticResult;
                }

                var gatewayResult = InvokeMethod(
                    adapter,
                    "SetGateways",
                    parameters =>
                    {
                        if (string.IsNullOrWhiteSpace(configuration.Gateway))
                        {
                            parameters["DefaultIPGateway"] = null;
                            parameters["GatewayCostMetric"] = null;
                        }
                        else
                        {
                            parameters["DefaultIPGateway"] = new[] { configuration.Gateway };
                            parameters["GatewayCostMetric"] = new ushort[] { 1 };
                        }
                    },
                    "设置默认网关");
                if (!gatewayResult.Success)
                {
                    return gatewayResult;
                }

                var dnsServers = new[] { configuration.PrimaryDns, configuration.SecondaryDns }
                    .Where(server => !string.IsNullOrWhiteSpace(server))
                    .ToArray();
                var dnsResult = InvokeMethod(
                    adapter,
                    "SetDNSServerSearchOrder",
                    parameters => parameters["DNSServerSearchOrder"] = dnsServers.Length == 0 ? null : dnsServers,
                    "设置 DNS");
                if (!dnsResult.Success)
                {
                    return dnsResult;
                }

                var refreshedAdapter = GetAdapter(interfaceId);
                return new NetworkConfigurationApplyResult(true, "静态 IPv4 配置已应用。", refreshedAdapter?.CurrentConfiguration ?? configuration);
            }
            catch (Exception ex)
            {
                return new NetworkConfigurationApplyResult(false, $"应用静态 IPv4 失败：{ex.Message}", null);
            }
        }

        private static Dictionary<string, WmiNetworkConfiguration> LoadWmiConfigurations()
        {
            var configurations = new Dictionary<string, WmiNetworkConfiguration>(StringComparer.OrdinalIgnoreCase);
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var settingId = item["SettingID"] as string;
                    if (string.IsNullOrWhiteSpace(settingId))
                    {
                        continue;
                    }

                    configurations[settingId] = new WmiNetworkConfiguration(
                        settingId,
                        item["DHCPEnabled"] as bool? ?? false,
                        FilterIpv4Strings(item["IPAddress"] as string[]),
                        FilterIpv4Strings(item["IPSubnet"] as string[]),
                        FilterIpv4Strings(item["DefaultIPGateway"] as string[]),
                        FilterIpv4Strings(item["DNSServerSearchOrder"] as string[]));
                }
            }

            return configurations;
        }

        private static ManagementObject OpenConfigurationObject(string interfaceId)
        {
            if (string.IsNullOrWhiteSpace(interfaceId))
            {
                return null;
            }

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var settingId = item["SettingID"] as string;
                    if (!string.Equals(settingId, interfaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return new ManagementObject(item.Path.Path);
                }
            }

            return null;
        }

        private static NetworkAdapterInfo BuildAdapterInfo(NetworkInterface networkInterface, IReadOnlyDictionary<string, WmiNetworkConfiguration> wmiConfigurations)
        {
            if (networkInterface == null || !wmiConfigurations.TryGetValue(networkInterface.Id, out var configuration))
            {
                return null;
            }

            var ipProperties = networkInterface.GetIPProperties();
            var ipAddress = configuration.IpAddresses.FirstOrDefault() ?? ipProperties.UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address.ToString())
                .FirstOrDefault();
            var subnetMask = configuration.SubnetMasks.FirstOrDefault() ?? ipProperties.UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.IPv4Mask?.ToString())
                .FirstOrDefault(mask => !string.IsNullOrWhiteSpace(mask));
            var gateway = configuration.Gateways.FirstOrDefault() ?? ipProperties.GatewayAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address.ToString())
                .FirstOrDefault();
            var dnsServers = configuration.DnsServers.Any()
                ? configuration.DnsServers.ToArray()
                : ipProperties.DnsAddresses
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.ToString())
                    .ToArray();

            return new NetworkAdapterInfo(
                networkInterface.Id,
                networkInterface.Name,
                networkInterface.Description,
                new NetworkIpv4Configuration(
                    configuration.DhcpEnabled,
                    ipAddress,
                    subnetMask,
                    gateway,
                    dnsServers.ElementAtOrDefault(0),
                    dnsServers.ElementAtOrDefault(1)));
        }

        private static bool IsCandidateAdapter(NetworkInterface networkInterface)
        {
            return networkInterface != null &&
                   networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                   networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                   networkInterface.OperationalStatus == OperationalStatus.Up &&
                   networkInterface.Supports(NetworkInterfaceComponent.IPv4);
        }

        private static string[] FilterIpv4Strings(string[] values)
        {
            return values?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork)
                .ToArray() ?? Array.Empty<string>();
        }

        private static NetworkConfigurationApplyResult InvokeMethod(ManagementObject adapter, string methodName, Action<ManagementBaseObject> parameterSetter, string operationName)
        {
            using var parameters = adapter.GetMethodParameters(methodName);
            parameterSetter?.Invoke(parameters);
            using var result = adapter.InvokeMethod(methodName, parameters, null);
            var returnValue = result?["ReturnValue"] as uint? ?? Convert.ToUInt32(result?["ReturnValue"] ?? 1);

            if (returnValue == 0 || returnValue == 1)
            {
                return new NetworkConfigurationApplyResult(true, returnValue == 1 ? $"{operationName}完成，系统提示可能需要重启。" : string.Empty, null);
            }

            return new NetworkConfigurationApplyResult(false, BuildFailureMessage(operationName, returnValue), null);
        }

        private static string BuildFailureMessage(string operationName, uint returnValue)
        {
            return returnValue switch
            {
                64 => $"{operationName}失败：当前方法在该网卡上不受支持。",
                65 => $"{operationName}失败：网卡当前已在处理其它请求。",
                66 => $"{operationName}失败：参数无效。",
                67 => $"{operationName}失败：目标网卡不存在。",
                68 => $"{operationName}失败：网卡访问权限不足。",
                69 => $"{operationName}失败：静态配置方法与当前状态不兼容。",
                70 => $"{operationName}失败：DHCP 方法与当前状态不兼容。",
                71 => $"{operationName}失败：系统访问注册表时出错。",
                72 => $"{operationName}失败：系统访问网卡驱动时出错。",
                73 => $"{operationName}失败：系统访问 DHCP 服务时出错。",
                74 => $"{operationName}失败：内存不足。",
                75 => $"{operationName}失败：没有可用的 IP 地址。",
                76 => $"{operationName}失败：IP 地址与网卡配置冲突。",
                84 => $"{operationName}失败：IP 地址、掩码或 DNS 参数非法。",
                91 => $"{operationName}失败：系统拒绝写入当前网络参数。",
                _ => $"{operationName}失败：系统返回码 {returnValue}。",
            };
        }

        private sealed class WmiNetworkConfiguration
        {
            public WmiNetworkConfiguration(string settingId, bool dhcpEnabled, IReadOnlyList<string> ipAddresses, IReadOnlyList<string> subnetMasks, IReadOnlyList<string> gateways, IReadOnlyList<string> dnsServers)
            {
                SettingId = settingId;
                DhcpEnabled = dhcpEnabled;
                IpAddresses = ipAddresses ?? Array.Empty<string>();
                SubnetMasks = subnetMasks ?? Array.Empty<string>();
                Gateways = gateways ?? Array.Empty<string>();
                DnsServers = dnsServers ?? Array.Empty<string>();
            }

            public string SettingId { get; }

            public bool DhcpEnabled { get; }

            public IReadOnlyList<string> IpAddresses { get; }

            public IReadOnlyList<string> SubnetMasks { get; }

            public IReadOnlyList<string> Gateways { get; }

            public IReadOnlyList<string> DnsServers { get; }
        }
    }
}