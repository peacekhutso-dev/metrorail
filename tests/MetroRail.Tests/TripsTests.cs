using MetroRail.Trips;

namespace MetroRail.Tests;

public class TripsTests
{
    private static readonly object Work = new
    {
        name = "Work", from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-CLAREMONT",
        days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }, usual_time = "07:18", train_no = "3412",
    };

    private static Task<TestApp> Start() => TestApp.StartAsync(TripsService.CreateBuilder, TripsService.Build, "trips");

    [Fact]
    public async Task Add_edit_list_delete_trip()
    {
        await using var t = await Start();
        var (status, trip) = await t.Client.Post("/trips", Work, "u1");
        Assert.Equal(201, status);
        var id = trip.Str("id");
        var edited = new { name = "Varsity", from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-CLAREMONT", days = new[] { "Mon" }, usual_time = "07:18", train_no = "3412" };
        Assert.Equal("Varsity", (await t.Client.Send(HttpMethod.Put, $"/trips/{id}", edited, "u1")).Body.Str("name"));
        Assert.Equal(["Varsity"], (await t.Client.Get("/trips", "u1")).Body.EnumerateArray().Select(x => x.Str("name")));
        Assert.Equal(0, (await t.Client.Get("/trips", "u2")).Body.GetArrayLength());           // other users can't see it
        Assert.Equal(404, (await t.Client.Send(HttpMethod.Delete, $"/trips/{id}", null, "u2")).Status);
        Assert.Equal(204, (await t.Client.Send(HttpMethod.Delete, $"/trips/{id}", null, "u1")).Status);
        Assert.Equal(["trips.trip.saved", "trips.trip.saved", "trips.trip.deleted"], t.Bus.PublishedSubjects);
    }

    [Fact]
    public async Task Validation_and_login_required()
    {
        await using var t = await Start();
        Assert.Equal(401, (await t.Client.Get("/trips")).Status);
        var same = new { name = "Work", from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-RONDEBOSCH", days = new[] { "Mon" }, usual_time = "07:18" };
        Assert.Equal("same_station", (await t.Client.Post("/trips", same, "u1")).Body.GetProperty("error").Str("code"));
        var blank = new { name = "   ", from_station_id = "ST-RONDEBOSCH", to_station_id = "ST-CLAREMONT", days = new[] { "Mon" }, usual_time = "07:18" };
        Assert.Equal(422, (await t.Client.Post("/trips", blank, "u1")).Status);
    }

    [Fact]
    public async Task User_deleted_purges_trips()
    {
        await using var t = await Start();
        await t.Client.Post("/trips", Work, "u9");
        await t.Bus.PublishAsync("identity.user.deleted", new { user_id = "u9" });
        Assert.Equal(0, (await t.Client.Get("/trips", "u9")).Body.GetArrayLength());
    }
}
