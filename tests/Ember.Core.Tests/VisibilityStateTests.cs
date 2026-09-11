using NUnit.Framework;

namespace Ember.Core.Tests
{
    [TestFixture]
    public class VisibilityStateTests
    {
        [Test]
        public void InView_DefaultIsFalse()
        {
            Assert.That(new VisibilityState().InView, Is.False);
        }

        [Test]
        public void InView_SetTrue_RaisesBit()
        {
            var state = new VisibilityState { InView = true };

            Assert.That(state.InView, Is.True);
            Assert.That(state.Flags, Is.EqualTo(VisibilityState.InViewBit));
        }

        [Test]
        public void InView_Toggle_PreservesOtherBits()
        {
            var state = new VisibilityState { Flags = 0b1010 };

            state.InView = true;
            Assert.That(state.Flags, Is.EqualTo(0b1011));

            state.InView = false;
            Assert.That(state.Flags, Is.EqualTo(0b1010));
        }
    }
}
