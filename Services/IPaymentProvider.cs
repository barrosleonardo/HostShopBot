namespace AirbnbShopApi.Services
{
    public interface IPaymentProvider
    {
        string Name { get; }
        bool RequiresManualConfirmation { get; }
        Task<string> CreatePayment(decimal amount, string pixKey);
        Task<bool> ConfirmPayment(string paymentId);
    }

    public class PaymentProviderConfig
    {
        public bool Enabled { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string ApiUrl { get; set; } = string.Empty;
    }
}