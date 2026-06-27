using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Caching.Distributed;

namespace PartsPortal.Shared.Reservations;

/// <summary>
/// Durable reservation registry over <see cref="IDistributedCache"/> (Phase 2, DR-011) so the
/// availability app and the separate TTL-release Function app share one view of soft reservations.
///
/// Unlike the other durable stores this one must ENUMERATE (FindStaleSoft), which a plain
/// key/value cache can't do — so it keeps an index set of the currently-soft reservation ids and
/// loads those entries to filter by age. Terminal reservations (converted/released) are removed
/// from the index and dropped.
///
/// CONCURRENCY CAVEAT: the index is maintained with read-modify-write, which is not atomic across
/// processes — a lost index write could leave a soft reservation unswept (IVS's own TTL is the
/// backstop). An in-process lock narrows intra-process races. A production hardening is a Redis
/// sorted set keyed by placed-at (atomic ZADD/ZRANGEBYSCORE); this implementation is config-gated
/// and build-verified, with the sorted-set upgrade tracked as Phase-2 infra.
/// </summary>
public sealed class DistributedReservationRegistry(IDistributedCache cache) : IReservationRegistry
{
    private const string EntryPrefix = "resv:";
    private const string IndexKey = "resv:index";
    private readonly Lock _gate = new();

    public void Record(string reservationId, DateTimeOffset placedAtUtc, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);

        lock (_gate)
        {
            var entry = new ReservationEntry(reservationId, placedAtUtc, correlationId, ReservationState.Soft);
            cache.SetString(EntryPrefix + reservationId, JsonSerializer.Serialize(entry));
            var index = ReadIndex();
            if (index.Add(reservationId))
            {
                WriteIndex(index);
            }
        }
    }

    public void MarkConverted(string reservationId) => RemoveFromSoft(reservationId);

    public void MarkReleased(string reservationId) => RemoveFromSoft(reservationId);

    public IReadOnlyList<ReservationEntry> FindStaleSoft(DateTimeOffset cutoffUtc)
    {
        lock (_gate)
        {
            var stale = new List<ReservationEntry>();
            foreach (var id in ReadIndex())
            {
                var json = cache.GetString(EntryPrefix + id);
                if (json is null)
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<ReservationEntry>(json);
                if (entry is { State: ReservationState.Soft } && entry.PlacedAtUtc < cutoffUtc)
                {
                    stale.Add(entry);
                }
            }

            return stale;
        }
    }

    private void RemoveFromSoft(string reservationId)
    {
        if (string.IsNullOrWhiteSpace(reservationId))
        {
            return;
        }

        lock (_gate)
        {
            cache.Remove(EntryPrefix + reservationId);
            var index = ReadIndex();
            if (index.Remove(reservationId))
            {
                WriteIndex(index);
            }
        }
    }

    private HashSet<string> ReadIndex()
    {
        var json = cache.GetString(IndexKey);
        return json is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(JsonSerializer.Deserialize<List<string>>(json) ?? [], StringComparer.Ordinal);
    }

    private void WriteIndex(HashSet<string> index) =>
        cache.SetString(IndexKey, JsonSerializer.Serialize(index.ToList()));
}
