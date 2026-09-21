using System;

namespace WeldingTrainer.FusionDemo
{
    /// <summary>Direction-independent, fixed-resolution coverage in workpiece metres.</summary>
    public sealed class SeamCoverage
    {
        private readonly bool[] occupied;
        public int Count => occupied.Length;
        public int CoveredCount { get; private set; }
        public float Length { get; }
        public float CellLength => Length / Count;
        public float Fraction => (float)CoveredCount / Count;
        public bool this[int index] => occupied[index];

        public SeamCoverage(float length, int count)
        {
            if (length <= 0 || float.IsNaN(length) || float.IsInfinity(length))
                throw new ArgumentOutOfRangeException(nameof(length));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            Length = length;
            occupied = new bool[count];
        }

        public int IndexAt(float metres) => Math.Max(0, Math.Min(Count - 1,
            (int)Math.Floor(metres / CellLength)));

        public void Deposit(float from, float to, Action<int, bool> touch)
        {
            int first = IndexAt(Math.Min(from, to));
            int last = IndexAt(Math.Max(from, to));
            for (int i = first; i <= last; i++)
            {
                bool fresh = !occupied[i];
                if (fresh) { occupied[i] = true; CoveredCount++; }
                touch(i, fresh);
            }
        }

        public void Clear()
        {
            Array.Clear(occupied, 0, Count);
            CoveredCount = 0;
        }
    }
}
