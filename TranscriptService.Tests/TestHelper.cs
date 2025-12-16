namespace TranscriptService.Tests;

/// <summary>
/// Klasa pomocnicza do obsługi plików testowych
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// Zwraca pełną ścieżkę do pliku testowego w folderze TestData
    /// </summary>
    /// <param name="relativePath">Relatywna ścieżka do pliku, np. "Audio/sample.wav"</param>
    /// <returns>Pełna ścieżka do pliku testowego</returns>
    public static string GetTestFilePath(string relativePath)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var testDataPath = Path.Combine(baseDirectory, "TestData", relativePath);

        if (!File.Exists(testDataPath))
        {
            throw new FileNotFoundException($"Plik testowy nie został znaleziony: {testDataPath}");
        }

        return testDataPath;
    }

    /// <summary>
    /// Sprawdza czy plik testowy istnieje
    /// </summary>
    /// <param name="relativePath">Relatywna ścieżka do pliku, np. "Audio/sample.wav"</param>
    /// <returns>True jeśli plik istnieje, w przeciwnym razie false</returns>
    public static bool TestFileExists(string relativePath)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var testDataPath = Path.Combine(baseDirectory, "TestData", relativePath);
        return File.Exists(testDataPath);
    }

    /// <summary>
    /// Zwraca folder TestData/Audio
    /// </summary>
    public static string GetTestAudioDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return Path.Combine(baseDirectory, "TestData", "Audio");
    }
}
