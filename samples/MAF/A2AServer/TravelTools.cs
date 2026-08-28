namespace A2AServer;

internal static class TravelTools
{
    private static readonly Dictionary<string, Tour> s_tours =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ireland"] = new("galway-food-tour", "Galway Food Tour", "11:00", 75, 6),
            ["France"] = new("paris-food-tour", "Paris Food Tour", "10:30", 90, 5),
            ["Italy"] = new("historic-rome-walk", "Historic Rome Walking Tour", "09:30", 45, 8),
            ["Japan"] = new("kyoto-temple-tour", "Kyoto Temple Tour", "08:30", 60, 4),
            ["Spain"] = new("barcelona-architecture-tour", "Barcelona Architecture Tour", "10:00", 50, 7),
            ["United States"] = new("new-york-city-highlights", "New York City Highlights Tour", "09:00", 80, 10),
        };

    public static Tour? GetAvailableTour(string country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);

        return s_tours.GetValueOrDefault(country.Trim());
    }

    public static string GetCurrentLocalTime(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return "Unable to determine local time: a time-zone ID is required.";
        }

        try
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            DateTimeOffset localTime = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);

            return $"{localTime:yyyy-MM-dd HH:mm:ss zzz} ({timeZone.Id})";
        }
        catch (TimeZoneNotFoundException)
        {
            return $"Time zone '{timeZoneId}' was not found. Use an IANA time-zone ID such as 'Europe/Dublin'.";
        }
        catch (InvalidTimeZoneException)
        {
            return $"Time zone '{timeZoneId}' is invalid.";
        }
    }
}

internal sealed record Tour(
    string Id,
    string Name,
    string StartTime,
    decimal PriceEur,
    int AvailableSpots);
