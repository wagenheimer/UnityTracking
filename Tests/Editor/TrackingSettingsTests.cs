using NUnit.Framework;
using UnityEngine;

namespace Wagenheimer.Tracking.Tests
{
    public sealed class TrackingSettingsTests
    {
        [Test]
        public void DefaultsAreSafe()
        {
            var settings = TrackingSettings.CreateDefaults();

            try
            {
                Assert.IsTrue(settings.Enabled);
                Assert.AreEqual(TrackingDeclarationMode.ScriptingDefine, settings.DeclarationMode);
                Assert.AreEqual(TrackingSettings.DefaultDefine, settings.ScriptingDefine);
                Assert.AreEqual(TrackingRequirementMode.Auto, settings.RequirementMode);
                Assert.IsTrue(settings.HasUsablePurposeString);
                Assert.IsTrue(settings.FailBuildOnInvalidConfiguration);
                Assert.GreaterOrEqual(settings.InitialDelaySeconds, 0f);
                Assert.GreaterOrEqual(settings.TimeoutSeconds, 1f);
                Assert.That(settings.MobileScale, Is.InRange(0.75f, 3f));
                Assert.That(settings.DesktopScale, Is.InRange(0.75f, 3f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void EmptyHistoryPrefixFallsBack()
        {
            var settings = TrackingSettings.CreateDefaults();

            try
            {
                Assert.IsFalse(string.IsNullOrEmpty(settings.HistoryKeyPrefix));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
