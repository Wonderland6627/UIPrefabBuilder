using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json;

namespace Indey.UIPrefabBuilder.Indexing
{
    [Serializable]
    public class AssetIndexEntry
    {
        public string assetPath;
        public string guid;
        public string contentHash;
        public long timestamp;
        public int vectorOffset;
    }

    [Serializable]
    internal class IndexMetaFile
    {
        public int version = 1;
        public int dimension = EmbeddingService.Dimension;
        public int count;
        public List<AssetIndexEntry> entries = new List<AssetIndexEntry>();
    }

    public class VectorStore
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AIDX");
        private const int BinaryVersion = 1;

        private List<AssetIndexEntry> _entries = new List<AssetIndexEntry>();
        private float[] _vectorMatrix;
        private int _dimension = EmbeddingService.Dimension;
        private int _capacity;
        private bool _dirty;

        public int Count => _entries.Count;
        public bool IsLoaded { get; private set; }
        public long LastBuildTimestamp { get; private set; }

        public void Load(string metaPath, string vectorsPath)
        {
            _entries.Clear();
            _vectorMatrix = null;
            IsLoaded = false;

            if (!File.Exists(metaPath) || !File.Exists(vectorsPath))
            {
                InitFresh();
                ConsoleLogger.Log("[VectorStore] No existing index found, starting fresh.");
                return;
            }

            try
            {
                var json = File.ReadAllText(metaPath, Encoding.UTF8);
                var meta = JsonConvert.DeserializeObject<IndexMetaFile>(json);
                if (meta == null || meta.entries == null)
                    throw new Exception("Invalid meta file");

                _dimension = meta.dimension;
                _entries = meta.entries;

                var bytes = File.ReadAllBytes(vectorsPath);
                if (!ValidateBinaryHeader(bytes, out int fileDim, out int fileCount))
                    throw new Exception("Invalid binary header");

                if (fileDim != _dimension)
                    throw new Exception($"Dimension mismatch: meta={_dimension}, binary={fileDim}");

                int headerSize = 16;
                int expectedDataSize = meta.count * _dimension * 4;
                int actualDataSize = bytes.Length - headerSize;
                if (actualDataSize < expectedDataSize)
                {
                    ConsoleLogger.Warning($"[VectorStore] Binary data truncated (expected {expectedDataSize}B, got {actualDataSize}B). Rebuilding.");
                    InitFresh();
                    return;
                }

                int dataFloats = actualDataSize / 4;
                _vectorMatrix = new float[dataFloats];
                Buffer.BlockCopy(bytes, headerSize, _vectorMatrix, 0, actualDataSize);
                _capacity = dataFloats / _dimension;

                // Validate entry offsets are within bounds
                int maxOffset = _capacity - 1;
                var validEntries = _entries.FindAll(e => e.vectorOffset >= 0 && e.vectorOffset <= maxOffset);
                if (validEntries.Count < _entries.Count)
                {
                    ConsoleLogger.Warning($"[VectorStore] Pruned {_entries.Count - validEntries.Count} entries with invalid offsets.");
                    _entries = validEntries;
                }

                LastBuildTimestamp = _entries.Count > 0
                    ? _entries.Max(e => e.timestamp) : 0;

                IsLoaded = true;
                ConsoleLogger.Log($"[VectorStore] Loaded {_entries.Count} entries ({_dimension}D).");
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[VectorStore] Load failed: {e.Message}. Starting fresh.");
                InitFresh();
            }
        }

        private void InitFresh()
        {
            _entries = new List<AssetIndexEntry>();
            _vectorMatrix = new float[256 * _dimension];
            _capacity = 256;
            IsLoaded = true;
        }

        public void Save(string metaPath, string vectorsPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(metaPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var meta = new IndexMetaFile
                {
                    version = 1,
                    dimension = _dimension,
                    count = _entries.Count,
                    entries = _entries
                };
                var json = JsonConvert.SerializeObject(meta, Formatting.Indented);
                File.WriteAllText(metaPath, json, Encoding.UTF8);

                SaveBinary(vectorsPath);
                _dirty = false;
                ConsoleLogger.Log($"[VectorStore] Saved {_entries.Count} entries.");
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[VectorStore] Save failed: {e.Message}");
            }
        }

