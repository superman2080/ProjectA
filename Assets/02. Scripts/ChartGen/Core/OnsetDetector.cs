using System;
using System.Collections.Generic;

namespace ChartGen
{
    /// <summary>온셋 하나 — 시각과 세기(RMS 증가량). 솎아내기(<see cref="OnsetThinner"/>)가 세기로 버릴 순서를 정한다.</summary>
    public readonly struct Onset
    {
        public float Time { get; }
        public float Strength { get; }

        public Onset(float time, float strength)
        {
            Time = time;
            Strength = strength;
        }
    }

    /// <summary>음원 샘플에서 RMS(진폭 에너지) 급증 지점을 온셋(입력 판정 시각 후보)으로 감지한다. Unity API 비의존.</summary>
    public static class OnsetDetector
    {
        public static float[] Detect(float[] samples, int sampleRate, int windowSize, float rmsThreshold)
        {
            var onsets = DetectWithStrength(samples, sampleRate, windowSize, rmsThreshold);
            var times = new float[onsets.Length];
            for (int i = 0; i < onsets.Length; i++) times[i] = onsets[i].Time;
            return times;
        }

        /// <summary><see cref="Detect"/>와 같은 감지를 하되, 세기(= 직전 창 대비 RMS 증가량)를 함께 돌려준다.</summary>
        public static Onset[] DetectWithStrength(float[] samples, int sampleRate, int windowSize, float rmsThreshold)
        {
            var onsets = new List<Onset>();
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

                float rise = rms - previousRms;
                if (w > 0 && rise >= rmsThreshold)
                {
                    float onsetTime = (float)start / sampleRate;
                    onsets.Add(new Onset(onsetTime, rise));
                }

                previousRms = rms;
            }

            return onsets.ToArray();
        }
    }
}
