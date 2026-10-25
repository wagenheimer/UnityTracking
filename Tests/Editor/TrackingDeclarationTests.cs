using NUnit.Framework;

namespace Wagenheimer.Tracking.Tests
{
    public sealed class TrackingDeclarationTests
    {
        [Test]
        public void NeverDoesNotDeclare()
        {
            Assert.IsFalse(TrackingDeclaration.DeclaresTracking(
                true, TrackingDeclarationMode.Never, definePresent: true));
        }

        [Test]
        public void AlwaysDeclaresEvenWithoutTheDefine()
        {
            Assert.IsTrue(TrackingDeclaration.DeclaresTracking(
                true, TrackingDeclarationMode.Always, definePresent: false));
        }

        [Test]
        public void ScriptingDefineMirrorsTheDefine()
        {
            Assert.IsTrue(TrackingDeclaration.DeclaresTracking(
                true, TrackingDeclarationMode.ScriptingDefine, definePresent: true));
            Assert.IsFalse(TrackingDeclaration.DeclaresTracking(
                true, TrackingDeclarationMode.ScriptingDefine, definePresent: false));
        }

        [Test]
        public void DisabledMasterSwitchNeverDeclares()
        {
            Assert.IsFalse(TrackingDeclaration.DeclaresTracking(
                false, TrackingDeclarationMode.Always, definePresent: true));
        }

        [Test]
        public void WritesOnlyWhenDeclaringAndEnabled()
        {
            Assert.IsTrue(TrackingDeclaration.ShouldWritePurposeString(
                true, TrackingDeclarationMode.Always, true, writePurposeString: true));
            Assert.IsFalse(TrackingDeclaration.ShouldWritePurposeString(
                true, TrackingDeclarationMode.Always, true, writePurposeString: false));
            Assert.IsFalse(TrackingDeclaration.ShouldWritePurposeString(
                true, TrackingDeclarationMode.Never, true, writePurposeString: true));
        }

        [Test]
        public void RemovesOnlyWhenNotDeclaringAndEnabled()
        {
            Assert.IsTrue(TrackingDeclaration.ShouldRemovePurposeString(
                true, TrackingDeclarationMode.Never, true, removeWhenNotDeclared: true));
            Assert.IsFalse(TrackingDeclaration.ShouldRemovePurposeString(
                true, TrackingDeclarationMode.Always, true, removeWhenNotDeclared: true));
            Assert.IsFalse(TrackingDeclaration.ShouldRemovePurposeString(
                true, TrackingDeclarationMode.Never, true, removeWhenNotDeclared: false));
        }

        [Test]
        public void DescribeMentionsTheDefineState()
        {
            string withDefine = TrackingDeclaration.Describe(
                true, TrackingDeclarationMode.ScriptingDefine, "FTDOTD_TRACKING", true);
            string withoutDefine = TrackingDeclaration.Describe(
                true, TrackingDeclarationMode.ScriptingDefine, "FTDOTD_TRACKING", false);

            StringAssert.Contains("Declares tracking", withDefine);
            StringAssert.Contains("Does not declare", withoutDefine);
        }
    }
}
