
using MetroRail.Common;
using Microsoft.EntityFrameworkCore;

namespace MetroRail.Network;

public static class Endpoints
{
    public static void MapNetwork(this WebApplication app)
    {
        var g = app.MapGroup("").WithTags("network");

        // ------------------------------------------------------------
        // GET /stations
        // Returns all stations, optionally filtered by station name.
        // ------------------------------------------------------------
        g.MapGet("/stations", async (string? q, NetworkDb db) =>
        {
            var stations = await db.Stations
                .OrderBy(s => s.Name)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(q))
            {
                stations = stations
                    .Where(s => s.Name.Contains(
                        q,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Determine which railway line each station belongs to.
            var lineOf = (await db.LineStops.ToListAsync())
                .GroupBy(x => x.StationId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(stop => stop.LineId).Distinct().ToList());

            return stations.Select(s =>
                new StationOut(
                    s.Id,
                    s.Name,
                    s.Lat,
                    s.Lng,
                    lineOf.TryGetValue(s.Id, out var lines)
                        ? lines
                        : []));
        });

        // ------------------------------------------------------------
        // GET /stations/nearest?lat=...&lng=...
        // Returns the nearest station to the supplied coordinates.
        // ------------------------------------------------------------
        g.MapGet("/stations/nearest", async (
            double lat,
            double lng,
            NetworkDb db) =>
        {
            var stations = await db.Stations.ToListAsync();

            if (stations.Count == 0)
                throw new NotFoundException("No stations loaded");

            var best = stations.MinBy(s =>
                Math.Pow(s.Lat - lat, 2) +
                Math.Pow(s.Lng - lng, 2))!;

            return new StationOut(
                best.Id,
                best.Name,
                best.Lat,
                best.Lng,
                []);
        });

        // ------------------------------------------------------------
        // GET /lines
        // Returns all railway lines.
        // ------------------------------------------------------------
        g.MapGet("/lines", async (NetworkDb db) =>
            (await db.Lines.ToListAsync())
                .Select(l => new LineOut(
                    l.Id,
                    l.Name,
                    l.Colour)));

        // ------------------------------------------------------------
        // GET /lines/{lineId}/stops
        // Returns the physical station sequence of a railway line.
        // This is network information, not an individual train timetable.
        // ------------------------------------------------------------
        g.MapGet("/lines/{lineId}/stops", async (
            string lineId,
            NetworkDb db) =>
        {
            var stops = await LineStops(db, lineId);

            if (stops.Count == 0)
            {
                throw new NotFoundException(
                    $"Line {lineId} not found",
                    "line_not_found");
            }

            return stops.Select(s => new LineStopOut(
                s.Seq,
                s.StationId,
                s.StationName,
                s.MinutesFromOrigin));
        });

        // ------------------------------------------------------------
        // GET /trips/{trainNo}
        // Returns the actual scheduled calls for one train.
        //
        // IMPORTANT:
        // This endpoint no longer calculates station times using
        // general line intervals. It reads ScheduledTripStops.
        // ------------------------------------------------------------
        g.MapGet("/trips/{trainNo}", async (
            string trainNo,
            NetworkDb db) =>
        {
            var trip = await db.ScheduledTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Station)
                .FirstOrDefaultAsync(t => t.TrainNo == trainNo)
                ?? throw new NotFoundException(
                    $"Train {trainNo} is not in the timetable",
                    "train_not_found");

            var stops = trip.Stops
                .OrderBy(s => s.Seq)
                .ToList();

            if (stops.Count == 0)
            {
                throw new NotFoundException(
                    $"Train {trainNo} has no scheduled stops",
                    "trip_stops_not_found");
            }

            // The final actual scheduled call is the train's terminus.
            var terminus = stops[^1].Station.Name;

            return new TripOut(
                trip.TrainNo,
                trip.LineId,
                trip.Direction,
                terminus,
                trip.Platform,
                stops.Select(s => new StopTime(
                    s.Seq,
                    s.StationId,
                    s.Station.Name,
                    s.SourceStationLabel,
                    Clock.Hhmm(s.ScheduledMin)
                )).ToList()
            );
        });

        // ------------------------------------------------------------
        // GET /lines/{lineId}/trips
        //
        // Returns the complete scheduled timetable for a railway line.
        //
        // Includes operating calendars, actual scheduled events,
        // original timetable labels and station coordinates.
        //
        // The catalogue is independent of the current clock.
        // Consumers determine which services operate at a given time.
        // ------------------------------------------------------------
        g.MapGet("/lines/{lineId}/trips", async (
            string lineId,
            NetworkDb db,
            CancellationToken cancellationToken) =>
        {
            // Verify that the requested railway line exists.
            var lineExists = await db.Lines
                .AsNoTracking()
                .AnyAsync(
                    line => line.Id == lineId,
                    cancellationToken);

            if (!lineExists)
            {
                throw new NotFoundException(
                    $"Line {lineId} not found",
                    "line_not_found");
            }

            // Load scheduled trains and their actual station events.
            // AsNoTracking avoids unnecessary EF Core change tracking
            // because this endpoint performs read-only operations.
            var trips = await db.ScheduledTrips
                .AsNoTracking()
                .Where(t => t.LineId == lineId)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Station)
                .OrderBy(t => t.TrainNo)
                .ToListAsync(cancellationToken);

            var timetable = new List<TimetableTripOut>();

            foreach (var trip in trips)
            {
                var stops = trip.Stops
                    .OrderBy(s => s.Seq)
                    .ToList();

                // Fail explicitly rather than silently publishing
                // an incomplete train timetable.
                if (stops.Count < 2 ||
                    stops.Select(s => s.StationId)
                        .Distinct().Count() < 2)
                {
                    throw new InvalidOperationException(
                        $"Train {trip.TrainNo} has an invalid timetable.");
                }

                // Preserve the actual terminating station.
                var terminus = stops[^1].Station.Name;

                var scheduledEvents = stops
                    .Select(s => new TimetableStopOut(
                        s.Seq,
                        s.StationId,
                        s.Station.Name,
                        s.SourceStationLabel,
                        s.ScheduledMin,
                        Clock.Hhmm(s.ScheduledMin),
                        s.Station.Lat,
                        s.Station.Lng
                    ))
                    .ToList();

                timetable.Add(new TimetableTripOut(
                    trip.TrainNo,
                    trip.LineId,
                    trip.Direction,
                    terminus,
                    trip.Platform,
                    trip.ServiceDays,
                    scheduledEvents
                ));
            }

            return timetable;
        });

        // ------------------------------------------------------------
        // GET /stations/{stationId}/departures
        //
        // Returns trains that actually call at this station.
        // Uses each train's stored scheduled time.
        // ------------------------------------------------------------
        g.MapGet("/stations/{stationId}/departures", async (
            string stationId,
            string? direction,
            string? after,
            int? limit,
            NetworkDb db,
            IClock clock) =>
        {
            if (direction is not null)
            {
                Check.Matches(
                    direction,
                    "^(southbound|northbound)$",
                    "direction");
            }

            var take = Check.Range(
                limit ?? 6,
                1,
                50,
                "limit");

            var station = await db.Stations.FindAsync(stationId);

            if (station is null)
            {
                throw new NotFoundException(
                    $"Station {stationId} not found",
                    "station_not_found");
            }

            // Use a consistent clock snapshot for this request.
            var currentTime = clock.UtcNow;
            var now = Clock.MinuteOfDay(currentTime);

            var start = after is null
                ? now
                : TimetableRules.ParseQueryTime(after);

            // Retrieve actual timetable calls at this station.
            // The train's other stops are included so we can identify
            // its final scheduled call and therefore its terminus.
            var scheduledStops = await db.ScheduledTripStops
                .Include(s => s.Trip)
                    .ThenInclude(t => t.Stops)
                        .ThenInclude(other => other.Station)
                .Include(s => s.Station)
                .Where(s => s.StationId == stationId)
                .ToListAsync();

            var departures = new List<DepartureOut>();

            foreach (var stop in scheduledStops)
            {
                var trip = stop.Trip;

                // Exclude services that do not operate today.
                if (!TimetableRules.OperatesOn(trip, currentTime))
                    continue;

                // Apply the optional direction filter.
                if (direction is not null &&
                    trip.Direction != direction)
                {
                    continue;
                }

                // Identify the actual terminating station.
                var finalStop = trip.Stops
                    .OrderBy(s => s.Seq)
                    .LastOrDefault();

                // Exclude arrival-only calls and terminating stations.
                if (finalStop is null ||
                    !TimetableRules.CanBoard(stop, finalStop))
                {
                    continue;
}

                // Use the actual timetable time, not line intervals.
                var scheduledMin = stop.ScheduledMin;

                if (scheduledMin < start)
                    continue;

                var terminus = finalStop.Station.Name;

                departures.Add(new DepartureOut(
                    trip.TrainNo,
                    trip.LineId,
                    trip.Direction,
                    terminus,
                    trip.Platform,
                    stop.SourceStationLabel,
                    Clock.Hhmm(scheduledMin),
                    scheduledMin,
                    scheduledMin - now
                ));
            }

            return departures
                .OrderBy(d => d.ScheduledMin)
                .Take(take);
        });

        // ------------------------------------------------------------
        // GET /journeys?from=...&to=...&after=...&limit=...
        //
        // Finds direct journeys using actual scheduled calls.
        // A train must call at BOTH requested stations, and the
        // destination must occur after the origin in its stop sequence.
        // ------------------------------------------------------------
        g.MapGet("/journeys", async (
            HttpContext ctx,
            NetworkDb db,
            IClock clock) =>
        {
            var q = ctx.Request.Query;

            var from = Check.Required(q["from"], "from");
            var to = Check.Required(q["to"], "to");

            // Origin and destination must be different physical stations.
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "Origin and destination must be different.",
                    new[] { "from", "to" });
            }

            // Default to three journeys only when no limit was supplied.
            var requestedLimit = 3;

            if (q.ContainsKey("limit"))
            {
                if (!int.TryParse(q["limit"], out requestedLimit))
                {
                    throw new ValidationException(
                        "Limit must be an integer.",
                        new[] { "limit" });
                }
            }

            var take = Check.Range(requestedLimit, 1, 10, "limit");

            // Capture the clock once for consistent time and calendar checks.
            var currentTime = clock.UtcNow;

            var start = q.ContainsKey("after")
                ? TimetableRules.ParseQueryTime(q["after"].ToString())
                : Clock.MinuteOfDay(currentTime);

            // Validate both station IDs before searching.
            var stationIds = await db.Stations
                .Where(s => s.Id == from || s.Id == to)
                .Select(s => s.Id)
                .ToListAsync();

            if (!stationIds.Contains(from))
            {
                throw new NotFoundException(
                    $"Origin station {from} not found",
                    "station_not_found");
            }

            if (!stationIds.Contains(to))
            {
                throw new NotFoundException(
                    $"Destination station {to} not found",
                    "station_not_found");
            }

            // Load the actual calls for each scheduled train.
            var trips = await db.ScheduledTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Station)
                .ToListAsync();

