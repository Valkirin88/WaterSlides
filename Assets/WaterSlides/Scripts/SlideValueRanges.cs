using System;
using UnityEngine;

namespace WaterSlides
{
    [Serializable]
    public struct FloatRange
    {
        [Tooltip("Нижняя граница диапазона. Случайное значение берётся равномерно между Min и Max.")]
        public float Min;
        [Tooltip("Верхняя граница диапазона. Случайное значение берётся равномерно между Min и Max.")]
        public float Max;

        public float Sample(System.Random rng)
        {
            if (Max < Min)
                (Min, Max) = (Max, Min);
            return Min + (float)rng.NextDouble() * (Max - Min);
        }
    }

    [Serializable]
    public struct IntRange
    {
        [Tooltip("Нижняя граница целочисленного диапазона (включительно). Случайное значение берётся между Min и Max.")]
        public int Min;
        [Tooltip("Верхняя граница целочисленного диапазона (включительно). Случайное значение берётся между Min и Max.")]
        public int Max;

        public int Sample(System.Random rng)
        {
            if (Max < Min)
                (Min, Max) = (Max, Min);
            return rng.Next(Min, Max + 1);
        }
    }
}
