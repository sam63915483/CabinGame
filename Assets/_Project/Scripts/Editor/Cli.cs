namespace Cabin.Editor
{
    /// <summary>Batchmode entry points (handoff Appendix B). They call the same code as the Tools/Cabin menu items.</summary>
    public static class Cli
    {
        /// <summary>-executeMethod Cabin.Editor.Cli.GenerateSandbox</summary>
        public static void GenerateSandbox()
        {
            SandboxAssetGenerator.GenerateAll();
            TestPlatformBuilder.Rebuild();
        }
    }
}
