using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

namespace AirbnbShopApi.Services
{
    public class StripePaymentProvider : IPaymentProvider
    {
        private readonly HttpClient _httpClient;
        private readonly PaymentProviderConfig _config;

        public StripePaymentProvider(IHttpClientFactory httpClientFactory, IOptionsSnapshot<PaymentProviderConfig> config)
        {
            _config = config.Get("Stripe");
            _httpClient = httpClientFactory.CreateClient();
            string apiUrl = string.IsNullOrWhiteSpace(_config.ApiUrl) ? "https://api.stripe.com" : _config.ApiUrl;
            _httpClient.BaseAddress = new Uri(apiUrl);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.SecretKey}");
        }

        public string Name => "Stripe";
        public bool RequiresManualConfirmation => false;

        public async Task<string> CreatePayment(decimal amount, string pixKey)
        {
            if (string.IsNullOrWhiteSpace(_config.SecretKey))
            {
                throw new InvalidOperationException("SecretKey para Stripe não está configurado no appsettings.json.");
            }

            var requestBody = new
            {
                amount = (int)(amount * 100), // Centavos
                currency = "brl",
                payment_method_types = new[] { "card" },
                description = "Compra no AirbnbShop"
            };

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "amount", requestBody.amount.ToString() },
                { "currency", requestBody.currency },
                { "payment_method_types[]", "card" },
                { "description", requestBody.description }
            });

            var response = await _httpClient.PostAsync("/v1/payment_intents", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var intent = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            return intent?["id"]?.ToString() ?? throw new InvalidOperationException("ID do pagamento não encontrado na resposta.");
        }

        public async Task<bool> ConfirmPayment(string paymentId)
        {
            var response = await _httpClient.GetAsync($"/v1/payment_intents/{paymentId}");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var intent = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            return intent?["status"]?.ToString() == "succeeded";
        }
    }
}