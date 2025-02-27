using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AirbnbShopApi.Services
{
    public class PayPalPaymentProvider : IPaymentProvider
    {
        private readonly HttpClient _httpClient;
        private readonly PaymentProviderConfig _config;

        public PayPalPaymentProvider(IHttpClientFactory httpClientFactory, IOptionsSnapshot<PaymentProviderConfig> config)
        {
            _config = config.Get("PayPal");
            _httpClient = httpClientFactory.CreateClient();
            string apiUrl = string.IsNullOrWhiteSpace(_config.ApiUrl) ? "https://api.sandbox.paypal.com" : _config.ApiUrl;
            _httpClient.BaseAddress = new Uri(apiUrl);
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ClientId}:{_config.ClientSecret}"));
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
        }

        public string Name => "PayPal";
        public bool RequiresManualConfirmation => false;

        public async Task<string> CreatePayment(decimal amount, string pixKey)
        {
            if (string.IsNullOrWhiteSpace(_config.ClientId) || string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                throw new InvalidOperationException("ClientId ou ClientSecret para PayPal não estão configurados no appsettings.json.");
            }

            var requestBody = new
            {
                intent = "sale",
                payer = new { payment_method = "paypal" },
                transactions = new[] { new { amount = new { total = amount.ToString("F2"), currency = "BRL" } } },
                redirect_urls = new { return_url = "http://localhost:5000/Financial/Return", cancel_url = "http://localhost:5000/Financial/Cancel" }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/v2/checkout/orders", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var order = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            return order?["id"]?.ToString() ?? throw new InvalidOperationException("ID do pedido não encontrado na resposta.");
        }

        public async Task<bool> ConfirmPayment(string paymentId)
        {
            var response = await _httpClient.GetAsync($"/v2/checkout/orders/{paymentId}");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var order = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            return order?["status"]?.ToString() == "COMPLETED";
        }
    }
}