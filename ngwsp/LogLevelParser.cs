using Microsoft.Extensions.Logging;

namespace ngwsp;

public static class LogLevelParser
{
    public static bool TryParse(string? value, out LogLevel level)
    {
        level = LogLevel.Information;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value, true, out level);
    }
}
