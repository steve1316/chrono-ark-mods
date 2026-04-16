using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.PerfDebug
{
    /// <summary>
    /// Persistent MonoBehaviour that detects frame-time hitches and logs
    /// diagnostic context including recent method calls and active canvases.
    /// </summary>
    internal class FrameTimeMonitor : MonoBehaviour
    {
        private const string Tag = "[GameplayEnhancements][PerfDebug]";
        private const float HitchThresholdSec = 0.100f;
        private const float MajorHitchThresholdSec = 0.500f;
        private const float FreezeThresholdSec = 2.000f;

        // Ring buffer for recent method entries recorded by Harmony patches.
        private const int RingSize = 32;
        private static readonly string[] _ring = new string[RingSize];
        private static int _ringIndex;

        // TAB press tracking.
        private static float _lastTabPressTime = -1f;
        private static int _lastTabPressFrame = -1;

        // Recovery tracking.
        private int _consecutiveSlowFrames;
        private bool _inHitchSequence;

        /// <summary>
        /// Records a method entry into the ring buffer. Called by Harmony patches.
        /// </summary>
        internal static void RecordMethodEntry(string entry)
        {
            int idx = _ringIndex % RingSize;
            _ring[idx] = $"[F{Time.frameCount}] {entry}";
            _ringIndex++;
        }

        void Update()
        {
            // Track TAB presses for correlation with subsequent hitches.
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _lastTabPressTime = Time.realtimeSinceStartup;
                _lastTabPressFrame = Time.frameCount;
                Debug.Log($"{Tag}[TAB] TAB pressed at frame {Time.frameCount}, t={_lastTabPressTime:F3}s");
            }

            float dt = Time.unscaledDeltaTime;
            if (dt < HitchThresholdSec)
            {
                // Frame was smooth. Check if we're recovering from a hitch sequence.
                if (_inHitchSequence)
                {
                    Debug.Log($"{Tag}[HITCH] Recovered after {_consecutiveSlowFrames} slow frame(s) " +
                              $"at frame {Time.frameCount}");
                    _consecutiveSlowFrames = 0;
                    _inHitchSequence = false;
                }
                return;
            }

            // This frame was slow.
            _consecutiveSlowFrames++;
            _inHitchSequence = true;

            float dtMs = dt * 1000f;
            string severity;
            if (dt >= FreezeThresholdSec)
                severity = "[FREEZE]";
            else if (dt >= MajorHitchThresholdSec)
                severity = "[MAJOR_HITCH]";
            else
                severity = "[HITCH]";

            string scene = SceneManager.GetActiveScene().name;
            string tabCorrelation = "";
            if (_lastTabPressFrame >= 0)
            {
                float elapsed = Time.realtimeSinceStartup - _lastTabPressTime;
                int frameDelta = Time.frameCount - _lastTabPressFrame;
                tabCorrelation = $", {elapsed:F3}s / {frameDelta} frames after TAB";
            }

            Debug.Log($"{Tag}{severity} {dtMs:F1}ms at frame {Time.frameCount}, " +
                      $"scene={scene}, t={Time.realtimeSinceStartup:F3}s, " +
                      $"slowStreak={_consecutiveSlowFrames}{tabCorrelation}");

            // Dump ring buffer contents.
            DumpRingBuffer(severity);

            // On FREEZE, also snapshot active canvases to identify which UI is open.
            if (dt >= FreezeThresholdSec)
                DumpActiveCanvases();
        }

        /// <summary>
        /// Logs the recent method entries from the ring buffer.
        /// </summary>
        private void DumpRingBuffer(string severity)
        {
            int count = Mathf.Min(_ringIndex, RingSize);
            if (count == 0) return;

            // Read entries oldest-to-newest.
            int start = _ringIndex >= RingSize ? _ringIndex % RingSize : 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{Tag}{severity} Recent method entries ({count}):");
            for (int i = 0; i < count; i++)
            {
                int idx = (start + i) % RingSize;
                if (_ring[idx] != null)
                    sb.AppendLine($"  {_ring[idx]}");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Logs all active Canvas objects to identify which UI screens are open.
        /// </summary>
        private void DumpActiveCanvases()
        {
            var canvases = FindObjectsOfType<Canvas>();
            if (canvases.Length == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{Tag}[Canvas] Active canvases ({canvases.Length}):");
            foreach (var c in canvases)
            {
                string path = GetGameObjectPath(c.gameObject);
                sb.AppendLine($"  {path} (enabled={c.enabled}, sortOrder={c.sortingOrder})");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Builds the full transform hierarchy path for a GameObject.
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }
    }
}
