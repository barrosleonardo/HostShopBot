using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AirbnbShopApi.Data
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            var connectionString = "Server=localhost;Port=3306;Database=airbnb_shop;User=user_airbnb_shop;Password=senha123;";
            optionsBuilder.UseMySQL(connectionString); // Sem ServerVersion
            return new AppDbContext(optionsBuilder.Options);
        }
    }
}