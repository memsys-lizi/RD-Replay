using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RDReplay.Core;

namespace RDReplay.Storage
{
    /// <summary>
    /// 回放文件签名/验证（HMAC-SHA256）。
    /// 用途：防止玩家手动篡改 replay.json 来伪造成绩或解锁内容。
    /// 注意：本地 mod 无法做到服务端级别的防作弊，此举主要阻止"普通用户"改文件。
    /// </summary>
    public static class ReplayCrypto
    {
        // 签名密钥：硬编码在 mod 内（混淆后），不对外公开。
        // 若未来需要更强的保护，可改为从服务器下发或结合机器特征衍生。
        private static readonly byte[] _secretKey = Encoding.UTF8.GetBytes(
            "RD-Replay-HMAC-Key-v1_7f3a91c0e2b84d5f");

        /// <summary>
        /// 对 ReplayData 计算签名并写入 data.signature，然后返回最终 JSON 字符串。
        /// 签名覆盖除 signature 字段本身以外的所有数据。
        /// </summary>
        public static string SignAndSerialize(ReplayData data)
        {
            // 先清空旧签名，保证序列化结果可复现
            data.signature = null;

            // 紧凑序列化（不含缩进），用于签名计算
            string payload = JsonConvert.SerializeObject(data, Formatting.None);

            data.signature = ComputeHmac(payload);

            // 最终带签名的 JSON（带缩进，方便调试）
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        /// <summary>
        /// 验证 JSON 字符串的签名是否有效。
        /// </summary>
        /// <param name="json">从文件读取的原始 JSON</param>
        /// <param name="data">反序列化后的对象（out）</param>
        /// <returns>签名是否合法</returns>
        public static bool VerifyAndDeserialize(string json, out ReplayData data)
        {
            data = null;

            try
            {
                data = JsonConvert.DeserializeObject<ReplayData>(json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Crypto] JSON parse failed: {ex.Message}");
                return false;
            }

            if (data == null || string.IsNullOrEmpty(data.signature))
            {
                Plugin.Log.LogWarning("[Crypto] Missing signature.");
                return false;
            }

            // 保存并清空签名字段，重新序列化后计算
            string storedSig = data.signature;
            data.signature = null;
            string payload = JsonConvert.SerializeObject(data, Formatting.None);
            string expected = ComputeHmac(payload);
            data.signature = storedSig;

            bool valid = storedSig == expected;
            if (!valid)
                Plugin.Log.LogWarning("[Crypto] Signature mismatch! File may have been tampered with.");

            return valid;
        }

        // ── 内部工具 ─────────────────────────────────────────────────

        private static string ComputeHmac(string data)
        {
            using (var hmac = new HMACSHA256(_secretKey))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToBase64String(hash);
            }
        }
    }
}
