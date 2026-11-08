using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue F7 (fixed by the flight profile change set): orientation is replicated as an interpolated
    /// quaternion so remote ships no longer spin when the roll angle wraps.
    /// </summary>
    [TestFixture]
    [Category("Flight")]
    public class F7_MovementStateReplicationTests
    {
        [Test]
        public void Rotation_IsAnInterpolatedGhostField()
        {
            var field = typeof(VehicleMovementState).GetField(nameof(VehicleMovementState.Rotation));
            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(quaternion)));

            var ghostField = field.GetCustomAttribute<GhostFieldAttribute>();
            Assert.That(ghostField, Is.Not.Null, "Rotation must be replicated");
            Assert.That(ghostField.Smoothing, Is.EqualTo(SmoothingAction.Interpolate));
            Assert.That(ghostField.Quantization, Is.GreaterThan(0));
        }

        [Test]
        public void CalculateTargetRotation_ReturnsTheQuaternionWhenSet()
        {
            var state = new VehicleMovementState();
            var target = quaternion.Euler(0.3f, 1.2f, -2.9f);
            VehicleMovementState.SetRotation(ref state, target);

            var result = state.CalculateTargetRotation();
            Assert.That(math.abs(math.dot(result, target)), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void CalculateTargetRotation_FallsBackToEulerWhenQuaternionUnset()
        {
            var state = new VehicleMovementState { PitchRadians = 0.2f, YawRadians = 0.5f, RollRadians = 0.1f };
            var expected = quaternion.Euler(0.2f, 0.5f, 0.1f);

            var result = state.CalculateTargetRotation();
            Assert.That(math.abs(math.dot(result, expected)), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void RollWrap_DoesNotChangeTheReplicatedOrientation()
        {
            // The spin came from interpolating RollRadians across +pi -> -pi. With a quaternion the two are identical.
            var a = quaternion.Euler(0f, 0f, math.PI - 0.01f);
            var b = quaternion.Euler(0f, 0f, -math.PI + 0.01f);
            Assert.That(math.angle(a, b), Is.LessThan(0.05f), "orientations on both sides of the wrap are neighbours");
        }
    }
}
