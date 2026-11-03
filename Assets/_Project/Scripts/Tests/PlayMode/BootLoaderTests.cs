using System.Collections;
using Cabin.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    public class BootLoaderTests
    {
        [UnityTest]
        public IEnumerator Boot_LoadsWorldAdditively_AndMakesItActive()
        {
            yield return SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);

            const float timeout = 10f;
            float start = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != BootLoader.WorldSceneName)
            {
                Assert.Less(Time.realtimeSinceStartup - start, timeout, "World never became the active scene");
                yield return null;
            }

            Assert.IsTrue(SceneManager.GetSceneByName("Boot").isLoaded, "Boot should stay loaded under World");
            Assert.IsTrue(SceneManager.GetSceneByName(BootLoader.WorldSceneName).isLoaded);
            yield return SceneManager.UnloadSceneAsync(BootLoader.WorldSceneName);
        }
    }
}
