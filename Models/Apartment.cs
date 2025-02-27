namespace AirbnbShopApi.Models
{
    public class Apartment
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Identifier { get; set; } = null!;
        public decimal Balance { get; set; }
        public string PixKey { get; set; } = null!;

        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<UserApartment> UserApartments { get; set; } = new List<UserApartment>();
        public ICollection<AdminTransaction> AdminTransactions { get; set; } = new List<AdminTransaction>();
    }
}