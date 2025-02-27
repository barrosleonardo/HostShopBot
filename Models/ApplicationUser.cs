using Microsoft.AspNetCore.Identity;

namespace AirbnbShopApi.Models
{
    public class ApplicationUser : IdentityUser
    {
        public ICollection<UserApartment> UserApartments { get; set; } = new List<UserApartment>();
    }
}