using NUnit.Framework;
using Wagenheimer.Tracking.Editor;

namespace Wagenheimer.Tracking.Tests
{
    public sealed class TrackingBuildProfilesTests
    {
        [Test]
        public void AppliedDefineIsFound()
        {
            Assert.IsTrue(TrackingBuildProfiles.IsApplied(
                new[] { "FTDOTD_MONETIZATION", "FTDOTD_TRACKING" }, "FTDOTD_TRACKING"));
        }

        [Test]
        public void MissingDefineIsNotApplied()
        {
            Assert.IsFalse(TrackingBuildProfiles.IsApplied(
                new[] { "FTDOTD_MONETIZATION" }, "FTDOTD_TRACKING"));
        }

        [Test]
        public void RemovalWins()
        {
            Assert.IsFalse(TrackingBuildProfiles.IsApplied(
                new[] { "FTDOTD_MONETIZATION", "!FTDOTD_MONETIZATION" }, "FTDOTD_MONETIZATION"));

            Assert.IsFalse(TrackingBuildProfiles.IsApplied(
                new[] { "-FTDOTD_TRACKING" }, "FTDOTD_TRACKING"));
        }

        [Test]
        public void MatchingIsCaseInsensitiveAndTrimmed()
        {
            Assert.IsTrue(TrackingBuildProfiles.IsApplied(
                new[] { "  ftdotd_tracking  " }, "FTDOTD_TRACKING"));
        }

        [Test]
        public void EmptyInputsAreSafe()
        {
            Assert.IsFalse(TrackingBuildProfiles.IsApplied(null, "FTDOTD_TRACKING"));
            Assert.IsFalse(TrackingBuildProfiles.IsApplied(new string[0], "FTDOTD_TRACKING"));
            Assert.IsFalse(TrackingBuildProfiles.IsApplied(new[] { "FTDOTD_TRACKING" }, "  "));
        }
    }
}