            var options = new List<JourneyOption>();

            foreach (var trip in trips)
            {
                // Exclude services that do not operate today.
                if (!TimetableRules.OperatesOn(trip, currentTime))
                    continue;

                var orderedStops = trip.Stops
                    .OrderBy(s => s.Seq)
                    .ToList();

                // A valid journey requires at least two scheduled events.
                if (orderedStops.Count < 2)
                    continue;

                var firstStop = orderedStops[0];
                var finalStop = orderedStops[^1];

                // Find events where passengers are allowed to board.
                var originStops = orderedStops
                    .Where(s =>
                        s.StationId == from &&
                        TimetableRules.CanBoard(s, finalStop))
                    .ToList();

                // Find events where passengers are allowed to alight.
                var destinationStops = orderedStops
                    .Where(s =>
                        s.StationId == to &&
                        TimetableRules.CanAlight(s, firstStop))
                    .ToList();

                // This train must call at both stations.
                if (originStops.Count == 0 ||
                    destinationStops.Count == 0)
                {
                    continue;
                }

                // Find a valid origin/destination pair in travel order.
                // Boarding and alighting eligibility has already been checked.
                var journeyFound = false;

                foreach (var originStop in originStops)
                {
                    foreach (var destinationStop in destinationStops)
                    {
                        // Destination must occur after origin.
                        if (destinationStop.Seq <= originStop.Seq)
                            continue;

                        var departure = originStop.ScheduledMin;
                        var arrival = destinationStop.ScheduledMin;

                        // Do not return journeys that have already departed.
                        if (departure < start)
                            continue;

                        // Reject invalid or inconsistent time sequences.
                        if (arrival < departure)
                            continue;

                        var duration = arrival - departure;

                        options.Add(new JourneyOption(
                            trip.TrainNo,
                            trip.LineId,
                            originStop.Station.Name,
                            destinationStop.Station.Name,
                            Clock.Hhmm(departure),
                            Clock.Hhmm(arrival),
                            duration,
                            trip.Platform
                        ));

                        journeyFound = true;
                        break;
                    }

                    if (journeyFound)
                        break;
                }
            }

            return options
                .OrderBy(o => o.Departs, StringComparer.Ordinal)
                .Take(take);
        });
    }

    // ------------------------------------------------------------
    // Helper: physical station sequence for a railway line.
    //
    // This remains useful for displaying the network layout.
    // It must NOT be used to calculate an individual train's
    // actual scheduled stop times.
    // ------------------------------------------------------------
    private static async Task<List<Stop>> LineStops(
        NetworkDb db,
        string lineId)
    {
        var rows = await (
            from ls in db.LineStops
            join st in db.Stations
                on ls.StationId equals st.Id
            where ls.LineId == lineId
            orderby ls.Seq
            select new
            {
                ls.Seq,
                st.Id,
                st.Name,
                ls.MinutesFromPrevious
            }
        ).ToListAsync();

        var acc = 0;

        return rows.Select(r =>
        {
            acc += r.MinutesFromPrevious;

            return new Stop(
                r.Seq,
                r.Id,
                r.Name,
                acc);
        }).ToList();
    }
}
