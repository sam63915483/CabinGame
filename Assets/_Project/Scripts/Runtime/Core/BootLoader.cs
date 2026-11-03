using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cabin.Core
{
    /// <summary>
    /// Lives in the Boot scene. Loads the World scene additively and makes it the active
    /// scene so its lighting and skybox apply. Managers that must outlive World go in Boot.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        public const string WorldSceneName = "World";

        [SerializeField] string worldScene = WorldSceneName;

        IEnumerator Start()
        {
            if (SceneManager.GetSceneByName(worldScene).isLoaded)
                yield break;

            float t0 = Time.realtimeSinceStartup;
            yield return SceneManager.LoadSceneAsync(worldScene, LoadSceneMode.Additive);
#if UNITY_EDITOR || CABIN_DEV
            Debug.Log($"[Cabin] World scene loaded in {Time.realtimeSinceStartup - t0:F1} s, at {Time.realtimeSinceStartup:F1} s since start");
#endif
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(worldScene));
        }
    }
}
