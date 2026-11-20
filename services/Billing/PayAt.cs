using System.Security.Cryptography;
using System.Text;

namespace MetroRail.Billing;

/// <summary>
/// Pay@ gateway adapter. Card payments only.
///
/// MetroRail never sees a full card number in production: the card form is Pay@'s hosted/secure
/// form shown inside the app, and we keep only Pay@'s card token + brand + last 4 digits (PCI DSS SAQ-A).
///
/// - <see cref="MockPayAt"/> (PAYAT_MODE=mock, default outside production), deterministic test cards:
///     4000 0000 0000 0002 -> approved after 3-D Secure (OTP 123456)
///     4000 0000 0000 0069 -> declined by bank
/// - <see cref="PayAtClient"/> (PAYAT_MODE=sandbox|live): real Pay@ API using PAYAT_* settings.
///   TODO(Peace): map these methods to the Pay@ sandbox endpoints from the merchant docs (create
///   transaction / hosted card page, 3-D Secure redirect, status, refund) and confirm the webhook
///   signature header and algorithm.
/// </summary>
public sealed record GatewayResult(string GatewayRef, string Status, string? CardFormUrl = null, string? ThreeDsUrl = null,
    string? CardToken = null, string? Brand = null, string? Last4 = null, string? DeclineReason = null);

public interface IGateway
{
    bool IsMock { get; }
    Task<GatewayResult> CreateAsync(string reference, int amountCents, string? cardToken);
    Task<GatewayResult> SubmitCardAsync(string gatewayRef, string number, string expiry, string cvv, string name, bool save);
    Task<GatewayResult> Confirm3dsAsync(string gatewayRef, string otp);
    bool VerifyWebhook(byte[] body, string signature);
}

public static class Cards
{
    public static string Brand(string number) => number.StartsWith('4') ? "visa" : number[..1] is "2" or "5" ? "mastercard" : "card";

    public static bool LuhnOk(string number)
    {
        if (number.Length is not (15 or 16 or 19) || !number.All(char.IsDigit)) return false;
        var sum = 0;
        for (var i = 0; i < number.Length; i++)
        {
            var d = number[number.Length - 1 - i] - '0';
            if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 == 0;
    }

    public static bool HmacOk(byte[] secret, byte[] body, string signature)
    {
        var expected = Convert.ToHexString(HMACSHA256.HashData(secret, body)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature ?? ""));
    }
}

/// <summary>Behaves like a card gateway with 3-D Secure, without any network calls.</summary>
public sealed class MockPayAt(string webhookSecret) : IGateway
{
    public const string Decline = "4000000000000069";
    public const string Otp = "123456";
    private readonly Dictionary<string, (string Reference, int Amount, string? Token, string? Number)> _pending = new();
    private readonly byte[] _secret = Encoding.UTF8.GetBytes(webhookSecret);

    public bool IsMock => true;

    public Task<GatewayResult> CreateAsync(string reference, int amountCents, string? cardToken)
    {
        var r = "PAT-" + Guid.NewGuid().ToString("N")[..12];
        lock (_pending) _pending[r] = (reference, amountCents, cardToken, null);
        return Task.FromResult(cardToken is not null
            ? new GatewayResult(r, "requires_3ds", ThreeDsUrl: $"mock://3ds/{r}") // saved card -> straight to bank approval
            : new GatewayResult(r, "requires_card", CardFormUrl: $"mock://card/{r}"));
    }

    public Task<GatewayResult> SubmitCardAsync(string gatewayRef, string number, string expiry, string cvv, string name, bool save)
    {
        number = number.Replace(" ", "");
        if (!Cards.LuhnOk(number)) return Task.FromResult(new GatewayResult(gatewayRef, "requires_card", DeclineReason: "invalid_card_number"));
        var mm = int.Parse(expiry[..2]);
        var yy = 2000 + int.Parse(expiry[3..]);
        if ((yy, mm).CompareTo((2026, 11)) < 0) return Task.FromResult(new GatewayResult(gatewayRef, "requires_card", DeclineReason: "card_expired"));
        var token = save ? "tok_" + Guid.NewGuid().ToString("N")[..16] : null;
        lock (_pending) _pending[gatewayRef] = _pending[gatewayRef] with { Number = number, Token = token };
        return Task.FromResult(new GatewayResult(gatewayRef, "requires_3ds", ThreeDsUrl: $"mock://3ds/{gatewayRef}",
            CardToken: token, Brand: Cards.Brand(number), Last4: number[^4..]));
    }

    public Task<GatewayResult> Confirm3dsAsync(string gatewayRef, string otp)
    {
        string? number;
        lock (_pending) number = _pending.TryGetValue(gatewayRef, out var p) ? p.Number : null;
        if (number == Decline) return Task.FromResult(new GatewayResult(gatewayRef, "declined", DeclineReason: "declined_by_bank"));
        return Task.FromResult(otp != Otp
            ? new GatewayResult(gatewayRef, "requires_3ds", DeclineReason: "wrong_otp")
            : new GatewayResult(gatewayRef, "succeeded"));
    }

    public bool VerifyWebhook(byte[] body, string signature) => Cards.HmacOk(_secret, body, signature);
}

/// <summary>Real Pay@ integration (sandbox / live). See the TODO above.</summary>
public sealed class PayAtClient(string baseUrl, string merchantId, string apiKey, string webhookSecret) : IGateway
{
    // Used by the real implementation (TODO): HTTP client with the merchant credentials.
    private readonly HttpClient _http = CreateHttp(baseUrl, apiKey);
    private readonly string _merchantId = merchantId;
    private readonly byte[] _secret = Encoding.UTF8.GetBytes(webhookSecret);

    public bool IsMock => false;

    private static HttpClient CreateHttp(string baseUrl, string apiKey)
    {
        var http = new HttpClient { BaseAddress = new Uri(string.IsNullOrEmpty(baseUrl) ? "https://invalid.local" : baseUrl), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return http;
    }

    public override string ToString() => $"PayAtClient(merchant={_merchantId}, base={_http.BaseAddress})";

    public Task<GatewayResult> CreateAsync(string reference, int amountCents, string? cardToken) =>
        throw new NotImplementedException("Map to Pay@ 'create transaction' (hosted card page / token charge)");

    public Task<GatewayResult> SubmitCardAsync(string gatewayRef, string number, string expiry, string cvv, string name, bool save) =>
        throw new NotImplementedException("With Pay@'s hosted form the card never reaches our API");

    public Task<GatewayResult> Confirm3dsAsync(string gatewayRef, string otp) =>
        throw new NotImplementedException("Pay@ completes 3-D Secure; we learn the result via the webhook");

    public bool VerifyWebhook(byte[] body, string signature) => Cards.HmacOk(_secret, body, signature);
}
