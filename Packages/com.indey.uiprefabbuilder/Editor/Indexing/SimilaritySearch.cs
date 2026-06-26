using System;
using System.Numerics;

namespace Indey.UIPrefabBuilder.Indexing
{
    public static class SimilaritySearch
    {
        /// <summary>
        /// Find the top-K most similar vectors in the matrix to the query vector.
        /// Both query and matrix vectors must be L2-normalized (cosine similarity = dot product).
        /// Matrix is stored as a flat array: N vectors of 'dimension' floats each.
        /// </summary>
        public static (int[] indices, float[] scores) TopK(
            float[] query, float[] matrix, int vectorCount, int dimension, int topK)
        {
            if (query == null || matrix == null || vectorCount == 0)
                return (Array.Empty<int>(), Array.Empty<float>());

            topK = Math.Min(topK, vectorCount);

            var heapIndices = new int[topK];
            var heapScores = new float[topK];
            for (int i = 0; i < topK; i++)
            {
                heapIndices[i] = -1;
                heapScores[i] = float.MinValue;
            }

            int heapSize = 0;

            for (int i = 0; i < vectorCount; i++)
            {
                int offset = i * dimension;
                if (offset + dimension > matrix.Length) break;

                float score = DotProductSimd(query, 0, matrix, offset, dimension);

                if (heapSize < topK)
                {
                    heapIndices[heapSize] = i;
                    heapScores[heapSize] = score;
                    heapSize++;
                    if (heapSize == topK)
                        BuildMinHeap(heapIndices, heapScores, topK);
                }
                else if (score > heapScores[0])
                {
                    heapIndices[0] = i;
                    heapScores[0] = score;
                    SiftDown(heapIndices, heapScores, 0, topK);
                }
            }

            SortHeapDescending(heapIndices, heapScores, heapSize);

            if (heapSize < topK)
            {
                Array.Resize(ref heapIndices, heapSize);
                Array.Resize(ref heapScores, heapSize);
            }

            return (heapIndices, heapScores);
        }

        private static float DotProductSimd(float[] a, int offsetA, float[] b, int offsetB, int length)
        {
            float sum = 0f;
            int i = 0;

            int simdWidth = System.Numerics.Vector<float>.Count;
            int simdEnd = length - (length % simdWidth);

            for (; i < simdEnd; i += simdWidth)
            {
                var va = new System.Numerics.Vector<float>(a, offsetA + i);
                var vb = new System.Numerics.Vector<float>(b, offsetB + i);
                sum += System.Numerics.Vector.Dot(va, vb);
            }

            for (; i < length; i++)
                sum += a[offsetA + i] * b[offsetB + i];

            return sum;
        }

        #region Min-Heap utilities

        private static void BuildMinHeap(int[] indices, float[] scores, int size)
        {
            for (int i = size / 2 - 1; i >= 0; i--)
                SiftDown(indices, scores, i, size);
        }

        private static void SiftDown(int[] indices, float[] scores, int i, int size)
        {
            while (true)
            {
                int smallest = i;
                int left = 2 * i + 1;
                int right = 2 * i + 2;

                if (left < size && scores[left] < scores[smallest])
                    smallest = left;
                if (right < size && scores[right] < scores[smallest])
                    smallest = right;

                if (smallest == i) break;

                (indices[i], indices[smallest]) = (indices[smallest], indices[i]);
                (scores[i], scores[smallest]) = (scores[smallest], scores[i]);
                i = smallest;
            }
        }

        private static void SortHeapDescending(int[] indices, float[] scores, int size)
        {
            Array.Sort(scores, indices, 0, size);
            Array.Reverse(scores, 0, size);
            Array.Reverse(indices, 0, size);
        }

        #endregion
    }
}
