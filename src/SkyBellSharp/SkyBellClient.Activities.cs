using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SkyBellSharp.Models;

namespace SkyBellSharp;

public sealed partial class SkyBellClient
{
    // =================================================================
    //  Activity history
    // =================================================================

    /// <summary>
    /// <c>GET /api/v5/activity</c> — events, newest first. Without <see cref="ActivityQuery.Start"/>
    /// the API returns the last 7 days only.
    /// </summary>
    /// <remarks>
    /// The production API currently ignores <c>limit</c>/<c>offset</c> and returns the whole range
    /// (<c>limit: 100000</c>); this method then applies <see cref="ActivityQuery.Offset"/> and
    /// <see cref="ActivityQuery.Limit"/> client-side so paging behaves as documented.
    /// <see cref="ActivityPage.Count"/> is always the total.
    /// </remarks>
    public async Task<ActivityPage> GetActivitiesAsync(ActivityQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new ActivityQuery();
        var page = await GetDataAsync<ActivityPage>("/api/v5/activity" + query.ToQueryString(), cancellationToken)
            .ConfigureAwait(false);

        var requestedOffset = query.Offset ?? 0;
        var serverIgnoredOffset = requestedOffset > 0 && (page.Offset ?? 0) != requestedOffset;
        var serverIgnoredLimit = query.Limit is { } l && page.Rows.Count > l;

        if (serverIgnoredOffset || serverIgnoredLimit)
        {
            IEnumerable<SkyBellActivity> rows = page.Rows;
            if (serverIgnoredOffset) rows = rows.Skip(requestedOffset);
            if (query.Limit is { } limit) rows = rows.Take(limit);
            page.Rows = rows.ToList();
            page.Offset = requestedOffset;
            page.Limit = query.Limit;
        }
        return page;
    }

    /// <summary>
    /// Enumerates all matching events, fetching pages of <paramref name="pageSize"/> on demand.
    /// <see cref="ActivityQuery.Limit"/> and <see cref="ActivityQuery.Offset"/> are ignored.
    /// </summary>
    public async IAsyncEnumerable<SkyBellActivity> EnumerateActivitiesAsync(ActivityQuery? query = null, int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var q = query ?? new ActivityQuery();
        var offset = 0;

        while (true)
        {
            var page = await GetActivitiesAsync(new ActivityQuery
            {
                DeviceId = q.DeviceId,
                EventType = q.EventType,
                Start = q.Start,
                End = q.End,
                IncludePreviews = q.IncludePreviews,
                Limit = pageSize,
                Offset = offset
            }, cancellationToken).ConfigureAwait(false);

            foreach (var a in page.Rows) yield return a;

            offset += page.Rows.Count;
            if (page.Rows.Count == 0 || page.Rows.Count < pageSize || (page.Count > 0 && offset >= page.Count))
                yield break;
        }
    }

    /// <summary>
    /// Events of a single local day <b>with</b> their base64 previews (the API only returns
    /// previews when the requested range fits in one day).
    /// </summary>
    public Task<ActivityPage> GetActivitiesForDayAsync(DateOnly day, string? deviceId = null,
        TimeZoneInfo? timeZone = null, CancellationToken cancellationToken = default)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;
        var startLocal = day.ToDateTime(TimeOnly.MinValue);
        var start = new DateTimeOffset(startLocal, tz.GetUtcOffset(startLocal));
        var endLocal = day.ToDateTime(new TimeOnly(23, 59, 59, 999));
        var end = new DateTimeOffset(endLocal, tz.GetUtcOffset(endLocal));

