namespace HemodinksAPI.Application.Features.Events;

// Civil dates are authoritative. UTC boundaries exist for reminders and legacy instant queries.
public static class AllDayEventSchedule
{
    public const string InvalidPeriod = "Informe datas válidas para o evento de dia inteiro, com término igual ou posterior ao início.";
    public const string InvalidZone = "Informe um fuso horário válido para o evento de dia inteiro.";

    public static bool IsValidZone(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 100
        && (id == "UTC" || TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _))
        && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static bool TryResolve(DateOnly? first, DateOnly? last, string? zoneId, out DateTime start, out DateTime end)
    {
        start = end = default;
        if (!first.HasValue || !last.HasValue || first > last || last == DateOnly.MaxValue || !IsValidZone(zoneId)) return false;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId!);
            start = StartOfDay(first.Value, zone);
            end = StartOfDay(last.Value.AddDays(1), zone);
            // Reject a civil date that does not exist in this timezone.
            _ = StartOfDay(last.Value, zone);
            return end > start;
        }
        catch (ArgumentException) { return false; }
    }

    private static DateTime StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some historical DST transitions skip midnight; use the first valid instant of that day.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
            if (DateOnly.FromDateTime(local) != day) throw new ArgumentException("Civil day does not exist.");
        }
        var utc = zone.IsAmbiguousTime(local)
            ? DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(local, zone);
        if (TimeZoneInfo.ConvertTimeFromUtc(utc, zone) != local) throw new ArgumentException("Civil day is outside the supported range.");
        return utc;
    }

    public static string Describe(DateOnly first, DateOnly last) => first == last
        ? $"Dia inteiro em {first:dd/MM/yyyy}." : $"Dia inteiro de {first:dd/MM/yyyy} a {last:dd/MM/yyyy}.";
}
