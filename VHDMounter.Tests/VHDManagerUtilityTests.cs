using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class VHDManagerUtilityTests
    {
        private static object InvokePrivateStatic(string methodName, params object[] args)
        {
            var method = typeof(VHDManager).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method.Invoke(null, args);
        }

        [Fact]
        public void NormalizeMountPoint_ReturnsDefaultForEmpty()
        {
            var result = InvokePrivateStatic("NormalizeMountPoint", string.Empty);

            Assert.Equal(@"N:\", result);
        }

        [Fact]
        public void NormalizeMountPoint_ReturnsDefaultForNull()
        {
            var result = InvokePrivateStatic("NormalizeMountPoint", (string)null);

            Assert.Equal(@"N:\", result);
        }

        [Fact]
        public void NormalizeMountPoint_AddsTrailingSlashToDriveLetter()
        {
            var result = InvokePrivateStatic("NormalizeMountPoint", "D:");

            Assert.Equal(@"D:\", result);
        }

        [Fact]
        public void NormalizeMountPoint_KeepsTrailingSlashIfPresent()
        {
            var result = InvokePrivateStatic("NormalizeMountPoint", @"D:\");

            Assert.Equal(@"D:\", result);
        }

        [Fact]
        public void NormalizeMountPoint_ResolvesRelativePath()
        {
            var result = InvokePrivateStatic("NormalizeMountPoint", "mount");

            Assert.True(Path.IsPathRooted((string)result));
        }

        [Fact]
        public void IsEvhdFile_TrueForEvhdExtension()
        {
            var result = InvokePrivateStatic("IsEvhdFile", "test.evhd");

            Assert.True((bool)result);
        }

        [Fact]
        public void IsEvhdFile_FalseForVhdExtension()
        {
            var result = InvokePrivateStatic("IsEvhdFile", "test.vhd");

            Assert.False((bool)result);
        }

        [Fact]
        public void IsEvhdFile_IsCaseInsensitive()
        {
            var result = InvokePrivateStatic("IsEvhdFile", "test.EVHD");

            Assert.True((bool)result);
        }

        [Fact]
        public void GetVhdDisplayType_ReturnsEvhdForEvhdFile()
        {
            var result = InvokePrivateStatic("GetVhdDisplayType", "test.evhd");

            Assert.Equal("EVHD", result);
        }

        [Fact]
        public void GetVhdDisplayType_ReturnsVhdForVhdFile()
        {
            var result = InvokePrivateStatic("GetVhdDisplayType", "test.vhd");

            Assert.Equal("VHD", result);
        }

        [Fact]
        public void OutputContainsAny_FindsNeedleInStderr()
        {
            var result = InvokePrivateStatic("OutputContainsAny", "error message", "output", new[] { "message" });

            Assert.True((bool)result);
        }

        [Fact]
        public void OutputContainsAny_FindsNeedleInStdout()
        {
            var result = InvokePrivateStatic("OutputContainsAny", "", "success output", new[] { "success" });

            Assert.True((bool)result);
        }

        [Fact]
        public void OutputContainsAny_IsCaseInsensitive()
        {
            var result = InvokePrivateStatic("OutputContainsAny", "ERROR", "", new[] { "error" });

            Assert.True((bool)result);
        }

        [Fact]
        public void OutputContainsAny_ReturnsFalseWhenNotFound()
        {
            var result = InvokePrivateStatic("OutputContainsAny", "output", "output", new[] { "missing" });

            Assert.False((bool)result);
        }

        [Fact]
        public void OutputContainsAny_HandlesNullStderr()
        {
            var result = InvokePrivateStatic("OutputContainsAny", null, "output", new[] { "out" });

            Assert.True((bool)result);
        }

        [Fact]
        public void OutputContainsAny_HandlesNullStdout()
        {
            var result = InvokePrivateStatic("OutputContainsAny", "error", null, new[] { "err" });

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesAlreadyAttached_TrueForAlreadyAttached()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesAlreadyAttached", "", "already attached");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesAlreadyAttached_TrueForChineseText()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesAlreadyAttached", "已经连接", "");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesAlreadyAttached_FalseForOtherText()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesAlreadyAttached", "success", "done");

            Assert.False((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesNotAttached_TrueForNotAttached()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesNotAttached", "not attached", "");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesNotAttached_TrueForChineseText()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesNotAttached", "", "未连接");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesNotAttached_FalseForOtherText()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesNotAttached", "ok", "done");

            Assert.False((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesIoError_TrueForIoDeviceError()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesIoError", "i/o device error", "");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesIoError_TrueForError1117()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesIoError", "error 1117", "");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesIoError_TrueForChineseText()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesIoError", "", "由于 i/o 设备错误");

            Assert.True((bool)result);
        }

        [Fact]
        public void DiskPartOutputIndicatesIoError_FalseForOtherText()
        {
            var result = InvokePrivateStatic("DiskPartOutputIndicatesIoError", "success", "done");

            Assert.False((bool)result);
        }

        [Fact]
        public void CompactDiagnosticText_TrimsWhitespace()
        {
            var result = InvokePrivateStatic("CompactDiagnosticText", "  a   b  c  ", 100);

            Assert.Equal("a b c", result);
        }

        [Fact]
        public void CompactDiagnosticText_TruncatesLongText()
        {
            var result = InvokePrivateStatic("CompactDiagnosticText", "abcdefghijklmnopqrstuvwxyz", 10);

            Assert.Equal("abcdefghij...", result);
        }

        [Fact]
        public void CompactDiagnosticText_ReturnsEmptyForNull()
        {
            var result = InvokePrivateStatic("CompactDiagnosticText", (string)null, 100);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void SanitizeSensitiveText_DelegatesToMachineLogSanitizer()
        {
            var result = InvokePrivateStatic("SanitizeSensitiveText", "password: secret123");

            Assert.Equal("password: ***", result);
        }

        [Fact]
        public void BuildMountToolFailureDetail_IncludesStderr()
        {
            var result = InvokePrivateStatic("BuildMountToolFailureDetail", 1, "mount failed", "");

            Assert.Contains("退出码 1", (string)result);
            Assert.Contains("mount failed", (string)result);
        }

        [Fact]
        public void BuildMountToolFailureDetail_FallsBackToStdout()
        {
            var result = InvokePrivateStatic("BuildMountToolFailureDetail", 2, "", "output message");

            Assert.Contains("output message", (string)result);
        }

        [Fact]
        public void BuildMountToolFailureDetail_OnlyExitCodeWhenNoOutput()
        {
            var result = InvokePrivateStatic("BuildMountToolFailureDetail", 3, "", "");

            Assert.Equal("EVHD挂载进程提前退出，退出码 3", result);
        }

        [Fact]
        public void BuildProcessFailureDetail_IncludesStderr()
        {
            var result = InvokePrivateStatic("BuildProcessFailureDetail", "diskpart", 1, "syntax error", "");

            Assert.Contains("diskpart", (string)result);
            Assert.Contains("syntax error", (string)result);
        }

        [Fact]
        public void BuildProcessFailureDetail_OnlyPrefixAndExitCodeWhenNoOutput()
        {
            var result = InvokePrivateStatic("BuildProcessFailureDetail", "diskpart", 0, "", "");

            Assert.Equal("diskpart（退出码 0）", result);
        }

        [Fact]
        public void FormatProcessArgumentsForLog_EscapesArgumentsWithSpaces()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "test.exe",
            };
            startInfo.ArgumentList.Add("--path");
            startInfo.ArgumentList.Add("C:\\Program Files\\App");
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add("test");

            var result = InvokePrivateStatic("FormatProcessArgumentsForLog", startInfo);

            Assert.Contains("\"C:\\Program Files\\App\"", (string)result);
        }

        [Fact]
        public void FormatProcessArgumentsForLog_SanitizesPasswords()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "test.exe",
            };
            startInfo.ArgumentList.Add("--password=secret123");

            var result = InvokePrivateStatic("FormatProcessArgumentsForLog", startInfo);

            Assert.Contains("***", (string)result);
            Assert.DoesNotContain("secret123", (string)result);
        }

        [Fact]
        public void FormatProcessArgumentsForLog_HandlesEmptyArgument()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "test.exe",
            };
            startInfo.ArgumentList.Add("");

            var result = InvokePrivateStatic("FormatProcessArgumentsForLog", startInfo);

            Assert.Contains("\"\"", (string)result);
        }

        [Fact]
        public void NormalizePasswordForMountStdin_TrimsTrailingTerminators()
        {
            var result = InvokePrivateStatic("NormalizePasswordForMountStdin", "secret\r\n\0", 0, false, false);

            Assert.Equal("secret", result);
        }

        [Fact]
        public void NormalizePasswordForMountStdin_RemovesBom()
        {
            var password = "﻿secret";
            var result = InvokePrivateStatic("NormalizePasswordForMountStdin", password, 0, false, false);

            Assert.Equal("secret", result);
        }

        [Fact]
        public void ResolveExecutableFromPath_ReturnsEmptyForEmptyInput()
        {
            var result = InvokePrivateStatic("ResolveExecutableFromPath", string.Empty);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ResolveExecutableFromPath_ReturnsFullPathForRootedPath()
        {
            var result = InvokePrivateStatic("ResolveExecutableFromPath", @"C:\\Windows\\System32\\notepad.exe");

            Assert.True(Path.IsPathRooted((string)result));
        }

        [Fact]
        public void ResolveExecutableFromPath_FindsExistingExecutable()
        {
            var result = InvokePrivateStatic("ResolveExecutableFromPath", "cmd.exe");

            Assert.True(Path.IsPathRooted((string)result));
            Assert.EndsWith("cmd.exe", (string)result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveExecutableFromPath_ReturnsOriginalWhenNotFound()
        {
            var result = InvokePrivateStatic("ResolveExecutableFromPath", "nonexistent-tool-xyz.exe");

            Assert.Equal("nonexistent-tool-xyz.exe", result);
        }
    }
}
