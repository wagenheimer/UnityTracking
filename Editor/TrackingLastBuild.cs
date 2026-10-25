using System;
using System.IO;
using UnityEngine;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>
    /// Result of the last iOS build, written by <see cref="TrackingBuildProcessor"/> and read by the
    /// checker. It lives under <c>Library/</c> (never shipped) and records what the build actually
    /// declared, not what the settings say today.
    /// </summary>
    [Serializable]
    internal sealed class TrackingLastBuild
    {
        public string path;
        public string platform;
        public bool declaresTracking;
        public bool purposeStringWritten;
        public string timestampUtc;
        public string unityVersion;
        public string packageVersion;

        private static string FilePath =>
            Path.Combine(Directory.GetCurrentDirectory(), "Library", "Wagenheimer.Tracking", "last-build.json");

        public static void Save(TrackingLastBuild info)
        {
            if (info == null)
                return;

            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(FilePath, JsonUtility.ToJson(info, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Tracking] Could not write the last-build report: {exception.Message}");
            }
        }

        public static TrackingLastBuild Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;

                return JsonUtility.FromJson<TrackingLastBuild>(File.ReadAllText(FilePath));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
