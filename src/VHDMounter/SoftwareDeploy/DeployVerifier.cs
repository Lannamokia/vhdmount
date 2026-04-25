using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace VHDMounter.SoftwareDeploy
{
    public class DeployVerificationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ExtractPath { get; set; } = string.Empty;
        public DeployManifest Manifest { get; set; } = new();
    }

    public static class DeployVerifier
    {
        public static DeployVerificationResult VerifyAndExtract(string zipPath, string sigPath, string trustedKeysPemPath)
        {
            var result = new DeployVerificationResult();

            if (!File.Exists(zipPath))
            {
                result.ErrorMessage = "ZIP 包不存在";
                return result;
            }
            if (!File.Exists(sigPath))
            {
                result.ErrorMessage = "签名文件不存在";
                return result;
            }
            if (!File.Exists(trustedKeysPemPath))
            {
                result.ErrorMessage = "可信公钥文件不存在";
                return result;
            }

            // 1. 整包签名验证
            bool sigOk = UpdateSecurity.VerifyManifestSignature(zipPath, sigPath, trustedKeysPemPath);
            if (!sigOk)
            {
                result.ErrorMessage = "ZIP 包签名验证失败";
                return result;
            }

            // 2. 解压到临时目录
            string extractDir = Path.Combine(Path.GetTempPath(), $"vhd-deploy-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"ZIP 解压失败: {ex.Message}";
                Cleanup(extractDir);
                return result;
            }

            // 3. 读取 deploy.json
            string deployJsonPath = Path.Combine(extractDir, "deploy.json");
            if (!File.Exists(deployJsonPath))
            {
                result.ErrorMessage = "deploy.json 不存在";
                Cleanup(extractDir);
                return result;
            }

            DeployManifest manifest;
            try
            {
                var json = File.ReadAllText(deployJsonPath);
                manifest = JsonSerializer.Deserialize<DeployManifest>(json);
                if (manifest == null)
                {
                    result.ErrorMessage = "deploy.json 解析失败";
                    Cleanup(extractDir);
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"deploy.json 解析异常: {ex.Message}";
                Cleanup(extractDir);
                return result;
            }

            // 4. 校验过期时间
            if (!string.IsNullOrWhiteSpace(manifest.expiresAt))
            {
                if (DateTime.TryParse(manifest.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exp))
                {
                    if (DateTime.UtcNow > exp)
                    {
                        result.ErrorMessage = "部署包已过期";
                        Cleanup(extractDir);
                        return result;
                    }
                }
            }

            // 5. 校验类型
            if (!manifest.IsSoftwareDeploy && !manifest.IsFileDeploy)
            {
                result.ErrorMessage = $"未知的部署类型: {manifest.type}";
                Cleanup(extractDir);
                return result;
            }

            // 6. 校验脚本存在性（software-deploy 必须包含 install.ps1）
            if (manifest.IsSoftwareDeploy)
            {
                string scriptPath = Path.Combine(extractDir, manifest.installScript);
                if (string.IsNullOrWhiteSpace(manifest.installScript) || !File.Exists(scriptPath))
                {
                    result.ErrorMessage = "software-deploy 包缺少 install.ps1";
                    Cleanup(extractDir);
                    return result;
                }
            }

            result.Success = true;
            result.ExtractPath = extractDir;
            result.Manifest = manifest;
            return result;
        }

        public static void Cleanup(string extractDir)
        {
            if (string.IsNullOrEmpty(extractDir)) return;
            try
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
            }
            catch { }
        }
    }
}
