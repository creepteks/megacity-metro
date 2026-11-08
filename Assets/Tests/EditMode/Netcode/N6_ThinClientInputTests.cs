using NUnit.Framework;
using Unity.MegacityMetro.Gameplay;
using Unity.Mathematics;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue N6: thin-client bots must drive about 40 % of the time, shoot about 30 % of the time and steer
    /// with the same look-velocity scale as real players (±100), otherwise load tests under-exercise the server.
    /// </summary>
    [TestFixture]
    [Category("Netcode")]
    public class N6_ThinClientInputTests
    {
        private const int Samples = 20000;

        private static (float accel, float shoot, float maxLook) Sample()
        {
            var rand = new Random(12345u);
            int accelerating = 0, shooting = 0;
            float maxLook = 0f;
            for (int i = 0; i < Samples; i++)
            {
                var input = new PlayerVehicleInput();
                MoveThinClientInputSystem.RandomizeInput(ref rand, ref input);
                if (input.Acceleration > 0f) accelerating++;
                if (input.Shoot) shooting++;
                maxLook = math.max(maxLook, math.cmax(math.abs(input.LookVelocity)));
            }
            return ((float) accelerating / Samples, (float) shooting / Samples, maxLook);
        }

        [Test]
        public void RandomizeInput_RollIsInUnitRange()
        {
            var rand = new Random(7u);
            for (int i = 0; i < 1000; i++)
            {
                var input = new PlayerVehicleInput();
                MoveThinClientInputSystem.RandomizeInput(ref rand, ref input);
                Assert.That(input.Roll, Is.InRange(-1f, 1f));
            }
        }

        [Test]
        [Ignore("Known open issue N6: NextInt() < 0.4f is a full-range int compared to a fraction (~50 %). Remove Ignore when fixed with NextFloat().")]
        public void RandomizeInput_AccelerationProbabilityIsAbout40Percent()
        {
            var (accel, _, _) = Sample();
            Assert.That(accel, Is.InRange(0.35f, 0.45f));
        }

        [Test]
        [Ignore("Known open issue N6: NextInt() < 0.3f is ~50 %. Remove Ignore when fixed with NextFloat().")]
        public void RandomizeInput_ShootProbabilityIsAbout30Percent()
        {
            var (_, shoot, _) = Sample();
            Assert.That(shoot, Is.InRange(0.25f, 0.35f));
        }

        [Test]
        [Ignore("Known open issue N6: LookVelocity is drawn in [-1, 1] while real input is scaled by 100. Remove Ignore when fixed.")]
        public void RandomizeInput_LookVelocityUsesPlayerScale()
        {
            var (_, _, maxLook) = Sample();
            Assert.That(maxLook, Is.GreaterThan(50f), "Bots should steer with the same ±100 look-velocity range as keyboard/stick input.");
        }
    }
}
