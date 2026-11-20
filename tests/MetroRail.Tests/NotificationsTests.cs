using MetroRail.Notifications;

namespace MetroRail.Tests;

public class NotificationsTests
{
    private static readonly object Trip = new
    {
        id = "TRP-1", user_id = "u1", name = "Work", train_no = "3412", alert_delays = true,
        from_station_id = "ST-RONDEBOSCH", usual_time = "07:18",
    };

    private static Task<TestApp> Start() => TestApp.StartAsync(NotificationsService.CreateBuilder, NotificationsService.Build, "notifications");

    [Fact]
    public async Task Stopped_train_alerts_trip_owner_once()
    {
        await using var t = await Start();
        await t.Bus.PublishAsync("trips.trip.saved", Trip);
        var ev = new { train_no = "3412", state = "stopped", stationary_s = 437, delay_min = 2, last_station_id = "ST-MOWBRAY", next_station_id = "ST-ROSEBANK" };
        await t.Bus.PublishAsync("tracking.train.state_changed", ev);
        await t.Bus.PublishAsync("tracking.train.state_changed", ev); // duplicate -> no second alert
        var (_, alerts) = await t.Client.Get("/alerts", "u1");
        Assert.Equal(1, alerts.GetArrayLength());
        Assert.Equal("Your train has stopped", alerts[0].Str("title"));
        Assert.Contains("between Mowbray and Rosebank for 7 min", alerts[0].Str("body"));
        Assert.Equal(1, (await t.Client.Get("/alerts/unread-count", "u1")).Body.GetProperty("unread").GetInt32());
        await t.Client.Post($"/alerts/{alerts[0].Str("id")}/read", null, "u1");
        Assert.Equal(0, (await t.Client.Get("/alerts/unread-count", "u1")).Body.GetProperty("unread").GetInt32());
        Assert.Contains("notifications.alert.created", t.Bus.PublishedSubjects);
    }

    [Fact]
    public async Task No_alert_for_other_trains_or_on_time()
    {
        await using var t = await Start();
        await t.Bus.PublishAsync("trips.trip.saved", Trip);
        await t.Bus.PublishAsync("tracking.train.state_changed", new { train_no = "3416", state = "stopped", stationary_s = 400 });
        await t.Bus.PublishAsync("tracking.train.state_changed", new { train_no = "3412", state = "moving", delay_min = 0 });
        Assert.Equal(0, (await t.Client.Get("/alerts", "u1")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Alerts_off_means_no_watch()
    {
        await using var t = await Start();
        await t.Bus.PublishAsync("trips.trip.saved", new { id = "TRP-1", user_id = "u1", name = "Work", train_no = "3412", alert_delays = false, from_station_id = "ST-RONDEBOSCH", usual_time = "07:18" });
        await t.Bus.PublishAsync("tracking.train.state_changed", new { train_no = "3412", state = "held", stationary_s = 200 });
        Assert.Equal(0, (await t.Client.Get("/alerts", "u1")).Body.GetArrayLength());
    }
}
