using System;
using System.Linq;
using UnityEngine;
using Indey.UIPrefabBuilder.Logging;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if SENTIS_AVAILABLE
using Unity.Sentis;
#endif

namespace Indey.UIPrefabBuilder.Indexing
{
    public class EmbeddingService : IDisposable
    {
        public const int Dimension = 512;

        private bool _isLoaded;
        private bool _disposed;

        private bool _textModelLoaded;
        private ClipTokenizer _tokenizer;

#if SENTIS_AVAILABLE
        private Model _model;
        private IWorker _worker;
        private string _pixelInputName;
        private bool _needsScale;
        private string _scaleInputName;

        private Model _textModel;
        private IWorker _textWorker;
        private string _textInputName;
        private string _textAttnMaskName;
#endif

        public bool IsLoaded => _isLoaded;
        public bool IsTextModelLoaded => _textModelLoaded;

        public void LoadModel(string modelPath)
        {
            if (_isLoaded) return;

#if SENTIS_AVAILABLE
            try
            {
                _model = LoadModelFromAsset(modelPath);
                if (_model == null)
                {
                    ConsoleLogger.Error($"[EmbeddingService] Failed to load model asset from: {modelPath}");
                    return;
                }

                DetectModelInputs();

                try
                {
                    _worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, _model);
                    _isLoaded = true;
                    ConsoleLogger.Log($"[EmbeddingService] Model loaded from {modelPath} (GPU backend)");
                }
                catch (Exception)
                {
                    _worker?.Dispose();
                    _worker = WorkerFactory.CreateWorker(BackendType.CPU, _model);
                    _isLoaded = true;
                    ConsoleLogger.Log($"[EmbeddingService] Model loaded from {modelPath} (CPU fallback)");
                }
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[EmbeddingService] Failed to load model: {e.Message}");
                _isLoaded = false;
            }
#else
            ConsoleLogger.Warning("[EmbeddingService] Unity Sentis not installed. Asset indexing unavailable.");
#endif
        }

        /// <summary>
        /// Load the CLIP text encoder model and tokenizer for text-to-image search.
        /// </summary>
        public void LoadTextModel(string textModelPath, string vocabPath, string mergesPath)
        {
            if (_textModelLoaded) return;

#if SENTIS_AVAILABLE
            try
            {
                _textModel = LoadModelFromAsset(textModelPath);
                if (_textModel == null)
                {
                    ConsoleLogger.Error($"[EmbeddingService] Failed to load text model from: {textModelPath}");
                    return;
                }

                _textInputName = null;
                _textAttnMaskName = null;
                foreach (var input in _textModel.inputs)
                {
                    ConsoleLogger.Log($"[EmbeddingService] Text model input: '{input.name}'");
                    if (input.name.Contains("input_ids") || input.name.Contains("input"))
                        _textInputName = input.name;
                    else if (input.name.Contains("attention_mask") || input.name.Contains("mask"))
                        _textAttnMaskName = input.name;
                }
                if (_textInputName == null)
                    _textInputName = _textModel.inputs[0].name;

                try
                {
                    _textWorker = WorkerFactory.CreateWorker(BackendType.GPUCompute, _textModel);
                }
                catch (Exception)
                {
                    _textWorker?.Dispose();
                    _textWorker = WorkerFactory.CreateWorker(BackendType.CPU, _textModel);
                }

                _tokenizer = new ClipTokenizer();
                _tokenizer.Load(vocabPath, mergesPath);

                if (!_tokenizer.IsLoaded)
                {
                    ConsoleLogger.Error("[EmbeddingService] Tokenizer failed to load vocab/merges.");
                    _textWorker?.Dispose();
                    _textWorker = null;
                    return;
                }

                _textModelLoaded = true;
                ConsoleLogger.Log($"[EmbeddingService] Text model loaded from {textModelPath}");
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[EmbeddingService] Failed to load text model: {e.Message}");
                _textModelLoaded = false;
            }
#else
            ConsoleLogger.Warning("[EmbeddingService] Unity Sentis not installed. Text encoding unavailable.");
#endif
        }

