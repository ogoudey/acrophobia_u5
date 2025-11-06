using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace PCPSLib
{
    public class PCPS
    {
        private int N = 150;
        private float maxThreshold;
        private float[] increments;
        private Func<float, float> predictLuminanceFunc;

        public void SetThreshold(float thresh)
        {
            maxThreshold = thresh;
            UnityEngine.Debug.Log($"Set threshold {thresh}");
        }

        public void SetIncrements(float[] inc)
        {
            increments = inc;
            UnityEngine.Debug.Log($"Set increments [{string.Join(", ", inc)}]");

            // Equivalent of numpy.polyfit(x, y, 3)
            float[] x = Enumerable.Range(0, inc.Length)
                                   .Select(i => (float)i / (inc.Length - 1))
                                   .ToArray();
            float[] coeffs = PolyFit(x, inc, 3);

            // Build cubic polynomial function f(x) = c0*x^3 + c1*x^2 + c2*x + c3
            predictLuminanceFunc = (l) =>
                coeffs[0] * (float)Math.Pow(l, 3) +
                coeffs[1] * (float)Math.Pow(l, 2) +
                coeffs[2] * l +
                coeffs[3];
        }

        public float PredictPupilSizeAtLuminance(float luminance)
        {
            return predictLuminanceFunc(luminance);
        }

        public float[] PredictPupilSizes(float[] luminances)
        {
            return luminances.Select(l => PredictPupilSizeAtLuminance(l)).ToArray();
        }

        private float[] PreprocessPupil(float[] y)
        {
            // Replace out-of-band values with NaN
            float[] processed = y.Select(v => (v < 0.8 || v > 10) ? float.NaN : v).ToArray();

            // Interpolate NaNs linearly
            return InterpolateNaN(processed);
        }

        public int CalculateFear(float[] pupilLeft, float[] luminances)
        {
            float[] predictedPupilLeft = PredictPupilSizes(luminances);
            float[] cleanedPupilLeft = PreprocessPupil(pupilLeft);

            // Remove luminance effects
            float[] luminanceRemoved = cleanedPupilLeft
                .Zip(predictedPupilLeft, (a, b) => a - b)
                .ToArray();

            // Pad (edge mode)
            float[] yPadded = PadEdge(luminanceRemoved, N / 2, N - 1 - N / 2);

            // Moving average
            float[] movingAvg = Convolve(yPadded, Enumerable.Repeat(1.0f / N, N).ToArray());

            // Power and its moving average
            float[] power = movingAvg.Select(v => v * v).ToArray();
            int windowSize = N; // not specified explicitly in Python; assumed same as N
            float[] movingAvgPower = Convolve(power, Enumerable.Repeat(1.0f / windowSize, windowSize).ToArray());

            float peakPoint = movingAvgPower.Max();

            return (peakPoint > maxThreshold) ? 1 : 0;
        }

        // --- Utility functions below ---

        private static float[] Convolve(float[] signal, float[] kernel)
        {
            int n = signal.Length;
            int k = kernel.Length;
            int size = n - k + 1;
            float[] result = new float[size];

            for (int i = 0; i < size; i++)
            {
                float sum = 0;
                for (int j = 0; j < k; j++)
                    sum += signal[i + j] * kernel[j];
                result[i] = sum;
            }
            return result;
        }

        private static float[] PadEdge(float[] data, int padLeft, int padRight)
        {
            List<float> padded = new List<float>();
            float leftVal = data.First();
            float rightVal = data.Last();

            padded.AddRange(Enumerable.Repeat(leftVal, padLeft));
            padded.AddRange(data);
            padded.AddRange(Enumerable.Repeat(rightVal, padRight));

            return padded.ToArray();
        }

        private static float[] InterpolateNaN(float[] data)
        {
            float[] result = new float[data.Length];
            Array.Copy(data, result, data.Length);

            int n = data.Length;
            int lastValid = -1;
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(result[i]))
                {
                    // find next valid
                    int nextValid = i + 1;
                    while (nextValid < n && float.IsNaN(result[nextValid]))
                        nextValid++;

                    float startVal = lastValid >= 0 ? result[lastValid] : result[nextValid];
                    float endVal = nextValid < n ? result[nextValid] : startVal;

                    int gap = nextValid - (lastValid >= 0 ? lastValid : 0);
                    for (int j = (lastValid >= 0 ? lastValid + 1 : 0); j < nextValid; j++)
                    {
                        float t = (float)(j - (lastValid >= 0 ? lastValid : 0)) / (gap);
                        result[j] = startVal + t * (endVal - startVal);
                    }
                }
                else
                {
                    lastValid = i;
                }
            }
            return result;
        }

        // Cubic polynomial fit equivalent to numpy.polyfit(x, y, degree)
        private static float[] PolyFit(float[] x, float[] y, int degree)
        {
            int n = x.Length;
            int order = degree + 1;

            // Vandermonde matrix
            float[,] vandermonde = new float[n, order];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < order; j++)
                    vandermonde[i, j] = (float)Math.Pow(x[i], degree - j);

            // Solve least squares (V * coeffs = y)
            float[,] a = vandermonde;
            float[] b = y;
            float[] coeffs = SolveLeastSquares(a, b);
            return coeffs;
        }

        // Simple least squares via normal equation (A^T A x = A^T b)
        private static float[] SolveLeastSquares(float[,] A, float[] b)
        {
            int m = A.GetLength(0);
            int n = A.GetLength(1);

            float[,] At = new float[n, m];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < n; j++)
                    At[j, i] = A[i, j];

            float[,] AtA = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float sum = 0;
                    for (int k = 0; k < m; k++)
                        sum += At[i, k] * A[k, j];
                    AtA[i, j] = sum;
                }

            float[] Atb = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0;
                for (int k = 0; k < m; k++)
                    sum += At[i, k] * b[k];
                Atb[i] = sum;
            }

            return GaussianElimination(AtA, Atb);
        }

        private static float[] GaussianElimination(float[,] A, float[] b)
        {
            int n = b.Length;
            float[,] M = (float[,])A.Clone();
            float[] B = (float[])b.Clone();

            for (int i = 0; i < n; i++)
            {
                // Pivot
                int maxRow = i;
                for (int k = i + 1; k < n; k++)
                    if (Math.Abs(M[k, i]) > Math.Abs(M[maxRow, i]))
                        maxRow = k;

                // Swap
                for (int k = i; k < n; k++)
                {
                    float tmp = M[maxRow, k];
                    M[maxRow, k] = M[i, k];
                    M[i, k] = tmp;
                }
                float temp = B[maxRow];
                B[maxRow] = B[i];
                B[i] = temp;

                // Eliminate
                for (int k = i + 1; k < n; k++)
                {
                    float c = -M[k, i] / M[i, i];
                    for (int j = i; j < n; j++)
                    {
                        if (i == j)
                            M[k, j] = 0;
                        else
                            M[k, j] += c * M[i, j];
                    }
                    B[k] += c * B[i];
                }
            }

            // Back substitution
            float[] x = new float[n];
            for (int i = n - 1; i >= 0; i--)
            {
                float sum = B[i];
                for (int j = i + 1; j < n; j++)
                    sum -= M[i, j] * x[j];
                x[i] = sum / M[i, i];
            }
            return x;
        }
    }
}
