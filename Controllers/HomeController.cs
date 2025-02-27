using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AirbnbShopApi.Data;
using AirbnbShopApi.Models;
using System.Globalization;

namespace AirbnbShopApi.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<HomeController> _logger;

        public HomeController(AppDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment environment, ILogger<HomeController> logger)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
            _logger = logger;
        }

        [AllowAnonymous]
        public IActionResult Welcome()
        {
            return View();
        }

        [Authorize]
        public async Task<IActionResult> Index(int? selectedApartmentId, int transactionsPage = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized("Usuário não encontrado.");

            var apartments = await _context.Apartments
                .Include(a => a.Products)
                .ThenInclude(p => p.Transactions)
                .Where(a => a.UserApartments.Any(ua => ua.UserId == user.Id))
                .ToListAsync();

            _logger.LogInformation("Usuário: {UserName}, Apartamentos encontrados: {Count}", user.UserName, apartments.Count);
            foreach (var apt in apartments)
            {
                _logger.LogInformation("Apartamento: {Name}, Produtos: {Count}", apt.Name, apt.Products.Count);
            }

            var selectedApartment = apartments.FirstOrDefault(a => a.Id == selectedApartmentId) ?? apartments.FirstOrDefault();
            if (selectedApartment != null)
            {
                const int pageSize = 20;
                var allTransactions = await _context.Transactions
                    .Include(t => t.Product)
                    .Where(t => t.Product.ApartmentId == selectedApartment.Id)
                    .OrderByDescending(t => t.RequestDate)
                    .ToListAsync();

                var totalTransactions = allTransactions.Count();
                var totalTransactionsPages = (int)Math.Ceiling(totalTransactions / (double)pageSize);
                var paginatedTransactions = allTransactions
                    .Skip((transactionsPage - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var retentionRate = await _context.RetentionRates
                    .OrderByDescending(rr => rr.EffectiveDate)
                    .FirstOrDefaultAsync() ?? new RetentionRate { Rate = 0.1m }; // Valor padrão 10%
                var retentionAmount = selectedApartment.Balance * retentionRate.Rate;
                var netBalance = selectedApartment.Balance - retentionAmount;

                ViewBag.Transactions = paginatedTransactions;
                ViewBag.TransactionsPage = transactionsPage;
                ViewBag.TransactionsTotalPages = totalTransactionsPages;
                ViewBag.RetentionFee = retentionRate.Rate;
                ViewBag.RetentionAmount = retentionAmount;
                ViewBag.NetBalance = netBalance;
            }

            ViewBag.Apartments = apartments;
            ViewBag.SelectedApartmentId = selectedApartmentId ?? apartments.FirstOrDefault()?.Id;
            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddProduct(int apartmentId, string name, string description, string priceStr, string lockCode, IFormFile image)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _context.UserApartments.AnyAsync(ua => ua.UserId == user.Id && ua.ApartmentId == apartmentId))
                return Forbid();

            if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
            {
                _logger.LogWarning("Erro ao converter preço: {PriceStr}", priceStr);
                return BadRequest("Formato de preço inválido.");
            }

            string? imageUrl = null;
            if (image != null && image.Length > 0)
            {
                _logger.LogInformation("Imagem recebida - Nome: {FileName}, Tamanho: {Length} bytes", image.FileName, image.Length);
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "_uploads");
                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                Directory.CreateDirectory(uploadsFolder);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(fileStream);
                }
                imageUrl = "/_uploads/" + uniqueFileName;
                _logger.LogInformation("Imagem salva em: {FilePath}, URL: {ImageUrl}", filePath, imageUrl);
            }

            var product = new Product
            {
                Name = name,
                Description = description,
                Price = price,
                OwnerTelegramId = user.Id,
                ApartmentId = apartmentId,
                ImageUrl = imageUrl,
                LockCode = lockCode,
                IsAvailable = true
            };

            _logger.LogInformation("Adicionando produto ao contexto...");
            _context.Products.Add(product);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Produto salvo com ID: {Id}, ImageUrl: {ImageUrl}", product.Id, product.ImageUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar no banco");
                return StatusCode(500, "Erro ao salvar o produto.");
            }

            return RedirectToAction(nameof(Index), new { selectedApartmentId = apartmentId });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> EditProduct(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao carregar produto para edição: ID {Id}", id);
                return Unauthorized("Usuário não encontrado.");
            }

            var product = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null || !await _context.UserApartments.AnyAsync(ua => ua.UserId == user.Id && ua.ApartmentId == product.ApartmentId))
            {
                _logger.LogWarning("Produto não encontrado ou sem permissão: ID {Id}", id);
                return NotFound();
            }

            _logger.LogInformation("Produto carregado do banco - ID: {Id}, Nome: {Name}, ImageUrl: {ImageUrl}", product.Id, product.Name, product.ImageUrl);

            ViewBag.StatusOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "true", Text = "Disponível" },
                new SelectListItem { Value = "false", Text = "Vendido" }
            };

            return View(product);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> EditProduct(Product product, IFormFile image)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao editar produto: ID {Id}", product.Id);
                return Unauthorized("Usuário não encontrado.");
            }

            var existingProduct = await _context.Products.FirstOrDefaultAsync(p => p.Id == product.Id);
            if (existingProduct == null || !await _context.UserApartments.AnyAsync(ua => ua.UserId == user.Id && ua.ApartmentId == existingProduct.ApartmentId))
            {
                _logger.LogWarning("Produto não encontrado ou sem permissão: ID {Id}", product.Id);
                return NotFound();
            }

            _logger.LogInformation("Editando produto - ID: {Id}, Nome antes: {OldName}, Nome novo: {NewName}", product.Id, existingProduct.Name, product.Name);
            _logger.LogInformation("ImageUrl antes da edição: {ImageUrl}", existingProduct.ImageUrl);

            existingProduct.Name = product.Name;
            existingProduct.Description = product.Description;
            existingProduct.Price = product.Price;
            existingProduct.LockCode = product.LockCode;
            existingProduct.IsAvailable = product.IsAvailable;

            if (image != null && image.Length > 0)
            {
                _logger.LogInformation("Nova imagem recebida - Nome: {FileName}, Tamanho: {Length} bytes", image.FileName, image.Length);
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "_uploads");
                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                Directory.CreateDirectory(uploadsFolder);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(fileStream);
                }
                var newImageUrl = "/_uploads/" + uniqueFileName;
                _logger.LogInformation("Nova imagem salva em: {FilePath}, ImageUrl: {ImageUrl}", filePath, newImageUrl);

                if (!string.IsNullOrEmpty(existingProduct.ImageUrl))
                {
                    var oldFilePath = Path.Combine(_environment.WebRootPath, existingProduct.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                        _logger.LogInformation("Imagem antiga removida: {OldFilePath}", oldFilePath);
                    }
                }

                existingProduct.ImageUrl = newImageUrl;
            }

            try
            {
                _logger.LogInformation("Salvando alterações no banco...");
                await _context.SaveChangesAsync();
                _logger.LogInformation("Produto atualizado - ID: {Id}, ImageUrl: {ImageUrl}", existingProduct.Id, existingProduct.ImageUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar atualização no banco");
                return StatusCode(500, "Erro ao atualizar o produto.");
            }

            return RedirectToAction(nameof(Index), new { selectedApartmentId = existingProduct.ApartmentId });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao excluir produto: ID {Id}", id);
                return Unauthorized("Usuário não encontrado.");
            }

            var product = await _context.Products.FindAsync(id);
            if (product == null || !await _context.UserApartments.AnyAsync(ua => ua.UserId == user.Id && ua.ApartmentId == product.ApartmentId))
            {
                _logger.LogWarning("Produto não encontrado ou sem permissão: ID {Id}", id);
                return NotFound();
            }

            if (!string.IsNullOrEmpty(product.ImageUrl))
            {
                var filePath = Path.Combine(_environment.WebRootPath, product.ImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    _logger.LogInformation("Imagem removida ao excluir produto: {FilePath}", filePath);
                }
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Produto excluído: ID {Id}", id);
            return RedirectToAction(nameof(Index), new { selectedApartmentId = product.ApartmentId });
        }
    }
}