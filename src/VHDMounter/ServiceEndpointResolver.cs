using System;
using System.Collections.Generic;

namespace VHDMounter
{
    internal static class ServiceEndpointResolver
    {
        public const string ServerBaseUrlKey = "ServerBaseUrl";
        public const string DefaultServerBaseUrl = "http://127.0.0.1:8080";

        private const string BootImageSelectPath = "api/boot-image-select";
        private const string EvhdEnvelopePath = "api/evhd-envelope";
        private const string ProtectionCheckPath = "api/protect";
        private const string MachineLogWebSocketPath = "ws/machine-log";

        public static string ResolveServerBaseUrl(IReadOnlyDictionary<string, string> config, string fallback = DefaultServerBaseUrl)
        {
            if (TryGetAbsoluteUrl(config, ServerBaseUrlKey, out var configuredBaseUrl))
            {
                return NormalizeBaseUrl(configuredBaseUrl);
            }

            if (TryGetAbsoluteUrl(config, "BootImageSelectUrl", out var bootImageUrl))
            {
                return ExtractBaseUrl(bootImageUrl);
            }

            if (TryGetAbsoluteUrl(config, "EvhdEnvelopeUrl", out var envelopeUrl))
            {
                return ExtractBaseUrl(envelopeUrl);
            }

            if (TryGetAbsoluteUrl(config, "EvhdPasswordUrl", out var legacyPasswordUrl))
            {
                return ExtractBaseUrl(legacyPasswordUrl);
            }

            if (TryGetAbsoluteUrl(config, "ProtectionCheckUrl", out var protectionUrl))
            {
                return ExtractBaseUrl(protectionUrl);
            }

            var legacyMachineLogBaseUrl = TryBuildLegacyMachineLogBaseUrl(config);
            if (!string.IsNullOrWhiteSpace(legacyMachineLogBaseUrl))
            {
                return legacyMachineLogBaseUrl;
            }

            return NormalizeBaseUrl(fallback);
        }

        public static string ResolveBootImageSelectUrl(IReadOnlyDictionary<string, string> config)
        {
            return CombineHttpEndpoint(ResolveServerBaseUrl(config), BootImageSelectPath);
        }

        public static string ResolveEvhdEnvelopeUrl(IReadOnlyDictionary<string, string> config)
        {
            return CombineHttpEndpoint(ResolveServerBaseUrl(config), EvhdEnvelopePath);
        }

        public static string ResolveProtectionCheckUrl(IReadOnlyDictionary<string, string> config)
        {
            return CombineHttpEndpoint(ResolveServerBaseUrl(config), ProtectionCheckPath);
        }

        public static Uri ResolveMachineLogWebSocketUri(IReadOnlyDictionary<string, string> config)
        {
            return BuildWebSocketEndpoint(ResolveServerBaseUrl(config), MachineLogWebSocketPath);
        }

        public static string NormalizeBaseUrl(string rawUrl)
        {
            if (!Uri.TryCreate(rawUrl?.Trim(), UriKind.Absolute, out var uri))
            {
                return DefaultServerBaseUrl;
            }

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty,
            };

            builder.Path = NormalizeBasePath(builder.Path);
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        public static string ExtractBaseUrl(string endpointUrl)
        {
            if (!Uri.TryCreate(endpointUrl?.Trim(), UriKind.Absolute, out var uri))
            {
                return DefaultServerBaseUrl;
            }

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty,
            };

            builder.Path = NormalizeBasePath(builder.Path);
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        public static string CombineHttpEndpoint(string baseUrl, string relativePath)
        {
            var baseUri = new Uri(AppendTrailingSlash(NormalizeBaseUrl(baseUrl)), UriKind.Absolute);
            var endpointUri = new Uri(baseUri, relativePath.TrimStart('/'));
            return endpointUri.AbsoluteUri;
        }

        public static Uri BuildWebSocketEndpoint(string baseUrl, string relativePath)
        {
            var httpEndpoint = new Uri(CombineHttpEndpoint(baseUrl, relativePath), UriKind.Absolute);
            var builder = new UriBuilder(httpEndpoint)
            {
                Scheme = string.Equals(httpEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    ? "wss"
                    : "ws",
                Query = string.Empty,
                Fragment = string.Empty,
            };

            if (httpEndpoint.IsDefaultPort)
            {
                builder.Port = -1;
            }

            return builder.Uri;
        }

        private static bool TryGetAbsoluteUrl(IReadOnlyDictionary<string, string> config, string key, out string url)
        {
            url = string.Empty;
            if (config == null || !config.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            if (!Uri.TryCreate(rawValue.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            url = uri.AbsoluteUri;
            return true;
        }

        private static string TryBuildLegacyMachineLogBaseUrl(IReadOnlyDictionary<string, string> config)
        {
            if (config == null || !config.TryGetValue("MachineLogServerIp", out var hostOrUrl) || string.IsNullOrWhiteSpace(hostOrUrl))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(hostOrUrl.Trim(), UriKind.Absolute, out var configuredUri))
            {
                return NormalizeBaseUrl(configuredUri.AbsoluteUri);
            }

            var port = 8080;
            if (config.TryGetValue("MachineLogServerPort", out var rawPort) && int.TryParse(rawPort, out var parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }

            return NormalizeBaseUrl($"http://{hostOrUrl.Trim()}:{port}");
        }

        private static string NormalizeBasePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || absolutePath == "/")
            {
                return "/";
            }

            var apiIndex = absolutePath.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (apiIndex >= 0)
            {
                absolutePath = absolutePath.Substring(0, apiIndex);
            }

            return absolutePath.TrimEnd('/') + "/";
        }

        private static string AppendTrailingSlash(string value)
        {
            return string.IsNullOrEmpty(value) || value.EndsWith("/", StringComparison.Ordinal)
                ? value
                : value + "/";
        }
    }
}