using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AirbnbShopApi.Services
{
    public class MercadoPagoPaymentProvider : IPaymentProvider
    {
        private readonly HttpClient _httpClient;
        private readonly PaymentProviderConfig _config;

        public MercadoPagoPaymentProvider(IHttpClientFactory httpClientFactory, IOptionsSnapshot<PaymentProviderConfig> config)
        {
            _config = config.Get("MercadoPago");
            _httpClient = httpClientFactory.CreateClient();
            string apiUrl = string.IsNullOrWhiteSpace(_config.ApiUrl) ? "https://api.mercadopago.com" : _config.ApiUrl;
            _httpClient.BaseAddress = new Uri(apiUrl);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.AccessToken}");
        }

        public string Name => "MercadoPago";
        public bool RequiresManualConfirmation => false;

        public async Task<string> CreatePayment(decimal amount, string pixKey)
        {
            if (string.IsNullOrWhiteSpace(_config.AccessToken))
            {
                throw new InvalidOperationException("AccessToken para MercadoPago não está configurado no appsettings.json.");
            }

            var requestBody = new
            {
                transaction_amount = amount,
                description = "Compra no AirbnbShop",
                payment_method_id = "pix",
                payer = new { email = "cliente@exemplo.com" }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/v1/payments", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var payment = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            return payment?["id"]?.ToString() ?? throw new InvalidOperationException("ID do pagamento não encontrado na resposta.");
        }

        public async Task<bool> ConfirmPayment(string paymentId)
        {
            var response = await _httpClient.GetAsync($"/v1/payments/{paymentId}");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var payment = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            return payment?["status"]?.ToString() == "approved";
        }
    }
}