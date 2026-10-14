using System.Net;
using System.Runtime.CompilerServices;
using SkyBellSharp.Models;

namespace SkyBellSharp;

public sealed partial class SkyBellClient
{
    // =================================================================
    //  Activity history
    // =================================================================

    /// <summary><c>GET /api/v5/activity</c> — one page of events, newest first.</summary>
    public Task<ActivityPage> GetActivitiesAsync(ActivityQuery? query = null, CancellationToken cancellationToken = default) =>
        GetDataAsync<ActivityPage>("/api/v5/activity" + (query ?? new ActivityQuery()).ToQueryString(), cancellationToken);

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
            if (page.Rows.Count < pageSize || (page.Count > 0 && offset >= page.Count)) yield break;
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

    /// <summary><c>GET /api/v5/activity/day</c> — short summary of recent events grouped by day.</summary>
    public Task<ActivityDaySummary> GetActivityDaySummaryAsync(CancellationToken cancellationToken = default) =>
        GetDataAsync<ActivityDaySummary>("/api/v5/activity/day", cancellationToken);

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
