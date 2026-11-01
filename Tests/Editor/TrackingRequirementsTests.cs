using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Wagenheimer.Tracking.Tests
{
    public sealed class TrackingRequirementsTests
    {
        [TearDown]
        public void TearDown()
        {
            Wagenheimer.Tracking.Editor.TrackingRequirements.ClearProvider();
        }

        [Test]
        public void NoProviderReturnsNothing()
        {
            Assert.IsFalse(Wagenheimer.Tracking.Editor.TrackingRequirements.HasProvider);
            Assert.IsEmpty(Wagenheimer.Tracking.Editor.TrackingRequirements.Evaluate());
        }

        [Test]
        public void ProviderItemsAreEvaluated()
        {
            Wagenheimer.Tracking.Editor.TrackingRequirements.SetProvider(() => new[]
            {
                new Wagenheimer.Tracking.Editor.TrackingRequirement(
                    "iOS Free declares tracking",
                    Wagenheimer.Tracking.Editor.TrackingRequirementStatus.Pass,
                    "FTDOTD_MONETIZATION + FTDOTD_TRACKING")
            });

            var items = Wagenheimer.Tracking.Editor.TrackingRequirements.Evaluate();

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("iOS Free declares tracking", items[0].Title);
            Assert.AreEqual(Wagenheimer.Tracking.Editor.TrackingRequirementStatus.Pass, items[0].Status);
        }

        [Test]
        public void ThrowingProviderBecomesAFailure()
        {
            Wagenheimer.Tracking.Editor.TrackingRequirements.SetProvider(
                () => throw new InvalidOperationException("boom"));

            var items = Wagenheimer.Tracking.Editor.TrackingRequirements.Evaluate();

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(Wagenheimer.Tracking.Editor.TrackingRequirementStatus.Fail, items[0].Status);
            StringAssert.Contains("boom", items[0].Detail);
        }

        [Test]
        public void NullItemsAreIgnored()
        {
            Wagenheimer.Tracking.Editor.TrackingRequirements.SetProvider(
                () => new List<Wagenheimer.Tracking.Editor.TrackingRequirement> { null });

            Assert.IsEmpty(Wagenheimer.Tracking.Editor.TrackingRequirements.Evaluate());
        }
    }
}