        return GetActivitiesAsync(new ActivityQuery
        {
            DeviceId = deviceId,
            Start = start,
            End = end,
            IncludePreviews = true,
            Limit = 500
        }, cancellationToken);
    }

    /// <summary>
    /// <c>GET /api/v5/activity/day</c> — number of events per day, most recent first.
    /// (The production API returns <c>[{ activity_day, event_count }]</c>; the documented
    /// <c>{ days: { "MM/DD/YYYY": [...] } }</c> shape is also accepted.)
    /// </summary>
    public async Task<IReadOnlyList<ActivityDayCount>> GetActivityDaysAsync(CancellationToken cancellationToken = default)
    {
        const string path = "/api/v5/activity/day";
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var result = new List<ActivityDayCount>();

        if (data is JsonArray rows)
        {
            foreach (var row in rows.OfType<JsonObject>())
            {
                var day = ParseDay(row["activity_day"] ?? row["day"] ?? row["date"]);
                var count = row["event_count"] ?? row["count"];
                if (day is { } d && TryInt(count, out var n)) result.Add(new ActivityDayCount(d, n));
            }
        }
        else if (data is JsonObject obj && obj["days"] is JsonObject days)
        {
            foreach (var (key, list) in days)
                if (ParseDay(JsonValue.Create(key)) is { } d)
                    result.Add(new ActivityDayCount(d, (list as JsonArray)?.Count ?? 0));
        }

        return result.OrderByDescending(r => r.Day).ToList();

        static DateOnly? ParseDay(JsonNode? node)
        {
            var s = node is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;
            if (s is null) return null;
            string[] formats = { "yyyy-MM-dd", "MM/dd/yyyy" };
            return DateOnly.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : null;
        }

        static bool TryInt(JsonNode? node, out int value)
        {
            value = 0;
            if (node is not JsonValue v) return false;
            if (v.TryGetValue(out int i)) { value = i; return true; }
            return v.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>
    /// <c>GET /api/v5/activity/{id}/video</c> — pre-signed S3 URL of the event's MP4.
    /// It expires quickly: use it right away. Returns null when the event has no video.
    /// </summary>
    public async Task<Uri?> GetActivityVideoUrlAsync(string activityId, CancellationToken cancellationToken = default)
    {
        var data = await SendForDataAsync(HttpMethod.Get, $"/api/v5/activity/{Seg(activityId)}/video", null,
            cancellationToken, notFoundIsNull: true).ConfigureAwait(false);
        var url = data?["download_url"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(url) ? null : new Uri(url);
    }

    /// <summary>Downloads the event's MP4 into <paramref name="destination"/>.</summary>
    /// <exception cref="SkyBellApiException">The event has no video.</exception>
    public async Task DownloadActivityVideoAsync(string activityId, Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var url = await GetActivityVideoUrlAsync(activityId, cancellationToken).ConfigureAwait(false)
                  ?? throw new SkyBellApiException($"Activity {activityId} has no video.", HttpStatusCode.NotFound);
        await DownloadAsync(url, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a pre-signed SkyBell S3 URL (video, preview…) without any SkyBell header
    /// (extra headers would break the S3 signature).
    /// </summary>
    public async Task DownloadAsync(Uri url, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(destination);
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw SkyBellHttp.CreateException(resp.StatusCode, url.GetLeftPart(UriPartial.Path), null, null);
        await using var src = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await src.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>DELETE /api/v5/activity/{id}</c> — deletes one event and its video (irreversible).</summary>
    public async Task DeleteActivityAsync(string activityId, CancellationToken cancellationToken = default) =>
        await SendForDataAsync(HttpMethod.Delete, $"/api/v5/activity/{Seg(activityId)}", null, cancellationToken,
            notFoundIsNull: true).ConfigureAwait(false);

    /// <summary>
    /// <c>DELETE /api/v5/activity/day</c> — deletes <b>all events of all devices</b> for that day (irreversible).
    /// </summary>
    public async Task DeleteActivitiesForDayAsync(DateOnly day, CancellationToken cancellationToken = default) =>
        await SendForDataAsync(HttpMethod.Delete, "/api/v5/activity/day",
            new { day = day.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken).ConfigureAwait(false);
}
