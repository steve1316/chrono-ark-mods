using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.PerfDebug
{
    /// <summary>
    /// Tracks Unity scene load and transition events, logging scene names
    /// and timestamps for correlating with performance hitches.
    /// </summary>
    internal class SceneTransitionTracker : MonoBehaviour
    {
        private const string Tag = "[GameplayEnhancements][PerfDebug][Scene]";

        void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Debug.Log($"{Tag} Scene tracking initialized, " +
                      $"current scene: {SceneManager.GetActiveScene().name}");
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        /// <summary>
        /// Fires when any scene finishes loading.
        /// </summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"{Tag} Scene loaded: '{scene.name}' (buildIndex={scene.buildIndex}, " +
                      $"mode={mode}) at frame {Time.frameCount}, t={Time.realtimeSinceStartup:F3}s");
        }

        /// <summary>
        /// Fires when the active scene changes.
        /// </summary>
        private static void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            Debug.Log($"{Tag} Active scene changed: '{oldScene.name}' -> '{newScene.name}' " +
                      $"at frame {Time.frameCount}, t={Time.realtimeSinceStartup:F3}s");
        }
    }
}
