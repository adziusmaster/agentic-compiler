namespace Agentic.Core.Execution;

public static class EnvLoader
{
    public static void Load(string filePath = ".env")
    {
        if (!File.Exists(filePath)) return;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) 
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0) 
                continue;

            var key = trimmed.Substring(0, separatorIndex).Trim();
            var value = trimmed.Substring(separatorIndex + 1).Trim().Trim('"', '\'');

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}