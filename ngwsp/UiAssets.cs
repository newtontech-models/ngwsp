using System.Reflection;

namespace ngwsp;

public static class UiAssets
{
    private const string ResourcePrefix = "ngwsp.ui.";

    public static string GetText(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = ResourcePrefix + name;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"UI asset not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
