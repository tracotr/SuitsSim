using NUnit.Framework;
using SuitsSim.Core;
using UnityEngine;

namespace SuitsSim.Tests
{
    public class WorldFrameTests
    {
        static readonly Vector2 Hab = new Vector2(-5670f, -10060f);
        static readonly Vector2 BoardAlpha = new Vector2(-5635f, -9970f);
        const float Eps = 1e-3f;

        [Test]
        public void NoYaw_MapUpIsUnityForward()
        {
            var f = new WorldFrame(Hab, 0f);
            var u = f.ToUnity(BoardAlpha);
            Assert.AreEqual(35f, u.x, Eps);
            Assert.AreEqual(90f, u.z, Eps);
        }

        [TestCase(0f)]
        [TestCase(37.5f)]
        [TestCase(-120f)]
        public void RoundTrip(float yaw)
        {
            var f = new WorldFrame(Hab, yaw);
            var back = f.ToTss(f.ToUnity(BoardAlpha, 3f));
            Assert.AreEqual(BoardAlpha.x, back.x, Eps);
            Assert.AreEqual(BoardAlpha.y, back.y, Eps);
        }

        [Test]
        public void Heading_ClockwiseFromMapUp()
        {
            var f = new WorldFrame(Hab, 0f);
            Assert.AreEqual(0f, f.HeadingFromForward(Vector3.forward), Eps);
            Assert.AreEqual(90f, f.HeadingFromForward(Vector3.right), Eps);
            Assert.AreEqual(270f, f.HeadingFromForward(Vector3.left), Eps);
        }

        [Test]
        public void Heading_AccountsForWorldYaw()
        {
            // World rotated 90 deg: map up now points along Unity +X.
            var f = new WorldFrame(Hab, 90f);
            Assert.AreEqual(0f, f.HeadingFromForward(Vector3.right), Eps);
        }

        [Test]
        public void Heading_OffsetAndCounterClockwise()
        {
            var f = new WorldFrame(Hab, 0f, headingOffsetDegrees: 10f, headingClockwise: false);
            Assert.AreEqual(280f, f.HeadingFromForward(Vector3.right), Eps);
        }

        [TestCase(10f, 350f, 20f)]
        [TestCase(350f, 10f, -20f)]
        [TestCase(180f, 0f, 180f)]
        public void DeltaDegrees_TakesShortWay(float a, float b, float expected)
        {
            Assert.AreEqual(expected, WorldFrame.DeltaDegrees(a, b), Eps);
        }
    }
}
