using System;
using System.Collections.Generic;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class ServiceEndpointResolverTests
    {
        [Fact]
        public void ResolveServerBaseUrl_ReturnsConfiguredUrl()
        {
            var config = new Dictionary<string, string>
            {
                { "ServerBaseUrl", "http://192.168.1.100:8080" },
            };

            var result = ServiceEndpointResolver.ResolveServerBaseUrl(config);

            Assert.Equal("http://192.168.1.100:8080", result);
        }

        [Fact]
        public void ResolveServerBaseUrl_FallsBackToBootImageSelectUrl()
        {
            var config = new Dictionary<string, string>
            {
                { "BootImageSelectUrl", "http://192.168.1.100:8080/api/boot-image-select" },
            };

            var result = ServiceEndpointResolver.ResolveServerBaseUrl(config);

            Assert.Equal("http://192.168.1.100:8080", result);
        }

        [Fact]
        public void ResolveServerBaseUrl_FallsBackToEvhdEnvelopeUrl()
        {
            var config = new Dictionary<string, string>
            {
                { "EvhdEnvelopeUrl", "https://server.example.com/api/evhd-envelope" },
            };

            var result = ServiceEndpointResolver.ResolveServerBaseUrl(config);

            Assert.Equal("https://server.example.com", result);
        }

        [Fact]
        public void ResolveServerBaseUrl_FallsBackToLegacyMachineLogServerIp()
        {
            var config = new Dictionary<string, string>
            {
                { "MachineLogServerIp", "192.168.1.50" },
                { "MachineLogServerPort", "9000" },
            };

            var result = ServiceEndpointResolver.ResolveServerBaseUrl(config);

            Assert.Equal("http://192.168.1.50:9000", result);
        }

        [Fact]
        public void ResolveServerBaseUrl_UsesDefaultWhenConfigEmpty()
        {
            var config = new Dictionary<string, string>();

            var result = ServiceEndpointResolver.ResolveServerBaseUrl(config);

            Assert.Equal("http://127.0.0.1:8080", result);
        }

        [Fact]
        public void ResolveServerBaseUrl_UsesFallbackParameter()
        {
            var config = new Dictionary<string, string>();

            var result = ServiceEndpointResolver.ResolveServerBaseUrl(config, "http://fallback.local:3000");

            Assert.Equal("http://fallback.local:3000", result);
        }

        [Fact]
        public void NormalizeBaseUrl_RemovesQueryAndFragment()
        {
            var result = ServiceEndpointResolver.NormalizeBaseUrl("http://server.com:8080/path?query=1#frag");

            Assert.Equal("http://server.com:8080/path", result);
        }

        [Fact]
        public void NormalizeBaseUrl_RemovesTrailingSlash()
        {
            var result = ServiceEndpointResolver.NormalizeBaseUrl("http://server.com:8080/");

            Assert.Equal("http://server.com:8080", result);
        }

        [Fact]
        public void NormalizeBaseUrl_StripsApiPath()
        {
            var result = ServiceEndpointResolver.NormalizeBaseUrl("http://server.com:8080/api/v1/boot-image-select");

            Assert.Equal("http://server.com:8080", result);
        }

        [Fact]
        public void NormalizeBaseUrl_ReturnsDefaultForInvalidUrl()
        {
            var result = ServiceEndpointResolver.NormalizeBaseUrl("not-a-url");

            Assert.Equal("http://127.0.0.1:8080", result);
        }

        [Fact]
        public void NormalizeBaseUrl_ReturnsDefaultForNull()
        {
            var result = ServiceEndpointResolver.NormalizeBaseUrl(null);

            Assert.Equal("http://127.0.0.1:8080", result);
        }

        [Fact]
        public void CombineHttpEndpoint_JoinsBaseAndRelativePath()
        {
            var result = ServiceEndpointResolver.CombineHttpEndpoint("http://server.com:8080", "api/boot-image-select");

            Assert.Equal("http://server.com:8080/api/boot-image-select", result);
        }

        [Fact]
        public void CombineHttpEndpoint_HandlesLeadingSlash()
        {
            var result = ServiceEndpointResolver.CombineHttpEndpoint("http://server.com:8080", "/api/boot-image-select");

            Assert.Equal("http://server.com:8080/api/boot-image-select", result);
        }

        [Fact]
        public void BuildWebSocketEndpoint_ConvertsHttpToWs()
        {
            var result = ServiceEndpointResolver.BuildWebSocketEndpoint("http://server.com:8080", "ws/machine-log");

            Assert.Equal("ws://server.com:8080/ws/machine-log", result.AbsoluteUri);
        }

        [Fact]
        public void BuildWebSocketEndpoint_ConvertsHttpsToWss()
        {
            var result = ServiceEndpointResolver.BuildWebSocketEndpoint("https://server.com:443", "ws/machine-log");

            Assert.Equal("wss://server.com/ws/machine-log", result.AbsoluteUri);
        }

        [Fact]
        public void ResolveBootImageSelectUrl_BuildsCorrectUrl()
        {
            var config = new Dictionary<string, string>
            {
                { "ServerBaseUrl", "http://192.168.1.100:8080" },
            };

            var result = ServiceEndpointResolver.ResolveBootImageSelectUrl(config);

            Assert.Equal("http://192.168.1.100:8080/api/boot-image-select", result);
        }

        [Fact]
        public void ResolveEvhdEnvelopeUrl_BuildsCorrectUrl()
        {
            var config = new Dictionary<string, string>
            {
                { "ServerBaseUrl", "http://192.168.1.100:8080" },
            };

            var result = ServiceEndpointResolver.ResolveEvhdEnvelopeUrl(config);

            Assert.Equal("http://192.168.1.100:8080/api/evhd-envelope", result);
        }

        [Fact]
        public void ResolveProtectionCheckUrl_BuildsCorrectUrl()
        {
            var config = new Dictionary<string, string>
            {
                { "ServerBaseUrl", "http://192.168.1.100:8080" },
            };

            var result = ServiceEndpointResolver.ResolveProtectionCheckUrl(config);

            Assert.Equal("http://192.168.1.100:8080/api/protect", result);
        }

        [Fact]
        public void ResolveMachineLogWebSocketUri_BuildsCorrectUri()
        {
            var config = new Dictionary<string, string>
            {
                { "ServerBaseUrl", "http://192.168.1.100:8080" },
            };

            var result = ServiceEndpointResolver.ResolveMachineLogWebSocketUri(config);

            Assert.Equal("ws://192.168.1.100:8080/ws/machine-log", result.AbsoluteUri);
        }

        [Fact]
        public void ExtractBaseUrl_StripsEndpointPath()
        {
            var result = ServiceEndpointResolver.ExtractBaseUrl("http://server.com:8080/api/protect?machineId=test");

            Assert.Equal("http://server.com:8080", result);
        }
    }
}