        public void Upsert(string assetPath, string guid, string contentHash, float[] vector)
        {
            if (vector == null || vector.Length != _dimension) return;

            var existing = _entries.FindIndex(e => e.assetPath == assetPath);
            if (existing >= 0)
            {
                var entry = _entries[existing];
                entry.contentHash = contentHash;
                entry.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Array.Copy(vector, 0, _vectorMatrix, entry.vectorOffset * _dimension, _dimension);
            }
            else
            {
                int offset = _entries.Count;
                EnsureCapacity(offset + 1);

                var entry = new AssetIndexEntry
                {
                    assetPath = assetPath,
                    guid = guid,
                    contentHash = contentHash,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    vectorOffset = offset
                };
                _entries.Add(entry);
                Array.Copy(vector, 0, _vectorMatrix, offset * _dimension, _dimension);
            }
            _dirty = true;
        }

        public void Remove(string assetPath)
        {
            var idx = _entries.FindIndex(e => e.assetPath == assetPath);
            if (idx < 0) return;

            _entries.RemoveAt(idx);
            _dirty = true;
        }

        public bool NeedsUpdate(string assetPath, string currentHash)
        {
            var entry = _entries.Find(e => e.assetPath == assetPath);
            if (entry == null) return true;
            return entry.contentHash != currentHash;
        }

        public bool Contains(string assetPath)
        {
            return _entries.Any(e => e.assetPath == assetPath);
        }

        /// <summary>
        /// Returns the compacted matrix and entry list for similarity search.
        /// After Remove operations, call Compact() first for correct results.
        /// </summary>
        public (float[] matrix, List<AssetIndexEntry> entries) GetMatrixView()
        {
            return (_vectorMatrix, _entries);
        }

        /// <summary>
        /// Rebuild the matrix to remove gaps left by deletions.
        /// </summary>
        public void Compact()
        {
            if (_entries.Count == 0)
            {
                _vectorMatrix = new float[256 * _dimension];
                _capacity = 256;
                return;
            }

            var newMatrix = new float[_entries.Count * _dimension];
            for (int i = 0; i < _entries.Count; i++)
            {
                int srcOffset = _entries[i].vectorOffset * _dimension;
                int dstOffset = i * _dimension;
                if (srcOffset + _dimension <= _vectorMatrix.Length)
                    Array.Copy(_vectorMatrix, srcOffset, newMatrix, dstOffset, _dimension);
                _entries[i].vectorOffset = i;
            }

            _vectorMatrix = newMatrix;
            _capacity = _entries.Count;
            _dirty = true;
        }

        public bool IsDirty => _dirty;

        private void EnsureCapacity(int required)
        {
            if (_vectorMatrix != null && required * _dimension <= _vectorMatrix.Length)
                return;

            int newCap = Math.Max(_capacity * 2, required);
            var newMatrix = new float[newCap * _dimension];
            if (_vectorMatrix != null)
                Array.Copy(_vectorMatrix, newMatrix, Math.Min(_vectorMatrix.Length, newMatrix.Length));
            _vectorMatrix = newMatrix;
            _capacity = newCap;
        }

        private void SaveBinary(string path)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            bw.Write(Magic);
            bw.Write(BinaryVersion);
            bw.Write(_dimension);
            bw.Write(_entries.Count);

            for (int i = 0; i < _entries.Count; i++)
            {
                int offset = _entries[i].vectorOffset * _dimension;
                for (int j = 0; j < _dimension; j++)
                    bw.Write(_vectorMatrix[offset + j]);
            }
        }

        private bool ValidateBinaryHeader(byte[] data, out int dimension, out int count)
        {
            dimension = 0;
            count = 0;
            if (data.Length < 16) return false;

            for (int i = 0; i < 4; i++)
                if (data[i] != Magic[i]) return false;

            int ver = BitConverter.ToInt32(data, 4);
            if (ver != BinaryVersion) return false;

            dimension = BitConverter.ToInt32(data, 8);
            count = BitConverter.ToInt32(data, 12);
            return dimension > 0 && count >= 0;
        }
    }
}
