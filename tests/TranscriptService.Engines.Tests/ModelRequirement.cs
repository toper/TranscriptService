namespace TranscriptService.Engines.Tests;

/// <summary>
/// Engine tests need real models, which are too large for the repository and CI. Point
/// <c>MODELS_DIR</c> at a folder holding them in the layout of <c>deploy/models/models.txt</c>
/// (or run <c>deploy/models/fetch-models.sh &lt;engine&gt; &lt;dir&gt;</c>);
/// without it the tests are reported as skipped, never as failed.
/// </summary>
internal static class ModelRequirement
{
    public static string PathOrSkip(string relativePath)
    {
        var root = Environment.GetEnvironmentVariable("MODELS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(root), "Set MODELS_DIR to run engine tests against real models.");

        var path = Path.Combine(root!, relativePath);
        Assert.SkipUnless(File.Exists(path) || Directory.Exists(path), $"Model not found: {path}");
        return path;
    }
}
