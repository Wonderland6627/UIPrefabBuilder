using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Indexing
{
    /// <summary>
    /// CLIP BPE tokenizer (ViT-B/32 compatible).
    /// Tokenizes text into integer IDs for the CLIP text encoder.
    /// </summary>
    public class ClipTokenizer
    {
        private const int MaxLength = 77;
        private const int SOTToken = 49406;
        private const int EOTToken = 49407;

        private Dictionary<string, int> _encoder;
        private Dictionary<int, string> _decoder;
        private Dictionary<(string, string), int> _bpeRanks;
        private Dictionary<string, string> _cache;
        private Regex _pattern;

        public bool IsLoaded => _encoder != null && _encoder.Count > 0;

        public void Load(string vocabPath, string mergesPath)
        {
            _cache = new Dictionary<string, string>();

            LoadVocab(vocabPath);
            LoadMerges(mergesPath);

            _pattern = new Regex(
                @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private void LoadVocab(string path)
        {
            var fullPath = ResolveAssetPath(path);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[ClipTokenizer] Vocab file not found: {fullPath}");
                return;
            }

            _encoder = new Dictionary<string, int>();
            _decoder = new Dictionary<int, string>();

            var content = File.ReadAllText(fullPath).TrimStart();

            if (content.StartsWith("{"))
            {
                // JSON format: {"token": id, ...}
                ParseJsonVocab(content);
            }
            else
            {
                // Plain text format: one token per line, line number = id
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    var token = lines[i].TrimEnd();
                    if (string.IsNullOrEmpty(token)) continue;
                    _encoder[token] = i;
                    _decoder[i] = token;
                }
            }
        }

        private void ParseJsonVocab(string json)
        {
            // Lightweight JSON parser for {"key": int, ...} without external dependencies
            int i = json.IndexOf('{') + 1;
            int end = json.LastIndexOf('}');

            while (i < end)
            {
                // Skip whitespace/commas
                while (i < end && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n' || json[i] == '\r' || json[i] == ','))
                    i++;

                if (i >= end) break;
                if (json[i] != '"') { i++; continue; }

                // Parse key
                i++; // skip opening quote
                var keyStart = i;
                while (i < end && json[i] != '"')
                {
                    if (json[i] == '\\') i++; // skip escaped char
                    i++;
                }
                var key = json.Substring(keyStart, i - keyStart)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\/", "/")
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t");
                i++; // skip closing quote

                // Skip colon and whitespace
                while (i < end && (json[i] == ' ' || json[i] == ':' || json[i] == '\t'))
                    i++;

                // Parse integer value
                var numStart = i;
                while (i < end && json[i] >= '0' && json[i] <= '9')
                    i++;

                if (int.TryParse(json.Substring(numStart, i - numStart), out int id))
                {
                    _encoder[key] = id;
                    _decoder[id] = key;
                }
            }
        }

        private void LoadMerges(string path)
        {
            var fullPath = ResolveAssetPath(path);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[ClipTokenizer] Merges file not found: {fullPath}");
                return;
            }

            _bpeRanks = new Dictionary<(string, string), int>();
            var lines = File.ReadAllLines(fullPath);
            int rank = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split(' ');
                if (parts.Length != 2) continue;
                _bpeRanks[(parts[0], parts[1])] = rank++;
            }
        }

        /// <summary>
        /// Tokenize text into CLIP input_ids (int32 array of length 77).
        /// </summary>
        public int[] Encode(string text)
        {
            if (_encoder == null || _bpeRanks == null)
                return null;

            text = text.ToLowerInvariant().Trim();

            var tokens = new List<int> { SOTToken };
            var matches = _pattern.Matches(text);

            foreach (Match match in matches)
            {
                var word = match.Value;
                var encoded = EncodeWord(word);
                tokens.AddRange(encoded);

                if (tokens.Count >= MaxLength - 1)
                    break;
            }

            tokens.Add(EOTToken);

            while (tokens.Count < MaxLength)
                tokens.Add(0);

            if (tokens.Count > MaxLength)
                tokens = tokens.Take(MaxLength).ToList();

            return tokens.ToArray();
        }

        private IEnumerable<int> EncodeWord(string word)
        {
            var bpeWord = string.Join("", word.Select(c => ByteToUnicode(c))) + "</w>";

            if (_cache.TryGetValue(bpeWord, out var cached))
            {
                return cached.Split(' ').Select(t => _encoder.TryGetValue(t, out var id) ? id : 0);
            }

            var bpeTokens = ApplyBPE(bpeWord);
            _cache[bpeWord] = bpeTokens;

            return bpeTokens.Split(' ').Select(t => _encoder.TryGetValue(t, out var id) ? id : 0);
        }

        private string ApplyBPE(string token)
        {
            var word = token.Select(c => c.ToString()).ToList();

            if (word.Count <= 1)
                return token;

            while (true)
            {
                int minRank = int.MaxValue;
                int minIdx = -1;

                for (int i = 0; i < word.Count - 1; i++)
                {
                    var pair = (word[i], word[i + 1]);
                    if (_bpeRanks.TryGetValue(pair, out var rank) && rank < minRank)
                    {
                        minRank = rank;
                        minIdx = i;
                    }
                }

                if (minIdx < 0)
                    break;

                var merged = word[minIdx] + word[minIdx + 1];
                word[minIdx] = merged;
                word.RemoveAt(minIdx + 1);
            }

            return string.Join(" ", word);
        }

        private static string ByteToUnicode(char c)
        {
            var b = (int)c;
            if (b >= 33 && b <= 126) return c.ToString();
            if (b >= 161 && b <= 172) return c.ToString();
            if (b >= 174 && b <= 255) return c.ToString();
            return ((char)(b + 256)).ToString();
        }

        private static string ResolveAssetPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;

            if (path.StartsWith("Packages/") || path.StartsWith("Assets/"))
                return Path.GetFullPath(path);

            return path;
        }
    }
}
