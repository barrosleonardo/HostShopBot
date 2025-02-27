using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AirbnbShopApi.Data;
using AirbnbShopApi.Hubs;
using AirbnbShopApi.Models;

namespace AirbnbShopApi.Controllers
{
    [Authorize]
    public class ReconciliationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<ReconciliationController> _logger;

        public ReconciliationController(AppDbContext context, UserManager<ApplicationUser> userManager, IHubContext<NotificationHub> hubContext, ILogger<ReconciliationController> logger)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
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
                    Details = details,
                    Timestamp = DateTime.UtcNow
                };
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
        }

        [Authorize(Roles = "Financeiro")]
        [HttpGet]
        public IActionResult OpenProtocol(int transactionId, ReconciliationType? type = null)
        {
            ViewBag.TransactionId = transactionId;
            ViewBag.Type = type;
            return View();
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> OpenProtocol(int transactionId, ReconciliationType type, string initialComment)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            var transaction = await _context.Transactions
                .Include(t => t.Product)
                .ThenInclude(p => p.Apartment)
                .ThenInclude(a => a.UserApartments)
                .FirstOrDefaultAsync(t => t.Id == transactionId);
            if (transaction == null)
            {
                return NotFound("Transação não encontrada.");
            }

            var ownerId = transaction.Product.Apartment.UserApartments.FirstOrDefault()?.UserId;
            if (string.IsNullOrEmpty(ownerId))
            {
                return BadRequest("Nenhum dono associado à transação.");
            }

            var protocol = new ReconciliationProtocol
            {
                TransactionId = transactionId,
                Type = type,
                Status = ProtocolStatus.Aberto,
                InitiatorId = user.Id,
                OwnerId = ownerId,
                CreatedDate = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(initialComment))
            {
                protocol.Comments.Add(new ProtocolComment
                {
                    UserId = user.Id,
                    Comment = initialComment,
                    CommentDate = DateTime.UtcNow
                });
            }

            _context.ReconciliationProtocols.Add(protocol);
            await _context.SaveChangesAsync();

            await LogAudit("OpenProtocol", "ReconciliationProtocol", protocol.Id, $"Protocolo aberto para transação {transactionId}, tipo {type}");
            await _hubContext.Clients.User(ownerId).SendAsync("ReceiveNotification", $"Novo protocolo de conciliação aberto (ID: {protocol.Id}) pelo financeiro para a transação {transactionId}.");

            return RedirectToAction("ViewProtocol", new { id = protocol.Id });
        }

        [HttpGet]
        public async Task<IActionResult> ViewProtocol(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            var protocol = await _context.ReconciliationProtocols
                .Include(p => p.Comments)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (protocol == null)
            {
                return NotFound("Protocolo não encontrado.");
            }

            var isFinancial = await _userManager.IsInRoleAsync(user, "Financeiro");
            if (protocol.InitiatorId != user.Id && protocol.OwnerId != user.Id && !isFinancial)
            {
                return Forbid("Você não tem permissão para visualizar este protocolo.");
            }

            return View(protocol);
        }

        [HttpPost]
        public async Task<IActionResult> AddComment(int protocolId, string comment)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            var protocol = await _context.ReconciliationProtocols
                .Include(p => p.Comments)
                .FirstOrDefaultAsync(p => p.Id == protocolId);
            if (protocol == null)
            {
                return NotFound("Protocolo não encontrado.");
            }

            var isFinancial = await _userManager.IsInRoleAsync(user, "Financeiro");
            if (protocol.InitiatorId != user.Id && protocol.OwnerId != user.Id && !isFinancial)
            {
                return Forbid("Você não tem permissão para comentar neste protocolo.");
            }

            var newComment = new ProtocolComment
            {
                ReconciliationProtocolId = protocolId,
                UserId = user.Id,
                Comment = comment,
                CommentDate = DateTime.UtcNow
            };

            _context.ProtocolComments.Add(newComment);
            protocol.Status = ProtocolStatus.EmProgresso; // Atualiza para "Em Progresso" ao adicionar comentário
            await _context.SaveChangesAsync();

            var recipientId = protocol.InitiatorId == user.Id ? protocol.OwnerId : protocol.InitiatorId;
            await _hubContext.Clients.User(recipientId).SendAsync("ReceiveNotification", $"Novo comentário no protocolo {protocolId} por {user.UserName}.");
            await LogAudit("AddComment", "ProtocolComment", newComment.Id, $"Comentário adicionado ao protocolo {protocolId}: {comment}");

            return RedirectToAction("ViewProtocol", new { id = protocolId });
        }

        [Authorize(Roles = "Financeiro")]
        [HttpPost]
        public async Task<IActionResult> CloseProtocol(int protocolId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized("Usuário não encontrado.");
            }

            var protocol = await _context.ReconciliationProtocols
                .FirstOrDefaultAsync(p => p.Id == protocolId);
            if (protocol == null)
            {
                return NotFound("Protocolo não encontrado.");
            }

            protocol.Status = ProtocolStatus.Fechado;
            await _context.SaveChangesAsync();

            await _hubContext.Clients.User(protocol.OwnerId).SendAsync("ReceiveNotification", $"Protocolo {protocolId} fechado pelo financeiro.");
            await LogAudit("CloseProtocol", "ReconciliationProtocol", protocolId, $"Protocolo {protocolId} fechado.");

            return RedirectToAction("ViewProtocol", new { id = protocolId });
        }
    }
}