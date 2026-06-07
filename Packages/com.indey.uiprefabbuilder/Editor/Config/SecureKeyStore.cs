using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Config
{
    public static class SecureKeyStore
    {
        private const string PrefKey = "UIPrefabBuilder_APIKey";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UIPrefabBuilder_v1_salt");

        public static void SaveApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) { EditorPrefs.DeleteKey(PrefKey); return; }
            var encrypted = Obfuscate(Encoding.UTF8.GetBytes(apiKey));
            EditorPrefs.SetString(PrefKey, Convert.ToBase64String(encrypted));
        }

        public static string LoadApiKey()
        {
            var stored = EditorPrefs.GetString(PrefKey, string.Empty);
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            try
            {
                var decrypted = Deobfuscate(Convert.FromBase64String(stored));
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UIPrefabBuilder] Failed to decrypt API key: {e.Message}");
                return string.Empty;
            }
        }

        public static bool HasApiKey() => !string.IsNullOrEmpty(LoadApiKey());

        private static byte[] Obfuscate(byte[] data)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ Entropy[i % Entropy.Length]);
            return result;
        }

        private static byte[] Deobfuscate(byte[] data) => Obfuscate(data);
    }
}