        /// <summary>
        /// Extract a normalized embedding vector from text using the CLIP text encoder.
        /// Must be called on the main thread.
        /// </summary>
        public float[] ExtractTextEmbedding(string text)
        {
            if (!_textModelLoaded || string.IsNullOrEmpty(text)) return null;

#if SENTIS_AVAILABLE
            TensorInt inputTensor = null;
            TensorInt attnTensor = null;
            try
            {
                var tokenIds = _tokenizer.Encode(text);
                if (tokenIds == null) return null;

                inputTensor = new TensorInt(new TensorShape(1, tokenIds.Length), tokenIds);
                _textWorker.SetInput(_textInputName, inputTensor);

                if (_textAttnMaskName != null)
                {
                    var attnMask = new int[tokenIds.Length];
                    for (int i = 0; i < tokenIds.Length; i++)
                        attnMask[i] = tokenIds[i] != 0 ? 1 : 0;
                    attnTensor = new TensorInt(new TensorShape(1, tokenIds.Length), attnMask);
                    _textWorker.SetInput(_textAttnMaskName, attnTensor);
                }

                _textWorker.Execute();

                var outputTensor = _textWorker.PeekOutput() as TensorFloat;
                if (outputTensor == null) return null;

                outputTensor.MakeReadable();
                var raw = outputTensor.ToReadOnlyArray();

                // Take first Dimension elements (the pooled output)
                var result = new float[Dimension];
                int copyLen = Math.Min(raw.Length, Dimension);
                Array.Copy(raw, result, copyLen);

                L2Normalize(result);
                return result;
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[EmbeddingService] Text inference failed: {e.Message}");
                return null;
            }
            finally
            {
                inputTensor?.Dispose();
                attnTensor?.Dispose();
            }
#else
            return null;
#endif
        }

#if SENTIS_AVAILABLE
        /// <summary>
        /// Load model via Unity's asset pipeline (which handles ONNX import)
        /// rather than raw file I/O. This ensures proper format conversion.
        /// </summary>
        private static Model LoadModelFromAsset(string modelPath)
        {
#if UNITY_EDITOR
            // Convert absolute/resolved paths back to Unity asset paths
            var assetPath = ToUnityAssetPath(modelPath);
            if (string.IsNullOrEmpty(assetPath))
            {
                ConsoleLogger.Error($"[EmbeddingService] Cannot resolve Unity asset path from: {modelPath}");
                return null;
            }

            ConsoleLogger.Log($"[EmbeddingService] Loading model asset: {assetPath}");

            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
            if (modelAsset == null)
            {
                ConsoleLogger.Error($"[EmbeddingService] ModelAsset not found at: {assetPath}. Ensure the .onnx file is imported by Unity.");
                return null;
            }

            return ModelLoader.Load(modelAsset);
#else
            return null;
#endif
        }

        private static string ToUnityAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Already a valid Unity relative path
            if (path.StartsWith("Packages/") || path.StartsWith("Assets/"))
                return path;

            // Convert absolute path to Unity-relative path
            var normalized = path.Replace('\\', '/');

            int packagesIdx = normalized.IndexOf("/Packages/", StringComparison.OrdinalIgnoreCase);
            if (packagesIdx >= 0)
                return normalized.Substring(packagesIdx + 1);

            int assetsIdx = normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIdx >= 0)
                return normalized.Substring(assetsIdx + 1);

            return null;
        }

        private void DetectModelInputs()
        {
            _pixelInputName = _model.inputs[0].name;
            _needsScale = false;

            foreach (var input in _model.inputs)
            {
                ConsoleLogger.Log($"[EmbeddingService] Model input: '{input.name}'");

                if (input.name.Contains("scale"))
                {
                    _needsScale = true;
                    _scaleInputName = input.name;
                }
                else
                {
                    _pixelInputName = input.name;
                }
            }

            foreach (var outputName in _model.outputs)
                ConsoleLogger.Log($"[EmbeddingService] Model output: '{outputName}'");

            if (_needsScale)
                ConsoleLogger.Log($"[EmbeddingService] Quantized model detected, will provide '{_scaleInputName}' = 1.0");
        }
