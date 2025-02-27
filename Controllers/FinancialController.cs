using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AirbnbShopApi.Data;
using AirbnbShopApi.Models;
using AirbnbShopApi.Services;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using AirbnbShopApi.Hubs;
using System.IO;
using System.Globalization;

namespace AirbnbShopApi.Controllers
{
    [Authorize]
    public class FinancialController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<FinancialController> _logger;

        public FinancialController(
            AppDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            IHubContext<NotificationHub> hubContext,
            IWebHostEnvironment environment,
            ILogger<FinancialController> logger)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _hubContext = hubContext;
            _environment = environment;
            _logger = logger;
        }

        private async Task LogAudit(string action, string entityType, int? entityId, string details)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                var auditLog = new AuditLog
                {
                    UserId = user.Id,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Details = details
                };
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IActionResult> Index(int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao carregar pedidos de recebimento.");
                return Unauthorized("Usuário não encontrado.");
            }

            var isFinancialUser = await _userManager.IsInRoleAsync(user, "Financeiro");

            if (isFinancialUser)
                return RedirectToAction(nameof(AdminIndex));

            var userApartments = await _context.UserApartments
                .Where(ua => ua.UserId == user.Id)
                .Select(ua => ua.Apartment)
                .ToListAsync();

            const int pageSize = 20;
            var totalTransactions = await _context.AdminTransactions
                .CountAsync(at => at.AdminId == user.Id);
            var totalPages = (int)Math.Ceiling(totalTransactions / (double)pageSize);

            var transactions = await _context.AdminTransactions
                .Include(at => at.Apartment)
                .Where(at => at.AdminId == user.Id)
                .OrderByDescending(at => at.RequestDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var retentionRate = await _context.RetentionRates
                .OrderByDescending(rr => rr.EffectiveDate)
                .FirstOrDefaultAsync() ?? new RetentionRate { Rate = 0.05m }; // Ajustado para 5% como exemplo
            ViewBag.RetentionRate = retentionRate.Rate;

            ViewBag.UserApartments = userApartments;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            return View(transactions);
        }

        [Authorize(Roles = "Financeiro")]
        public async Task<IActionResult> AdminIndex(int productPage = 1, int payoutPage = 1, int apartmentPage = 1, int userPage = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao carregar gestão financeira.");
                return Unauthorized("Usuário não encontrado.");
            }

            const int pageSize = 20;

            var totalProductTransactions = await _context.Transactions.CountAsync();
            var totalProductPages = (int)Math.Ceiling(totalProductTransactions / (double)pageSize);
            var productTransactions = await _context.Transactions
                .Include(t => t.Product)
                .ThenInclude(p => p.Apartment)
                .OrderByDescending(t => t.RequestDate)
                .Skip((productPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            foreach (var pt in productTransactions.Where(t => t.PaymentStatus == "Pending" && !t.PaymentId.StartsWith("PIX_")))
            {
                bool confirmed = await _paymentService.ConfirmPayment(pt.PaymentId);
                if (confirmed)
                {
                    pt.PaymentStatus = "Confirmed";
                    var apartment = pt.Product.Apartment;
                    apartment.Balance += pt.Amount;
                    await _context.SaveChangesAsync();

                    var apartmentOwners = await _context.UserApartments
                        .Where(ua => ua.ApartmentId == apartment.Id)
                        .Select(ua => ua.UserId)
                        .ToListAsync();
                    foreach (var ownerId in apartmentOwners)
                    {
                        await _hubContext.Clients.User(ownerId).SendAsync("ReceiveNotification", $"Pagamento de R${pt.Amount} para {pt.Product.Name} confirmado automaticamente.");
                    }
                }
            }

            var totalPayoutTransactions = await _context.AdminTransactions.CountAsync();
            var totalPayoutPages = (int)Math.Ceiling(totalPayoutTransactions / (double)pageSize);
            var transactions = await _context.AdminTransactions
                .Include(at => at.Apartment)
                .OrderByDescending(at => at.RequestDate)
                .Skip((payoutPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var totalApartments = await _context.Apartments.CountAsync();
            var totalApartmentPages = (int)Math.Ceiling(totalApartments / (double)pageSize);
            var allApartments = await _context.Apartments
                .OrderByDescending(a => a.Id)
                .Skip((apartmentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var totalUsers = await _userManager.Users.CountAsync();
            var totalUserPages = (int)Math.Ceiling(totalUsers / (double)pageSize);
            var users = await _userManager.Users
                .OrderByDescending(u => u.Id)
                .Skip((userPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userApartments = await _context.UserApartments
                .Where(ua => ua.UserId == user.Id)
                .Select(ua => ua.Apartment)
                .ToListAsync();

            var retentionRate = await _context.RetentionRates
                .OrderByDescending(rr => rr.EffectiveDate)
                .FirstOrDefaultAsync() ?? new RetentionRate { Rate = 0.05m }; // Ajustado para 5% como exemplo
            ViewBag.RetentionRate = retentionRate.Rate;

            var payoutData = transactions.Select(t => new
            {
                Id = t.Id,
                Amount = (double)t.Amount,
                NetAmount = (double)t.NetAmount,
                RetentionAmount = (double)t.RetentionAmount,
                ApartmentName = t.Apartment.Name
            }).ToList();

            ViewBag.PayoutTransactions = payoutData;

            ViewBag.UserApartments = userApartments;
            ViewBag.Users = users;
            ViewBag.AllApartments = allApartments;
            ViewBag.ProductTransactions = productTransactions;
            ViewBag.ProductPage = productPage;
            ViewBag.ProductTotalPages = totalProductPages;
            ViewBag.PayoutPage = payoutPage;
            ViewBag.PayoutTotalPages = totalPayoutPages;
            ViewBag.ApartmentPage = apartmentPage;
            ViewBag.ApartmentTotalPages = totalApartmentPages;
            ViewBag.UserPage = userPage;
            ViewBag.UserTotalPages = totalUserPages;
            return View("AdminIndex", transactions);
        }

        [Authorize(Roles = "Financeiro")]
        public async Task<IActionResult> Reports()
        {
            var totalBalance = await _context.Apartments.SumAsync(a => a.Balance);
            ViewBag.TotalBalance = totalBalance;
            return View();
        }

        [Authorize(Roles = "Financeiro")]
        public async Task<IActionResult> TransactionReport(DateTime? startDate, DateTime? endDate, string status, string type)
        {
            var transactions = new List<object>();

            if (type == "Product")
            {
                var query = _context.Transactions.AsQueryable();

                if (startDate.HasValue)
                    query = query.Where(t => t.RequestDate >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(t => t.RequestDate <= endDate.Value);
                if (!string.IsNullOrEmpty(status))
                    query = query.Where(t => t.PaymentStatus == status);

                transactions = (await query
                    .Include(t => t.Product)
                    .ThenInclude(p => p.Apartment)
                    .OrderByDescending(t => t.RequestDate)
                    .ToListAsync())
                    .Cast<object>()
                    .ToList();
            }
            else // Payout
            {
                var query = _context.AdminTransactions.AsQueryable();

                if (startDate.HasValue)
                    query = query.Where(t => t.RequestDate >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(t => t.RequestDate <= endDate.Value);
                if (!string.IsNullOrEmpty(status) && Enum.TryParse<PayoutStatus>(status, out var payoutStatus))
                    query = query.Where(t => t.Status == payoutStatus);

                transactions = (await query
                    .Include(t => t.Apartment)
                    .OrderByDescending(t => t.RequestDate)
                    .ToListAsync())
                    .Cast<object>()
                    .ToList();
            }

            if (Request.Headers["Accept"] == "text/csv")
            {
                var csv = new StringBuilder();
                csv.AppendLine(type == "Product"
                    ? "ID,Produto,Comprador,Valor,Data,Status"
                    : "ID,Apartamento,Valor,Data,Status,Motivo da Falha,Comprovante");
                
                foreach (var t in transactions)
                {
                    if (t is Transaction pt)
                        csv.AppendLine($"{pt.Id},{pt.Product.Name},{pt.BuyerTelegramId},{pt.Amount},{pt.RequestDate:yyyy-MM-dd HH:mm},{pt.PaymentStatus}");
                    else if (t is AdminTransaction at)
                        csv.AppendLine($"{at.Id},{at.Apartment.Name},{at.Amount},{at.RequestDate:yyyy-MM-dd HH:mm},{at.Status},{at.FailureReason ?? ""},{(at.ReceiptUrl != null ? at.ReceiptUrl : "Não")}");
                }

                return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"Relatorio_{type}_{DateTime.Now:yyyyMMdd}.csv");
            }

            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            ViewBag.Status = status;
            ViewBag.Type = type;
            return View("TransactionReport", transactions);
        }

        [Authorize(Roles = "Financeiro")]
        public async Task<IActionResult> AuditLog(int page = 1, DateTime? startDate = null, DateTime? endDate = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao carregar logs de auditoria.");
                return Unauthorized("Usuário não encontrado.");
            }

            const int pageSize = 20;
            var query = _context.AuditLogs.AsQueryable();

            if (startDate.HasValue)
                query = query.Where(al => al.Timestamp >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(al => al.Timestamp <= endDate.Value);

            var totalLogs = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalLogs / (double)pageSize);

            var logs = await query
                .Include(al => al.User)
                .OrderByDescending(al => al.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;

            return View(logs);
        }

        [Authorize(Roles = "Financeiro")]
        public async Task<IActionResult> ExportData()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao exportar dados.");
                return Unauthorized("Usuário não encontrado.");
            }

            var csv = new StringBuilder();

            csv.AppendLine("Apartamentos:");
            csv.AppendLine("ID,Nome,Identificador,Saldo,Chave Pix");
            var apartments = await _context.Apartments.ToListAsync();
            foreach (var apt in apartments)
            {
                csv.AppendLine($"{apt.Id},{apt.Name},{apt.Identifier},{apt.Balance},{apt.PixKey}");
            }
            csv.AppendLine();

            csv.AppendLine("Transações de Produtos:");
            csv.AppendLine("ID,Produto,Comprador,Valor,Data,Status");
            var productTransactions = await _context.Transactions
                .Include(t => t.Product)
                .ToListAsync();
            foreach (var pt in productTransactions)
            {
                csv.AppendLine($"{pt.Id},{pt.Product.Name},{pt.BuyerTelegramId},{pt.Amount},{pt.RequestDate:yyyy-MM-dd HH:mm},{pt.PaymentStatus}");
            }
            csv.AppendLine();

            csv.AppendLine("Solicitações de Payout:");
            csv.AppendLine("ID,Apartamento,Valor,Data,Status,Motivo da Falha,Comprovante");
            var payoutTransactions = await _context.AdminTransactions
                .Include(at => at.Apartment)
                .ToListAsync();
            foreach (var at in payoutTransactions)
            {
                csv.AppendLine($"{at.Id},{at.Apartment.Name},{at.Amount},{at.RequestDate:yyyy-MM-dd HH:mm},{at.Status},{at.FailureReason ?? ""},{(at.ReceiptUrl != null ? at.ReceiptUrl : "Não")}");
            }
            csv.AppendLine();

            csv.AppendLine("Logs de Auditoria:");
            csv.AppendLine("ID,Usuário,Ação,Tipo de Entidade,ID da Entidade,Detalhes,Data/Hora");
            var auditLogs = await _context.AuditLogs
                .Include(al => al.User)
                .ToListAsync();
            foreach (var log in auditLogs)
            {
                csv.AppendLine($"{log.Id},{log.User.UserName},{log.Action},{log.EntityType},{log.EntityId},{log.Details},{log.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }

            await LogAudit("ExportData", "System", null, "Exportação de todos os dados em CSV");

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"Exportacao_Dados_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        [Authorize(Roles = "Financeiro")]
        [HttpGet]
        public IActionResult ImportApartments()
        {
            return View();
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> ImportApartments(IFormFile csvFile)
        {
            if (csvFile == null || csvFile.Length == 0)
            {
                ModelState.AddModelError("", "Por favor, selecione um arquivo CSV.");
                return View();
            }

            if (!csvFile.FileName.EndsWith(".csv"))
            {
                ModelState.AddModelError("", "O arquivo deve ser no formato CSV.");
                return View();
            }

            using var reader = new StreamReader(csvFile.OpenReadStream());
            string? line;
            int lineNumber = 0;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                if (lineNumber == 1) continue;

                var values = line.Split(',');
                if (values.Length != 4)
                {
                    ModelState.AddModelError("", $"Erro na linha {lineNumber}: Formato inválido.");
                    continue;
                }

                if (!decimal.TryParse(values[2], out decimal balance))
                {
                    ModelState.AddModelError("", $"Erro na linha {lineNumber}: Saldo inválido.");
                    continue;
                }

                var apartment = new Apartment
                {
                    Name = values[0].Trim(),
                    Identifier = values[1].Trim(),
                    Balance = balance,
                    PixKey = values[3].Trim()
                };

                _context.Apartments.Add(apartment);
                await _context.SaveChangesAsync();
                await LogAudit("ImportApartment", "Apartment", apartment.Id, $"Apartamento {apartment.Name} importado via CSV");
            }

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpGet]
        public IActionResult CreateUser()
        {
            return View();
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> CreateUser(string username, string email, string password)
        {
            var user = new ApplicationUser { UserName = username, Email = email };
            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await LogAudit("CreateUser", "ApplicationUser", null, $"Usuário {username} criado");
                return RedirectToAction(nameof(AdminIndex));
            }
            
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }

        [Authorize(Roles = "Financeiro")]
        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();
            return View(user);
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> EditUser(string id, string username, string email)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            user.UserName = username;
            user.Email = email;
            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                await LogAudit("EditUser", "ApplicationUser", null, $"Usuário {username} editado");
                return RedirectToAction(nameof(AdminIndex));
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(user);
        }

        [Authorize(Roles = "Financeiro")]
        [HttpGet]
        public IActionResult CreateApartment()
        {
            return View();
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> CreateApartment(string name, string identifier, decimal balance, string pixKey)
        {
            var apartment = new Apartment { Name = name, Identifier = identifier, Balance = balance, PixKey = pixKey };
            _context.Apartments.Add(apartment);
            await _context.SaveChangesAsync();

            await LogAudit("CreateApartment", "Apartment", apartment.Id, $"Apartamento {name} criado com saldo R${balance}");

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpGet]
        public async Task<IActionResult> EditApartment(int id)
        {
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
                return NotFound();
            return View(apartment);
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> EditApartment(Apartment apartment)
        {
            var existingApartment = await _context.Apartments.FindAsync(apartment.Id);
            if (existingApartment == null)
                return NotFound();

            existingApartment.Name = apartment.Name;
            existingApartment.Identifier = apartment.Identifier;
            existingApartment.Balance = apartment.Balance;
            existingApartment.PixKey = apartment.PixKey;
            await _context.SaveChangesAsync();

            await LogAudit("EditApartment", "Apartment", apartment.Id, $"Apartamento {apartment.Name} editado");

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> DeleteApartment(int id)
        {
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
                return NotFound();

            _context.Apartments.Remove(apartment);
            await _context.SaveChangesAsync();

            await LogAudit("DeleteApartment", "Apartment", id, $"Apartamento {apartment.Name} excluído");

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> LinkUserToApartment(string userId, int apartmentId)
        {
            if (!await _context.Users.AnyAsync(u => u.Id == userId) || !await _context.Apartments.AnyAsync(a => a.Id == apartmentId))
                return BadRequest("Usuário ou apartamento inválido.");

            var userApartment = new UserApartment { UserId = userId, ApartmentId = apartmentId };
            _context.UserApartments.Add(userApartment);
            await _context.SaveChangesAsync();

            var user = await _userManager.FindByIdAsync(userId);
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (user != null && apartment != null)
            {
                await LogAudit("LinkUserToApartment", "UserApartment", null, $"Usuário {user.UserName} vinculado ao apartamento {apartment.Name}");
            }

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> UpdateRetentionRate(string rate)
        {
            if (!decimal.TryParse(rate, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rateValue))
            {
                return BadRequest("Por favor, insira um valor numérico válido (ex.: 0,05 ou 0.05 para 5%).");
            }

            if (rateValue < 0 || rateValue > 1)
            {
                return BadRequest("A taxa deve estar entre 0 e 1 (ex.: 0,05 ou 0.05 para 5%).");
            }

            var retentionRate = new RetentionRate { Rate = rateValue };
            _context.RetentionRates.Add(retentionRate);
            await _context.SaveChangesAsync();

            await LogAudit("UpdateRetentionRate", "RetentionRate", retentionRate.Id, $"Taxa de retenção atualizada para {rateValue * 100}%");
            return RedirectToAction(nameof(AdminIndex));
        }

        [HttpPost]
        public async Task<IActionResult> RequestPayout(int apartmentId, string amountStr)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _context.UserApartments.AnyAsync(ua => ua.UserId == user.Id && ua.ApartmentId == apartmentId))
            {
                _logger.LogWarning("Usuário não encontrado ou sem permissão para o apartamento: {ApartmentId}", apartmentId);
                return Forbid();
            }

            // Converte a string para decimal, aceitando vírgula ou ponto
            amountStr = amountStr.Replace(",", ".");
            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
            {
                _logger.LogWarning("Valor inválido para solicitação de recebimento: {AmountStr}", amountStr);
                TempData["Error"] = "Por favor, insira um valor válido (ex.: 20,50 ou 20.50).";
                return RedirectToAction(nameof(Index));
            }

            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null || apartment.Balance < amount)
            {
                _logger.LogWarning("Saldo insuficiente ou apartamento inválido: {ApartmentId}, Saldo: {Balance}, Solicitado: {Amount}", apartmentId, apartment?.Balance, amount);
                TempData["Error"] = "Saldo insuficiente ou apartamento inválido.";
                return RedirectToAction(nameof(Index));
            }

            var retentionRate = await CalculateRetentionRate();
            var retentionAmount = amount * retentionRate;
            var netAmount = amount - retentionAmount;

            var transaction = new AdminTransaction
            {
                ApartmentId = apartmentId,
                AdminId = user.Id,
                Amount = amount,
                NetAmount = netAmount,
                RetentionAmount = retentionAmount,
                PixKey = apartment.PixKey,
                RequestDate = DateTime.UtcNow,
                Status = PayoutStatus.Solicitado
            };

            apartment.Balance -= amount;
            _context.AdminTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            await LogAudit("RequestPayout", "AdminTransaction", transaction.Id, $"Solicitado payout de R${amount} (líquido: R${netAmount}, taxa: R${retentionAmount}) para {apartment.Name}");

            var financialUsers = await _userManager.GetUsersInRoleAsync("Financeiro");
            foreach (var financialUser in financialUsers)
            {
                await _hubContext.Clients.User(financialUser.Id).SendAsync("ReceiveNotification", $"Novo pedido de payout de R${amount} (líquido: R${netAmount}) para {apartment.Name} (ID: {transaction.Id})");
            }

            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> ApprovePayout(int transactionId, IFormFile receipt)
        {
            var transaction = await _context.AdminTransactions
                .Include(at => at.Apartment)
                .FirstOrDefaultAsync(at => at.Id == transactionId);
            if (transaction == null)
            {
                _logger.LogWarning("Transação não encontrada: {TransactionId}", transactionId);
                return NotFound();
            }

            string? receiptUrl = null;
            if (receipt != null && receipt.Length > 0)
            {
                _logger.LogInformation("Comprovante recebido - Nome: {FileName}, Tamanho: {Length} bytes", receipt.FileName, receipt.Length);
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "_uploads");
                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(receipt.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                Directory.CreateDirectory(uploadsFolder);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await receipt.CopyToAsync(fileStream);
                }
                receiptUrl = "/_uploads/" + uniqueFileName;
                _logger.LogInformation("Comprovante salvo em: {FilePath}, URL: {ReceiptUrl}", filePath, receiptUrl);

                // Remove o comprovante antigo, se existir
                if (!string.IsNullOrEmpty(transaction.ReceiptUrl))
                {
                    var oldFilePath = Path.Combine(_environment.WebRootPath, transaction.ReceiptUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                        _logger.LogInformation("Comprovante antigo removido: {OldFilePath}", oldFilePath);
                    }
                }
            }

            transaction.ReceiptUrl = receiptUrl;
            transaction.Status = PayoutStatus.Processado;
            await _context.SaveChangesAsync();

            await LogAudit("ApprovePayout", "AdminTransaction", transaction.Id, $"Payout de R${transaction.Amount} (líquido: R${transaction.NetAmount}) para {transaction.Apartment.Name} aprovado");

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> RejectPayout(int transactionId, string failureReason)
        {
            var transaction = await _context.AdminTransactions
                .Include(at => at.Apartment)
                .FirstOrDefaultAsync(at => at.Id == transactionId);
            if (transaction == null)
            {
                _logger.LogWarning("Transação não encontrada: {TransactionId}", transactionId);
                return NotFound();
            }

            transaction.Status = PayoutStatus.Falha;
            transaction.FailureReason = failureReason;
            var apartment = transaction.Apartment;
            apartment.Balance += transaction.Amount;
            await _context.SaveChangesAsync();

            await LogAudit("RejectPayout", "AdminTransaction", transaction.Id, $"Payout de R${transaction.Amount} (líquido: R${transaction.NetAmount}) para {apartment.Name} rejeitado. Motivo: {failureReason}");

            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> ConfirmPayment(int transactionId)
        {
            var transaction = await _context.Transactions
                .Include(t => t.Product)
                .ThenInclude(p => p.Apartment)
                .FirstOrDefaultAsync(t => t.Id == transactionId);
            if (transaction == null)
            {
                _logger.LogWarning("Transação não encontrada: {TransactionId}", transactionId);
                return NotFound();
            }

            bool isManualPix = transaction.PaymentId.StartsWith("PIX_");
            bool confirmed = isManualPix || await _paymentService.ConfirmPayment(transaction.PaymentId);

            if (confirmed)
            {
                transaction.PaymentStatus = "Confirmed";
                var apartment = transaction.Product.Apartment;
                apartment.Balance += transaction.Amount;

                var apartmentOwners = await _context.UserApartments
                    .Where(ua => ua.ApartmentId == apartment.Id)
                    .Select(ua => ua.UserId)
                    .ToListAsync();
                foreach (var ownerId in apartmentOwners)
                {
                    await _hubContext.Clients.User(ownerId).SendAsync("ReceiveNotification", $"Pagamento de R${transaction.Amount} para {transaction.Product.Name} confirmado.");
                }

                await LogAudit("ConfirmPayment", "Transaction", transaction.Id, $"Pagamento de R${transaction.Amount} para {transaction.Product.Name} confirmado");
            }
            else
            {
                transaction.PaymentStatus = "Failed";
                await LogAudit("ConfirmPayment", "Transaction", transaction.Id, $"Falha ao confirmar pagamento de R${transaction.Amount} para {transaction.Product.Name}");
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(AdminIndex));
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> RejectPayment(int transactionId, string rejectionReason)
        {
            var transaction = await _context.Transactions
                .Include(t => t.Product)
                .ThenInclude(p => p.Apartment)
                .FirstOrDefaultAsync(t => t.Id == transactionId);
            if (transaction == null)
            {
                _logger.LogWarning("Transação não encontrada: {TransactionId}", transactionId);
                return NotFound();
            }

            if (!transaction.PaymentId.StartsWith("PIX_"))
            {
                _logger.LogWarning("Tentativa de rejeitar pagamento não-PIX manual: {TransactionId}", transactionId);
                return BadRequest("Somente transações PIX manuais podem ser rejeitadas manualmente.");
            }

            transaction.PaymentStatus = "Failed";
            transaction.RejectionReason = rejectionReason;
            transaction.Product.IsAvailable = true;

            await _context.SaveChangesAsync();
            await LogAudit("RejectPayment", "Transaction", transaction.Id, $"Pagamento de R${transaction.Amount} para {transaction.Product.Name} rejeitado. Motivo: {rejectionReason}");

            return RedirectToAction(nameof(AdminIndex));
        }

        private async Task<decimal> CalculateRetentionRate()
        {
            var latestRetentionRate = await _context.RetentionRates
                .OrderByDescending(rr => rr.EffectiveDate)
                .FirstOrDefaultAsync();

            return latestRetentionRate?.Rate ?? 0.05m; // 5% como padrão
        }
    }
}