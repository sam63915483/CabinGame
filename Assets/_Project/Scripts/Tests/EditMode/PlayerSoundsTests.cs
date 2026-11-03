using System.Reflection;
using Cabin.Editor;
using Cabin.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cabin.Tests
{
    public class PlayerSoundsTests
    {
        [Test]
        public void Player_HasFootstepsJumpAndLanding_ForGrassAndWood()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.PlayerPrefabPath);
            Assert.IsNotNull(player);
            var sounds = player.GetComponent<PlayerSounds>();
            Assert.IsNotNull(sounds, "The player makes sounds");
            Assert.IsNotNull(player.GetComponent<AudioSource>());
            foreach (var field in new[] { "grassSteps", "woodSteps", "jumps", "grassLands", "woodLands", "punchWhooshes", "punchHits", "nailGunShots", "sawCuts" })
            {
                var clips = (AudioClip[])typeof(PlayerSounds).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sounds);
                Assert.IsNotNull(clips, field);
                Assert.Greater(clips.Length, 0, field);
                foreach (var c in clips) Assert.IsNotNull(c, field);
            }
        }
    }
}
