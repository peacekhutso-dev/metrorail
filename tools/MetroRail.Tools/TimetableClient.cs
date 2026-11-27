using System.Net.Http.Json;
using MetroRail.Common;

namespace MetroRail.Tools;

/// <summary>
/// Retrieves scheduled train services from the Network API.
///
/// The simulator does not access the Network database directly.
/// </summary>
public sealed class TimetableClient
{
    private readonly HttpClient _http;

    public TimetableClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>
    /// Retrieve and validate the complete timetable for a line.
    /// </summary>
    public async Task<IReadOnlyList<SimulationTrip>> LoadAsync(
        string lineId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lineId))
        {
            throw new ArgumentException(
                "Railway line identifier is required.",
                nameof(lineId));
        }

        var encodedLineId = Uri.EscapeDataString(lineId);

        var path = $"lines/{encodedLineId}/trips";

        // Request the timetable from the Network microservice.
        using var response = await _http.GetAsync(
            path,
            cancellationToken);

        // Do not continue with missing or failed API responses.
        response.EnsureSuccessStatusCode();

        // Use the shared MetroRail JSON configuration,
        // including its snake_case property naming convention.
        var trips = await response.Content
            .ReadFromJsonAsync<List<SimulationTrip>>(
                Json.Options,
                cancellationToken);

        if (trips is null)
        {
            throw new InvalidDataException(
                "The Network API returned an invalid timetable.");
        }

        if (trips.Count == 0)
        {
            throw new InvalidDataException(
                $"No scheduled services found for {lineId}.");
        }

        var trainNumbers = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var trip in trips)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Validate the complete schedule before simulation.
            SimulationEngine.Validate(trip);

            // Reject services belonging to an unexpected line.
            if (!string.Equals(
                trip.LineId,
                lineId,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Train {trip.TrainNo} belongs to an unexpected line.");
            }

            // Prevent duplicate train identities.
            if (!trainNumbers.Add(trip.TrainNo))
            {
                throw new InvalidDataException(
                    $"Duplicate train number: {trip.TrainNo}.");
            }
        }

        return trips
            .OrderBy(t => t.TrainNo, StringComparer.Ordinal)
            .ToList();
    }
}