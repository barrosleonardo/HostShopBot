using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AirbnbShopApi.Data;
using AirbnbShopApi.Models;
using System;
using System.Globalization;

namespace AirbnbShopApi.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(AppDbContext context, UserManager<ApplicationUser> userManager, ILogger<DashboardController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [Authorize(Roles = "Financeiro")]
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao carregar dashboard.");
                return Unauthorized("Usuário não encontrado.");
            }

            startDate ??= DateTime.Now.AddDays(-7);
            endDate ??= DateTime.Now;

            endDate = endDate.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            _logger.LogInformation("Período do dashboard - StartDate: {StartDate}, EndDate: {EndDate}", startDate, endDate);

            var totalBalance = await _context.Apartments.SumAsync(a => a.Balance);
            var retentionAmount = await CalculateRetentionAmount(startDate.Value, endDate.Value);
            var pendingTransactions = await _context.Transactions.CountAsync(t => t.PaymentStatus == "Pending");
            var pendingPayouts = await _context.AdminTransactions.CountAsync(t => t.Status == PayoutStatus.Solicitado);

            var productTransactions = await _context.Transactions
                .Where(t => t.RequestDate >= startDate.Value && t.RequestDate <= endDate.Value)
                .GroupBy(t => t.RequestDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.Amount) })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var payoutTransactions = await _context.AdminTransactions
                .Where(t => t.RequestDate >= startDate.Value && t.RequestDate <= endDate.Value)
                .GroupBy(t => t.RequestDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.NetAmount) })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var productTransactionDates = productTransactions.Select(t => t.Date.ToString("dd/MM/yyyy")).ToList();
            var productTransactionValues = productTransactions.Select(t => (double)t.Total).ToList();
            var payoutTransactionDates = payoutTransactions.Select(t => t.Date.ToString("dd/MM/yyyy")).ToList();
            var payoutTransactionValues = payoutTransactions.Select(t => (double)t.Total).ToList();

            var culture = CultureInfo.GetCultureInfo("pt-BR");
            ViewBag.TotalBalance = totalBalance.ToString("C2", culture);
            ViewBag.RetentionAmount = retentionAmount.ToString("C2", culture);
            ViewBag.PendingTransactions = pendingTransactions;
            ViewBag.PendingPayouts = pendingPayouts;
            ViewBag.ProductTransactionDates = productTransactionDates;
            ViewBag.ProductTransactionValues = productTransactionValues;
            ViewBag.PayoutTransactionDates = payoutTransactionDates;
            ViewBag.PayoutTransactionValues = payoutTransactionValues;
            ViewBag.StartDate = startDate.Value;
            ViewBag.EndDate = endDate.Value;

            return View();
        }

        public async Task<IActionResult> IndexOwner(int? selectedApartmentId, DateTime? startDate, DateTime? endDate)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado ao carregar dashboard do dono.");
                return Unauthorized("Usuário não encontrado.");
            }

            // Verifica se é um usuário financeiro; se for, redireciona para o dashboard financeiro
            var isFinancialUser = await _userManager.IsInRoleAsync(user, "Financeiro");
            if (isFinancialUser)
            {
                return RedirectToAction(nameof(Index));
            }

            // Obtém os apartamentos do dono
            var userApartments = await _context.UserApartments
                .Where(ua => ua.UserId == user.Id)
                .Select(ua => ua.Apartment)
                .ToListAsync();

            if (!userApartments.Any())
            {
                _logger.LogWarning("Usuário {UserId} não possui apartamentos associados.", user.Id);
                return BadRequest("Você não possui apartamentos associados.");
            }

            // Se nenhum apartamento foi selecionado, redireciona para selecionar um
            if (!selectedApartmentId.HasValue)
            {
                ViewBag.UserApartments = userApartments;
                return View("SelectApartment");
            }

            // Valida se o apartamento pertence ao dono
            var selectedApartment = userApartments.FirstOrDefault(a => a.Id == selectedApartmentId.Value);
            if (selectedApartment == null)
            {
                _logger.LogWarning("Apartamento {ApartmentId} não pertence ao usuário {UserId}.", selectedApartmentId, user.Id);
                return BadRequest("Apartamento inválido.");
            }

            startDate ??= DateTime.Now.AddDays(-7);
            endDate ??= DateTime.Now;

            endDate = endDate.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            _logger.LogInformation("Período do dashboard do dono - ApartmentId: {ApartmentId}, StartDate: {StartDate}, EndDate: {EndDate}", selectedApartmentId, startDate, endDate);

            // Saldo total do apartamento selecionado
            var totalBalance = selectedApartment.Balance;

            // Saldo de retenção do apartamento selecionado
            var retentionAmount = await _context.AdminTransactions
                .Where(at => at.ApartmentId == selectedApartmentId.Value && 
                             at.RequestDate >= startDate.Value && 
                             at.RequestDate <= endDate.Value && 
                             (at.Status == PayoutStatus.Processado || at.Status == PayoutStatus.Solicitado))
                .SumAsync(at => at.RetentionAmount);

            // Transações pendentes de produtos do apartamento
            var pendingTransactions = await _context.Transactions
                .Where(t => t.Product.ApartmentId == selectedApartmentId.Value && t.PaymentStatus == "Pending")
                .CountAsync();

            // Solicitações pendentes de payouts do apartamento
            var pendingPayouts = await _context.AdminTransactions
                .Where(t => t.ApartmentId == selectedApartmentId.Value && t.Status == PayoutStatus.Solicitado)
                .CountAsync();

            // Dados para o gráfico de transações de produtos do apartamento
            var productTransactions = await _context.Transactions
                .Where(t => t.Product.ApartmentId == selectedApartmentId.Value && 
                            t.RequestDate >= startDate.Value && 
                            t.RequestDate <= endDate.Value)
                .GroupBy(t => t.RequestDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.Amount) })
                .OrderBy(x => x.Date)
                .ToListAsync();

            // Dados para o gráfico de solicitações de payouts do apartamento
            var payoutTransactions = await _context.AdminTransactions
                .Where(t => t.ApartmentId == selectedApartmentId.Value && 
                            t.RequestDate >= startDate.Value && 
                            t.RequestDate <= endDate.Value)
                .GroupBy(t => t.RequestDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.NetAmount) })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var productTransactionDates = productTransactions.Select(t => t.Date.ToString("dd/MM/yyyy")).ToList();
            var productTransactionValues = productTransactions.Select(t => (double)t.Total).ToList();
            var payoutTransactionDates = payoutTransactions.Select(t => t.Date.ToString("dd/MM/yyyy")).ToList();
            var payoutTransactionValues = payoutTransactions.Select(t => (double)t.Total).ToList();

            var culture = CultureInfo.GetCultureInfo("pt-BR");
            ViewBag.TotalBalance = totalBalance.ToString("C2", culture);
            ViewBag.RetentionAmount = retentionAmount.ToString("C2", culture);
            ViewBag.PendingTransactions = pendingTransactions;
            ViewBag.PendingPayouts = pendingPayouts;
            ViewBag.ProductTransactionDates = productTransactionDates;
            ViewBag.ProductTransactionValues = productTransactionValues;
            ViewBag.PayoutTransactionDates = payoutTransactionDates;
            ViewBag.PayoutTransactionValues = payoutTransactionValues;
            ViewBag.StartDate = startDate.Value;
            ViewBag.EndDate = endDate.Value;
            ViewBag.SelectedApartmentId = selectedApartmentId.Value;
            ViewBag.UserApartments = userApartments;

            return View("IndexOwner");
        }

        private async Task<decimal> CalculateRetentionAmount(DateTime startDate, DateTime endDate)
        {
            var retentionAmount = await _context.AdminTransactions
                .Where(at => at.RequestDate >= startDate && at.RequestDate <= endDate && 
                            (at.Status == PayoutStatus.Processado || at.Status == PayoutStatus.Solicitado))
                .SumAsync(at => at.RetentionAmount);

            _logger.LogInformation("Retention Amount (raw decimal): {RetentionAmount}", retentionAmount);
            return retentionAmount;
        }
    }
}