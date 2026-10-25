namespace OpenMagicka.Engine;

/// <summary>
/// Where OpenMagicka keeps its own files (the settings, the saves, the log, the transcoded sound): <c>OpenMagicka</c>
/// in the user's local application data, or in the temporary folder where there is none (a system without a home).
/// </summary>
public static class DataFolder
{
    public static string Root
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(string.IsNullOrEmpty(local) ? Path.GetTempPath() : local, "OpenMagicka");
        }
    }

    public static string Of(string name) => Path.Combine(Root, name);
}
