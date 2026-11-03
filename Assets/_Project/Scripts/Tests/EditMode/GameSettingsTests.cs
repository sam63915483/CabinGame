using Cabin.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class GameSettingsTests
    {
        [Test]
        public void Clamp_KeepsEveryValueInRange()
        {
            GameSettings.Width = 10;
            GameSettings.Height = 100000;
            GameSettings.WindowMode = (FullScreenMode)99;
            GameSettings.FrameCap = 37;
            GameSettings.Quality = 99;
            GameSettings.RenderScale = 3f;
            GameSettings.TreeDistance = 5;
            GameSettings.GrassDistance = 9999;
            GameSettings.Fov = 10f;
            GameSettings.Sensitivity = 5f;
            GameSettings.Clamp();
            Assert.AreEqual(640, GameSettings.Width);
            Assert.AreEqual(4320, GameSettings.Height);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, GameSettings.WindowMode);
            Assert.AreEqual(0, GameSettings.FrameCap, "An unknown cap means no cap");
            Assert.AreEqual(QualitySettings.names.Length - 1, GameSettings.Quality);
            Assert.AreEqual(GameSettings.RenderScaleMax, GameSettings.RenderScale);
            Assert.AreEqual(GameSettings.TreeDistanceMin, GameSettings.TreeDistance);
            Assert.AreEqual(GameSettings.GrassDistanceMax, GameSettings.GrassDistance);
            Assert.AreEqual(GameSettings.FovMin, GameSettings.Fov);
            Assert.AreEqual(GameSettings.SensitivityMax, GameSettings.Sensitivity);
        }

        [Test]
        public void Save_ThenLoad_RoundTrips()
        {
            GameSettings.Load();
            int tree = GameSettings.TreeDistance, grass = GameSettings.GrassDistance;
            float fov = GameSettings.Fov;
            try
            {
                GameSettings.TreeDistance = 350;
                GameSettings.GrassDistance = 70;
                GameSettings.Fov = 85f;
                GameSettings.Save();
                Assert.AreEqual(350, PlayerPrefs.GetInt("treeDistance"));
                Assert.AreEqual(70, PlayerPrefs.GetInt("grassDistance"));
                Assert.AreEqual(85f, PlayerPrefs.GetFloat("fov"), 1e-4f);
            }
            finally
            {
                GameSettings.TreeDistance = tree;
                GameSettings.GrassDistance = grass;
                GameSettings.Fov = fov;
                GameSettings.Save();
            }
        }
    }
}
