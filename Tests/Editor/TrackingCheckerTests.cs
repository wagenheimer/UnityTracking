using NUnit.Framework;

namespace Wagenheimer.Tracking.Tests
{
    public sealed class TrackingCheckerTests
    {
        [Test]
        public void EmptyPurposeStringIsRejected()
        {
            Assert.IsNotNull(Wagenheimer.Tracking.Editor.TrackingChecker.ValidatePurposeString(null));
            Assert.IsNotNull(Wagenheimer.Tracking.Editor.TrackingChecker.ValidatePurposeString("   "));
        }

        [Test]
        public void ShortPurposeStringIsFlagged()
        {
            Assert.IsNotNull(Wagenheimer.Tracking.Editor.TrackingChecker.ValidatePurposeString("Ads"));
        }

        [Test]
        public void PlaceholderIsFlagged()
        {
            Assert.IsNotNull(Wagenheimer.Tracking.Editor.TrackingChecker.ValidatePurposeString(
                "TODO write the real purpose string for this app before shipping"));
        }

        [Test]
        public void DefaultPurposeStringIsAccepted()
        {
            Assert.IsNull(Wagenheimer.Tracking.Editor.TrackingChecker.ValidatePurposeString(
                Wagenheimer.Tracking.TrackingSettings.DefaultPurposeString));
        }

        [Test]
        public void DefineLookupIsExact()
        {
            Assert.IsTrue(Wagenheimer.Tracking.Editor.TrackingChecker.HasDefine("A;B;C", "B"));
            Assert.IsFalse(Wagenheimer.Tracking.Editor.TrackingChecker.HasDefine("A;BB;C", "B"));
            Assert.IsFalse(Wagenheimer.Tracking.Editor.TrackingChecker.HasDefine("", "B"));
        }
    }
}

