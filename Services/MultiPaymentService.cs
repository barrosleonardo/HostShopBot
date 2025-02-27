using Microsoft.Extensions.Configuration;

namespace AirbnbShopApi.Services
{
    public class MultiPaymentService : IPaymentService
    {
        private readonly IEnumerable<IPaymentProvider> _providers;
        private readonly IPaymentProvider _defaultProvider;

        public MultiPaymentService(IEnumerable<IPaymentProvider> providers, IConfiguration config)
        {
            _providers = providers.Where(p => config.GetSection($"PaymentProviders:{p.Name}:Enabled").Get<bool>());
            _defaultProvider = _providers.FirstOrDefault(p => p.Name == "PIXDefault") ?? _providers.First();
        }

        public async Task<string> CreatePayment(decimal amount, string pixKey)
        {
            return await _defaultProvider.CreatePayment(amount, pixKey);
        }

        public async Task<bool> ConfirmPayment(string paymentId)
        {
            if (_defaultProvider.Name == "PIXDefault")
                return await _defaultProvider.ConfirmPayment(paymentId); // Retorna false para confirmação manual

            return await _defaultProvider.ConfirmPayment(paymentId); // Confirmação automática para outros
        }
    }
}