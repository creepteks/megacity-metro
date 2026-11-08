using NUnit.Framework;
using Unity.MegacityMetro.Gameplay;

namespace Unity.MegacityMetro.Tests
{
    /// <summary>
    /// Known issue G3: the laser must stop firing (and therefore stop dealing server damage) at zero energy,
    /// matching what every client renders.
    /// </summary>
    [TestFixture]
    [Category("Gameplay")]
    public class G3_LaserEnergyTests
    {
        [Test]
        public void DeadShip_CannotShoot()
        {
            Assert.That(LaserJob.EvaluateIsShooting(true, 100f, ownerIsDead: 1), Is.False);
        }

        [Test]
        public void NoInput_DoesNotShoot()
        {
            Assert.That(LaserJob.EvaluateIsShooting(false, 100f, ownerIsDead: 0), Is.False);
        }

        [Test]
        public void LivingShipWithEnergy_Shoots()
        {
            Assert.That(LaserJob.EvaluateIsShooting(true, 50f, ownerIsDead: 0), Is.True);
        }

        [Test]
        [Ignore("Known open issue G3: 'Energy > 0 || IsDead == 0' is always true for a living ship, so damage continues at zero energy while the beam is hidden. Remove Ignore when the gate is Energy > 0.")]
        public void LivingShipWithoutEnergy_DoesNotShoot()
        {
            Assert.That(LaserJob.EvaluateIsShooting(true, 0f, ownerIsDead: 0), Is.False);
        }
    }
}
