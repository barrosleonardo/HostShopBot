namespace AirbnbShopApi.Services
{
    public class PaymentService : IPaymentService
    {
        public async Task<string> CreatePayment(decimal amount, string pixKey)
        {
            // Simulação de um serviço de pagamento (ex.: Pix)
            await Task.Delay(1000); // Simula latência de rede
            return $"PIX_{Guid.NewGuid().ToString()}";
        }

        public async Task<bool> ConfirmPayment(string paymentId)
        {
            // Simula a confirmação do pagamento (50% de chance de sucesso para teste)
            await Task.Delay(500); // Simula latência
            return new Random().Next(0, 2) == 1;
        }
    }
}