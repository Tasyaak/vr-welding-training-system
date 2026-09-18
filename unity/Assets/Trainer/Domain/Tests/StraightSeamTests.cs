using NUnit.Framework;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Domain.Tests
{
    public sealed class StraightSeamTests
    {
        private static StraightSeam HorizontalSeam() =>
            new(new Vector3(0, 0, 0), new Vector3(1, 0, 0));

        [Test]
        public void ZeroLengthSeamThrows()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new StraightSeam(Vector3.zero, Vector3.zero));
        }

        [Test]
        public void MidpointProjectsToHalfProgress()
        {
            var seam = HorizontalSeam();
            var proj = seam.Project(new Vector3(0.5f, 0, 0));
            Assert.AreEqual(0.5f, proj.Progress, 1e-6f);
            Assert.AreEqual(0f, proj.DistanceMetres, 1e-6f);
        }

        [Test]
        public void PointBeyondEndClampsProgressToOne()
        {
            var seam = HorizontalSeam();
            var proj = seam.Project(new Vector3(2f, 0, 0));
            Assert.AreEqual(1f, proj.Progress, 1e-6f);
            Assert.AreEqual(1f, proj.DistanceMetres, 1e-6f);
        }

        [Test]
        public void PointBeforeStartClampsProgressToZero()
        {
            var seam = HorizontalSeam();
            var proj = seam.Project(new Vector3(-1f, 0, 0));
            Assert.AreEqual(0f, proj.Progress, 1e-6f);
            Assert.AreEqual(1f, proj.DistanceMetres, 1e-6f);
        }

        [Test]
        public void PerpendicularOffsetReturnsCorrectDistance()
        {
            var seam = HorizontalSeam();
            var proj = seam.Project(new Vector3(0.5f, 0.03f, 0));
            Assert.AreEqual(0.5f, proj.Progress, 1e-5f);
            Assert.AreEqual(0.03f, proj.DistanceMetres, 1e-5f);
        }

        [Test]
        public void UnclampedProgressIsNegativeBeforeStart()
        {
            var seam = HorizontalSeam();
            var proj = seam.Project(new Vector3(-0.5f, 0, 0));
            Assert.Less(proj.UnclampedProgress, 0f);
        }

        [Test]
        public void DiagonalSeamLengthIsCorrect()
        {
            var seam = new StraightSeam(Vector3.zero, new Vector3(3, 4, 0));
            Assert.AreEqual(5f, seam.LengthMetres, 1e-5f);
        }
    }
}
