namespace AirbnbShopApi.Services
{
    public class PIXDefaultPaymentProvider : IPaymentProvider
    {
        public string Name => "PIXDefault";
        public bool RequiresManualConfirmation => true;

        public async Task<string> CreatePayment(decimal amount, string pixKey)
        {
            Console.WriteLine($"Criando pagamento PIX de R${amount}");
            await Task.Delay(100);
            return $"PIX_{Guid.NewGuid().ToString()}";
        }

        public async Task<bool> ConfirmPayment(string paymentId)
        {
            Console.WriteLine($"Confirmando pagamento PIX {paymentId} (requer confirmação manual)");
            await Task.Delay(100);
            return false; // Confirmação manual no AdminIndex
        }
    }
}