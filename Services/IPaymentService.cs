namespace AirbnbShopApi.Services
{
    public interface IPaymentService
    {
        Task<string> CreatePayment(decimal amount, string pixKey);
        Task<bool> ConfirmPayment(string paymentId);
    }
}