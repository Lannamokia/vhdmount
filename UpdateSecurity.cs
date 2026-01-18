using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VHDMounter
{
    public class UpdateManifestFile
    {
        public string path { get; set; } = string.Empty;
        public string target { get; set; } = string.Empty;
        public long size { get; set; }
        public string sha256 { get; set; } = string.Empty;
    }

    public class UpdateManifest
    {
        public string version { get; set; } = string.Empty;
        public string minVersion { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string signer { get; set; } = string.Empty;
        public string createdAt { get; set; } = string.Empty;
        public string expiresAt { get; set; } = string.Empty;
        public List<UpdateManifestFile> files { get; set; } = new();
    }

    public static class UpdateSecurity
    {
        public static UpdateManifest LoadManifest(string manifestPath)
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            return manifest ?? new UpdateManifest();
        }

        public static bool VerifyManifestSignature(string manifestPath, string signaturePath, string trustedKeysPemPath)
        {
            if (!File.Exists(trustedKeysPemPath)) return false;
            if (!File.Exists(signaturePath)) return false;
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var sigBytes = ReadSignatureBytes(signaturePath);
            var rsas = LoadRsaKeysFromPem(trustedKeysPemPath);
            foreach (var rsa in rsas)
            {
                try
                {
                    if (rsa.VerifyData(manifestBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        public static bool VerifyFileHash(string path, string expectedSha256, long expectedSize)
        {
            if (!File.Exists(path)) return false;
            var fi = new FileInfo(path);
            if (expectedSize > 0 && fi.Length != expectedSize) return false;
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = sha.ComputeHash(fs);
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return string.Equals(hex, expectedSha256?.ToLowerInvariant(), StringComparison.Ordinal);
        }

        private static byte[] ReadSignatureBytes(string signaturePath)
        {
            var raw = File.ReadAllText(signaturePath).Trim();
            try
            {
                return Convert.FromBase64String(raw);
            }
            catch
            {
                return File.ReadAllBytes(signaturePath);
            }
        }

        private static List<RSA> LoadRsaKeysFromPem(string pemPath)
        {
            var text = File.ReadAllText(pemPath);
            var blocks = SplitPemBlocks(text);
            var list = new List<RSA>();
            foreach (var b in blocks)
            {
                try
                {
                    var data = Convert.FromBase64String(b);
                    var rsa = RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(data, out _);
                    if (rsa.KeySize < 2048) rsa.KeySize = 2048;
                    list.Add(rsa);
                }
                catch { }
            }
            return list;
        }

        private static List<string> SplitPemBlocks(string pem)
        {
            var lines = pem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var blocks = new List<string>();
            var capture = false;
            foreach (var l in lines)
            {
                var s = l.Trim();
                if (s.StartsWith("-----BEGIN PUBLIC KEY-----"))
                {
                    sb.Clear();
                    capture = true;
                    continue;
                }
                if (s.StartsWith("-----END PUBLIC KEY-----"))
                {
                    capture = false;
                    blocks.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                if (capture)
                {
                    sb.Append(s);
                }
            }
            return blocks;
        }
    }
}
