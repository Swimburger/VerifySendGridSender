using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using SendGrid;

namespace VerifySender;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        var apiKey = configuration["SendGrid:ApiKey"] 
                     ?? Environment.GetEnvironmentVariable("SENDGRID_API_KEY")
                     ?? throw new Exception("SendGrid API Key not configured.");
        
        var client = new SendGridClient(apiKey);

        // replace this JSON with your own values
        const string data = """
        {
            "nickname": "Orders",
            "from_email": "orders@example.com",
            "from_name": "Example Orders",
            "reply_to": "orders@example.com",
            "reply_to_name": "Example Orders",
            "address": "1234 Fake St",
            "address2": "PO Box 1234",
            "state": "CA",
            "city": "San Francisco",
            "country": "USA",
            "zip": "94105"
        }
        """;

        var response = await client.RequestAsync(
            method: SendGridClient.Method.POST,
            urlPath: "verified_senders",
            requestBody: data
        );

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to request sender verification. HTTP status code {response.StatusCode}.");
            Console.WriteLine(await response.Body.ReadAsStringAsync());
            Console.WriteLine(response.Headers.ToString());
        }

        Console.WriteLine("Enter verification URL:");
        var verificationUrl = Console.ReadLine();

        var token = await GetVerificationTokenFromUrl(verificationUrl);

        response = await client.RequestAsync(
            method: SendGridClient.Method.GET,
            urlPath: $"verified_senders/verify/{token}"
        );

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to verify sender. HTTP status code {response.StatusCode}.");
            Console.WriteLine(await response.Body.ReadAsStringAsync());
            Console.WriteLine(response.Headers.ToString());
        }
    }

    private static async Task<string> GetVerificationTokenFromUrl(string url)
    {
        /*
         * url could be three different types:
         * 1. Click Tracking Link which responds with HTTP Found and Location header to url type 2.
         * 2. URL containing the verification token:
         *      https://app.sendgrid.com/settings/sender_auth/senders/verify?token=[VERIFICATION_TOKEN]&etc=etc
         * 3. URL prompting the user to login, but contains url 2. in the redirect_to parameter:
         *      https://app.sendgrid.com/login?redirect_to=[URL_TYPE_2_ENCODED]
        */
        const string verificationBaseUrl = "https://app.sendgrid.com/settings/sender_auth/senders/verify";
        const string loginBaseUrl = "https://app.sendgrid.com/login";
        if (url.StartsWith(verificationBaseUrl))
        {
            var uri = new Uri(url, UriKind.Absolute);
            var parameters = QueryHelpers.ParseQuery(uri.Query);
            if (parameters.ContainsKey("token"))
            {
                return parameters["token"].ToString();
            }

            throw new Exception("Did not find token in verification URL.");
        }

        if (url.StartsWith(loginBaseUrl))
        {
            var uri = new Uri(url, UriKind.Absolute);
            var parameters = QueryHelpers.ParseQuery(uri.Query);
            if (parameters.ContainsKey("redirect_to"))
            {
                url = $"https://app.sendgrid.com{parameters["redirect_to"]}";
                return await GetVerificationTokenFromUrl(url);
            }

            throw new Exception("Did not find token in verification URL.");
        }

        var clientHandler = new HttpClientHandler();
        clientHandler.AllowAutoRedirect = false;
        using var httpClient = new HttpClient(clientHandler);
        var response = await httpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Found)
        {
            var uri = response.Headers.Location;
            return await GetVerificationTokenFromUrl(uri.ToString());
        }

        throw new Exception("Did not find token in verification URL.");
    }
}