#endif

        /// <summary>
        /// Extract a normalized embedding vector from a Texture2D.
        /// Must be called on the main thread.
        /// </summary>
        public float[] ExtractEmbedding(Texture2D texture)
        {
            if (!_isLoaded || texture == null) return null;

#if SENTIS_AVAILABLE
            TensorFloat inputTensor = null;
            TensorFloat scaleTensor = null;
            try
            {
                inputTensor = PreprocessTexture(texture);

                _worker.SetInput(_pixelInputName, inputTensor);
                if (_needsScale)
                {
                    scaleTensor = new TensorFloat(new TensorShape(1), new float[] { 1.0f });
                    _worker.SetInput(_scaleInputName, scaleTensor);
                }
                _worker.Execute();

                var outputTensor = _worker.PeekOutput() as TensorFloat;
                if (outputTensor == null) return null;

                outputTensor.MakeReadable();
                var raw = outputTensor.ToReadOnlyArray();

                var result = new float[raw.Length];
                Array.Copy(raw, result, raw.Length);

                L2Normalize(result);
                return result;
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[EmbeddingService] Inference failed: {e.Message}");
                return null;
            }
            finally
            {
                inputTensor?.Dispose();
                scaleTensor?.Dispose();
            }
#else
            return null;
#endif
        }

        /// <summary>
        /// Extract embeddings from multiple textures, batched for reduced overhead.
        /// </summary>
        public float[][] ExtractEmbeddingBatch(Texture2D[] textures, int batchSize = 8)
        {
            if (!_isLoaded || textures == null) return null;

            var results = new float[textures.Length][];
            for (int i = 0; i < textures.Length; i++)
            {
                results[i] = ExtractEmbedding(textures[i]);
            }
            return results;
        }

        /// <summary>
        /// Extract embedding from raw image bytes (PNG/JPG).
        /// Must be called on the main thread.
        /// </summary>
        public float[] ExtractEmbeddingFromBytes(byte[] imageBytes)
        {
            if (!_isLoaded || imageBytes == null || imageBytes.Length == 0) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(imageBytes)) return null;
                return ExtractEmbedding(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

#if SENTIS_AVAILABLE
        /// <summary>
        /// Preprocess: resize to 224x224, normalize with CLIP mean/std,
        /// produce TensorFloat of shape [1, 3, 224, 224].
        /// </summary>
        private TensorFloat PreprocessTexture(Texture2D source)
        {
            const int size = 224;

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var resized = new Texture2D(size, size, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            resized.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = resized.GetPixels32();
            UnityEngine.Object.DestroyImmediate(resized);

            // CLIP normalization constants
            float[] mean = { 0.48145466f, 0.4578275f, 0.40821073f };
            float[] std = { 0.26862954f, 0.26130258f, 0.27577711f };

            var data = new float[1 * 3 * size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int srcIdx = (size - 1 - y) * size + x;
                    var c = pixels[srcIdx];
                    float r = (c.r / 255f - mean[0]) / std[0];
                    float g = (c.g / 255f - mean[1]) / std[1];
                    float b = (c.b / 255f - mean[2]) / std[2];

                    data[0 * size * size + y * size + x] = r;
                    data[1 * size * size + y * size + x] = g;
                    data[2 * size * size + y * size + x] = b;
                }
            }

            return new TensorFloat(new TensorShape(1, 3, size, size), data);
        }
#endif

        private static void L2Normalize(float[] vector)
        {
            float sumSq = 0f;
            for (int i = 0; i < vector.Length; i++)
                sumSq += vector[i] * vector[i];

            if (sumSq < 1e-12f) return;

            float invNorm = 1f / (float)Math.Sqrt(sumSq);
            for (int i = 0; i < vector.Length; i++)
                vector[i] *= invNorm;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

#if SENTIS_AVAILABLE
            _worker?.Dispose();
            _worker = null;
            _model = null;

            _textWorker?.Dispose();
            _textWorker = null;
            _textModel = null;
#endif
            _isLoaded = false;
            _textModelLoaded = false;
        }
    }
}
