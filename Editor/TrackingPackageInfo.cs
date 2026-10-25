using System.IO;
using UnityEditor.PackageManager;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>Package identity used by the checker and the menus.</summary>
    internal static class TrackingPackageInfo
    {
        public const string PackageName = "com.wagenheimer.tracking";
        public const string DisplayName = "Tracking Helper";
        public const string RepositoryUrl = "https://github.com/wagenheimer/UnityTracking";

        private static string packagePath;

        /// <summary>Resolved package path (falls back to the standard UPM folder name).</summary>
        public static string PackagePath
        {
            get
            {
                if (!string.IsNullOrEmpty(packagePath))
                    return packagePath;

                var info = PackageInfo.FindForAssembly(typeof(TrackingSettings).Assembly);
                packagePath = info != null && !string.IsNullOrEmpty(info.resolvedPath)
                    ? info.resolvedPath
                    : "Packages/" + PackageName;

                return packagePath;
            }
        }

        /// <summary>Package version, or "dev" when resolved from an embedded folder.</summary>
        public static string Version
        {
            get
            {
                var info = PackageInfo.FindForAssembly(typeof(TrackingSettings).Assembly);
                return info != null && !string.IsNullOrEmpty(info.version) ? info.version : "dev";
            }
        }

        public static string ReadmePath => Path.Combine(PackagePath, "README.md");
        public static string NativePluginPath =>
            Path.Combine(PackagePath, "Runtime", "Plugins", "iOS", "WagenheimerTracking.mm");

        public static void OpenDocumentation()
        {
            UnityEngine.Application.OpenURL(RepositoryUrl + "#readme");
        }
    }
}
