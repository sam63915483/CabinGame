using Cabin.Editor.Map;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class TreeLibraryTests
    {
        [Test]
        public void Thresholds_PutEverySpeciesSwitchAtTheSameDistance()
        {
            foreach (float size in new[] { 4.7f, 8.3f, 13f })
            {
                TreeLibrary.Thresholds(size, 3, out var heights, out var widths);
                // The LOD switches when the tree's screen height drops to the threshold: that happens at FullTo metres.
                float switchDistance = size / (heights[0] * 2f * Mathf.Tan(36f * Mathf.Deg2Rad));
                Assert.AreEqual(TreeLibrary.FullTo, switchDistance, 0.01f, $"size {size}");
                float middle = size / (heights[1] * 2f * Mathf.Tan(36f * Mathf.Deg2Rad));
                Assert.AreEqual(TreeLibrary.MiddleTo, middle, 0.01f, $"size {size}");
                Assert.That(heights[0], Is.GreaterThan(heights[1]));
                Assert.That(heights[1], Is.GreaterThan(heights[2]));
                Assert.That(widths[0], Is.InRange(0.01f, 0.9f), "A fade band, not the whole LOD");
                Assert.That(widths[1], Is.InRange(0.01f, 0.9f));
            }
        }

        [Test]
        public void Thresholds_FadeStartsTheRightNumberOfMetresBeforeTheSwitch()
        {
            const float size = 8f;
            TreeLibrary.Thresholds(size, 3, out var heights, out var widths);
            // Fade band in screen-height terms maps back to FullTo - FadeMetres.
            float fadeStartHeight = heights[0] + widths[0] * (1f - heights[0]);
            float fadeStartDistance = size / (fadeStartHeight * 2f * Mathf.Tan(36f * Mathf.Deg2Rad));
            Assert.AreEqual(TreeLibrary.FullTo - TreeLibrary.FadeMetres, fadeStartDistance, 0.05f);
        }
    }
}
