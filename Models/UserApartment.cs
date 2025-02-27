namespace AirbnbShopApi.Models
{
    public class UserApartment
    {
        public string UserId { get; set; } = null!;
        public int ApartmentId { get; set; }

        public ApplicationUser User { get; set; } = null!;
        public Apartment Apartment { get; set; } = null!;
    }
}