using System;
using UnityEngine;

namespace WaterSlides
{
    [Serializable]
    public struct FloatRange
    {
        public float Min;
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
        public int Min;
        public int Max;

        public int Sample(System.Random rng)
        {
            if (Max < Min)
                (Min, Max) = (Max, Min);
            return rng.Next(Min, Max + 1);
        }
    }
}
