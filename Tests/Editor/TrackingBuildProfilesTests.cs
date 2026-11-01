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

        [Test]
        public void MobileProfileNeedsAStableId()
        {
            Assert.IsTrue(TrackingBuildProfiles.NeedsStableId("", "iOS (Free)"));
            Assert.IsTrue(TrackingBuildProfiles.NeedsStableId("   ", "iOS (Free)"));
            Assert.IsFalse(TrackingBuildProfiles.NeedsStableId("iOSFree", "iOS (Free)"));
        }

        [Test]
        public void CliProfileIdIsReadFromBuildProfileOrPublisher()
        {
            Assert.IsTrue(TrackingBuildProfiles.TryGetCliProfileId(
                new[] { "Unity.exe", "-buildProfile", "iOSFree", "-platform", "iOS" }, out var id));
            Assert.AreEqual("iOSFree", id);

            Assert.IsTrue(TrackingBuildProfiles.TryGetCliProfileId(
                new[] { "Unity.exe", "-publisher", "iOSFree" }, out var publisherId));
            Assert.AreEqual("iOSFree", publisherId);

            Assert.IsFalse(TrackingBuildProfiles.TryGetCliProfileId(
                new[] { "Unity.exe", "-platform", "iOS" }, out _));
        }

        [Test]
        public void CliProfileIdIgnoresEmptyValues()
        {
            Assert.IsFalse(TrackingBuildProfiles.TryGetCliProfileId(
                new[] { "Unity.exe", "-buildProfile", "  " }, out _));
            Assert.IsFalse(TrackingBuildProfiles.TryGetCliProfileId(null, out _));
        }
    }
}
