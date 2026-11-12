using System;
using System.Collections.Generic;
using Unity.Services.Analytics;
using AnalyticsEvent = Unity.Services.Analytics.Event;
using Unity.Services.Core;
using UnityEngine;

namespace Wagenheimer.Tracking
{
    /// <summary>
    /// One entry point for game analytics on top of Unity Analytics (<c>com.unity.services.analytics</c>),
    /// replacing the deprecated <c>UnityEngine.Analytics.Analytics.CustomEvent</c>.
    /// <list type="bullet">
    /// <item>Initializes Unity Services once and starts data collection (or waits for consent).</item>
    /// <item>Buffers events recorded before the SDK is ready, so nothing from the first frames is lost.</item>
    /// <item>Never throws: analytics must not be able to break the game.</item>
    /// <item>Accepts the same <c>(string, IDictionary&lt;string, object&gt;)</c> shape as the old
    /// <c>CustomEvent</c>, so existing calls migrate by renaming the method.</item>
    /// </list>
    /// Typed helpers for the events that matter to a puzzle/casual game (levels, ads, purchases,
    /// paywall, economy, tutorial) are in <c>GameAnalytics.Events.cs</c>.
    /// </summary>
    public static partial class GameAnalytics
    {
        private const int MaxBufferedEvents = 200;

        private static readonly Queue<AnalyticsEvent> Buffer = new Queue<AnalyticsEvent>();
        private static bool initializing;

        /// <summary>True once Unity Services is initialized and events are sent straight away.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>True while data collection is on (consent given and the SDK started).</summary>
        public static bool IsCollecting { get; private set; }

        /// <summary>Writes every event to the console. Useful while checking the taxonomy in a dev build.</summary>
        public static bool LogEvents { get; set; }

        /// <summary>Raised for every event handed to <see cref="Track(string, IDictionary{string, object})"/>.</summary>
        public static event Action<string, IDictionary<string, object>> EventTracked;

        /// <summary>
        /// Initializes Unity Services and, unless <paramref name="waitForConsent"/> is set, starts data
        /// collection. Safe to call more than once. Call <see cref="SetConsent"/> later when consent is
        /// collected separately.
        /// </summary>
        /// <param name="waitForConsent">True when the player must opt in first (GDPR/CCPA flow).</param>
        public static async void Initialize(bool waitForConsent = false)
        {
            if (IsReady || initializing) return;
            initializing = true;

            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    await UnityServices.InitializeAsync();
                }
                else if (UnityServices.State == ServicesInitializationState.Initializing)
                {
                    while (UnityServices.State == ServicesInitializationState.Initializing)
                        await System.Threading.Tasks.Task.Yield();
                }

                IsReady = UnityServices.State == ServicesInitializationState.Initialized;
                if (!IsReady)
                {
                    Warn("Unity Services did not initialize; analytics stays off.");
                    return;
                }

                if (!waitForConsent) SetConsent(true);
                FlushBuffer();
            }
            catch (Exception ex)
            {
                Warn("Initialize failed: " + ex.Message);
            }
            finally
            {
                initializing = false;
            }
        }

        /// <summary>Starts or stops data collection. Pass false when the player opts out.</summary>
        public static void SetConsent(bool granted)
        {
            if (!IsReady) return;

            try
            {
                if (granted)
                {
                    AnalyticsService.Instance.StartDataCollection();
                    IsCollecting = true;
                    FlushBuffer();
                }
                else
                {
                    AnalyticsService.Instance.StopDataCollection();
                    IsCollecting = false;
                    Buffer.Clear();
                }
            }
            catch (Exception ex)
            {
                Warn("SetConsent failed: " + ex.Message);
            }
        }

        /// <summary>Asks Unity to delete the data it holds for this player (privacy request).</summary>
        public static void RequestDataDeletion()
        {
            if (!IsReady) return;

            try { AnalyticsService.Instance.RequestDataDeletion(); }
            catch (Exception ex) { Warn("RequestDataDeletion failed: " + ex.Message); }
        }

        /// <summary>Sends everything queued right now (for example before the app quits).</summary>
        public static void Flush()
        {
            if (!IsReady || !IsCollecting) return;

            try { AnalyticsService.Instance.Flush(); }
            catch (Exception ex) { Warn("Flush failed: " + ex.Message); }
        }

        /// <summary>
        /// Records a custom event. <paramref name="parameters"/> accepts string, int, long, float, double,
        /// bool and enums; anything else is sent as text and null values are skipped.
        /// </summary>
        public static void Track(string eventName, IDictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return;

            var name = AnalyticsNames.Sanitize(eventName);
            var custom = new CustomEvent(name);

            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    var value = AnalyticsNames.NormalizeValue(pair.Value);
                    if (value != null) custom.Add(AnalyticsNames.Sanitize(pair.Key), value);
                }
            }

            if (LogEvents) Debug.Log("[GameAnalytics] " + name + AnalyticsNames.Describe(parameters));
            EventTracked?.Invoke(name, parameters);
            Record(custom);
        }

        /// <summary>Records a custom event from name/value pairs: <c>Track("hintUsed", ("level", 12))</c>.</summary>
        public static void Track(string eventName, params (string key, object value)[] parameters)
        {
            Dictionary<string, object> map = null;
            if (parameters != null && parameters.Length > 0)
            {
                map = new Dictionary<string, object>(parameters.Length);
                foreach (var (key, value) in parameters) map[key] = value;
            }

            Track(eventName, map);
        }

        /// <summary>Records any Unity Analytics event, including the standard ones (ad impression, transaction).</summary>
        public static void Record(AnalyticsEvent analyticsEvent)
        {
            if (analyticsEvent == null) return;

            if (!IsReady || !IsCollecting)
            {
                if (Buffer.Count >= MaxBufferedEvents) Buffer.Dequeue();
                Buffer.Enqueue(analyticsEvent);
                return;
            }

            try { AnalyticsService.Instance.RecordEvent(analyticsEvent); }
            catch (Exception ex) { Warn("RecordEvent failed: " + ex.Message); }
        }

        private static void FlushBuffer()
        {
            if (!IsReady || !IsCollecting) return;

            while (Buffer.Count > 0)
            {
                var queued = Buffer.Dequeue();
                try { AnalyticsService.Instance.RecordEvent(queued); }
                catch (Exception ex) { Warn("RecordEvent failed: " + ex.Message); }
            }
        }

        private static void Warn(string message) => Debug.LogWarning("[GameAnalytics] " + message);
    }
}
