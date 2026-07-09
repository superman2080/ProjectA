using System;
using System.Collections.Generic;

namespace ChartGen
{
    /// <summary>음원 샘플에서 RMS(진폭 에너지) 급증 지점을 온셋(입력 판정 시각 후보)으로 감지한다. Unity API 비의존.</summary>
    public static class OnsetDetector
    {
        public static float[] Detect(float[] samples, int sampleRate, int windowSize, float rmsThreshold)
        {
            var onsets = new List<float>();
            if (samples == null || samples.Length == 0 || windowSize <= 0 || sampleRate <= 0)
                return onsets.ToArray();

            int windowCount = samples.Length / windowSize;
            float previousRms = 0f;

            for (int w = 0; w < windowCount; w++)
            {
                int start = w * windowSize;
                float sumSquares = 0f;
                for (int i = 0; i < windowSize; i++)
                {
                    float s = samples[start + i];
                    sumSquares += s * s;
                }
                float rms = (float)Math.Sqrt(sumSquares / windowSize);

                if (w > 0 && rms - previousRms >= rmsThreshold)
                {
                    float onsetTime = (float)start / sampleRate;
                    onsets.Add(onsetTime);
                }

                previousRms = rms;
            }

            return onsets.ToArray();
        }
    }
}
