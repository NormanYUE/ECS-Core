using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    [TestFixture]
    public class GlobalRandomTests
    {
        [Test]
        public void GlobalRandom_SameSeed_ProducesSameSequence()
        {
            var a = new GlobalRandom(42).Value;
            var b = new Random(42);

            for (var i = 0; i < 8; i++)
                Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test]
        public void GlobalRandom_DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new GlobalRandom(1).Value;
            var b = new GlobalRandom(2).Value;

            Assert.That(a.NextUInt(), Is.Not.EqualTo(b.NextUInt()));
        }
    }
}
