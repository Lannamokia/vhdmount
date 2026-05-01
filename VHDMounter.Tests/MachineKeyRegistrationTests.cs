using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class MachineKeyRegistrationTests : IDisposable
    {
        public MachineKeyRegistrationTests()
        {
            MachineKeyRegistration.ResetStateForTests();
        }

        public void Dispose()
        {
            MachineKeyRegistration.ResetStateForTests();
        }

        [Fact]
        public void ParseRetryAfter_PrefersStructuredPayloadSeconds()
        {
            using var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

            var result = (TimeSpan?)InvokePrivateStatic(
                "ParseRetryAfter",
                response,
                "{\"retryAfterSeconds\":42}");

            Assert.Equal(TimeSpan.FromSeconds(42), result);
        }

        [Fact]
        public void ParseRetryAfter_FallsBackToRetryAfterHeader()
        {
            using var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(9));

            var result = (TimeSpan?)InvokePrivateStatic(
                "ParseRetryAfter",
                response,
                "{\"error\":\"rate limited\"}");

            Assert.Equal(TimeSpan.FromSeconds(9), result);
        }

        [Fact]
        public void ParseErrorPayload_ReturnsStructuredErrorCodeAndMessage()
        {
            var result = ((string? errorCode, string? errorMessage))InvokePrivateStatic(
                "ParseErrorPayload",
                "{\"errorCode\":\"MACHINE_NOT_REGISTERED\",\"error\":\"missing key\"}");

            Assert.Equal("MACHINE_NOT_REGISTERED", result.errorCode);
            Assert.Equal("missing key", result.errorMessage);
        }

        [Theory]
        [InlineData("未注册公钥")]
        [InlineData("machine key is not registered")]
        [InlineData("该机台尚未注册")]
        public void ContainsNotRegisteredHint_RecognizesKnownMessages(string message)
        {
            var result = (bool)InvokePrivateStatic("ContainsNotRegisteredHint", message);

            Assert.True(result);
        }

        [Fact]
        public void ResetStateForTests_ClearsCachedStateAndBackoff()
        {
            SetPrivateStaticField("_currentState", MachineKeyRegistration.RegistrationState.Approved);
            SetPrivateStaticField("_nextRegistrationAttempt", DateTimeOffset.UtcNow.AddMinutes(3));

            MachineKeyRegistration.ResetStateForTests();

            Assert.Equal(MachineKeyRegistration.RegistrationState.Unknown, MachineKeyRegistration.CurrentState);
            Assert.Null(GetPrivateStaticField<DateTimeOffset?>("_nextRegistrationAttempt"));
        }

        private static object? InvokePrivateStatic(string methodName, params object?[] args)
        {
            var method = typeof(MachineKeyRegistration).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method!.Invoke(null, args);
        }

        private static void SetPrivateStaticField(string fieldName, object? value)
        {
            var field = typeof(MachineKeyRegistration).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            field!.SetValue(null, value);
        }

        private static T GetPrivateStaticField<T>(string fieldName)
        {
            var field = typeof(MachineKeyRegistration).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            return (T)field!.GetValue(null)!;
        }
    }
}
