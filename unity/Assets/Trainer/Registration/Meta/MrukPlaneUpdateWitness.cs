using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeldingTrainer.Registration.Meta
{
    // MRUK 205 SetPlane clears/refills its public boundary list on EVERY native update.
    // List enumerators are invalidated by mutation, including identical values and Clear of an empty list.
    // This witnesses an SDK update without reflection, pose perturbation or fabricated camera timestamps.
    public sealed class MrukPlaneUpdateWitness
    {
        private List<Vector2> boundary;
        private List<Vector2>.Enumerator cursor;
        public MrukPlaneUpdateWitness(List<Vector2> initial)
        {
            Reset(initial);
        }

        private void Reset(List<Vector2> value)
        {
            cursor.Dispose();
            boundary = value;
            cursor = value == null ? default : value.GetEnumerator();
        }

        public bool Consume(List<Vector2> current)
        {
            if (!ReferenceEquals(boundary, current))
            {
                Reset(current);
                return current != null;
            }

            if (current == null)
                return false;
            try
            {
                cursor.MoveNext();
                return false;
            }
            catch (InvalidOperationException)
            {
                Reset(current);
                return true;
            }
        }
    }
}
