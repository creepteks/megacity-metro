using NUnit.Framework;
using Unity.MegacityMetro.Gameplay;
using Unity.Mathematics;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue F3 (fixed by the flight profile change set): mouse deltas are accumulated over the frames of one
    /// tick, so the command carries the whole displacement regardless of frame rate.
    /// </summary>
    [TestFixture]
    [Category("Flight")]
    public class F3_InputSamplingTests
    {
        private const float TickDt = 1f / 30f;

        [Test]
        public void MouseLookVelocity_TwoFramesIntegrateToTheSameDisplacementAsOne()
        {
            // One frame of 1/30 s with a 10 px delta vs two frames of 1/60 s with 5 px each
            float2 single = PlayerVehicleInputSystemBase.MouseLookVelocity(new float2(10f, 0f), TickDt, 1f, 1f, 1f);
            float2 accumulated = PlayerVehicleInputSystemBase.MouseLookVelocity(new float2(5f, 0f) + new float2(5f, 0f), TickDt / 2f + TickDt / 2f, 1f, 1f, 1f);

            Assert.That(accumulated.y, Is.EqualTo(single.y).Within(1e-4f));
            Assert.That((accumulated * TickDt).y, Is.EqualTo(10f).Within(1e-4f), "velocity * tickDt must equal the accumulated displacement");
        }

        [Test]
        public void MouseLookVelocity_MapsAxesAndInversion()
        {
            // x = pitch from -deltaY, y = yaw from +deltaX
            float2 look = PlayerVehicleInputSystemBase.MouseLookVelocity(new float2(3f, 4f), 1f, 1f, 1f, 1f);
            Assert.That(look.x, Is.EqualTo(-4f).Within(1e-5f));
            Assert.That(look.y, Is.EqualTo(3f).Within(1e-5f));

            float2 inverted = PlayerVehicleInputSystemBase.MouseLookVelocity(new float2(3f, 4f), 1f, -1f, -1f, 1f);
            Assert.That(inverted.x, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(inverted.y, Is.EqualTo(-3f).Within(1e-5f));
        }

        [Test]
        public void MouseLookVelocity_ZeroTimeYieldsZero()
        {
            Assert.That(PlayerVehicleInputSystemBase.MouseLookVelocity(new float2(10f, 10f), 0f, 1f, 1f, 1f), Is.EqualTo(float2.zero));
        }

        [Test]
        public void ShapeStick_LinearWithoutDeadzoneIsIdentity()
        {
            var v = new float2(0.3f, -0.4f);
            var shaped = PlayerVehicleInputSystemBase.ShapeStick(v, 0f, 1f);
            Assert.That(math.distance(shaped, v), Is.LessThan(1e-5f));
        }

        [Test]
        public void ShapeStick_DeadzoneZeroesSmallInputsAndRescales()
        {
            Assert.That(PlayerVehicleInputSystemBase.ShapeStick(new float2(0.05f, 0f), 0.1f, 1f), Is.EqualTo(float2.zero));
            var full = PlayerVehicleInputSystemBase.ShapeStick(new float2(1f, 0f), 0.1f, 1f);
            Assert.That(full.x, Is.EqualTo(1f).Within(1e-5f), "full deflection must still reach 1 after deadzone rescale");
        }

        [Test]
        public void ShapeStick_ExponentCurvesTheResponse()
        {
            var half = PlayerVehicleInputSystemBase.ShapeStick(new float2(0.5f, 0f), 0f, 2f);
            Assert.That(half.x, Is.EqualTo(0.25f).Within(1e-5f));
        }
    }
}
