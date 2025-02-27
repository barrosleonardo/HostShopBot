using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using AirbnbShopApi.Data;
using AirbnbShopApi.Models;

namespace AirbnbShopApi.Services
{
    public class BotService
    {
        private readonly TelegramBotClient _botClient;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Dictionary<string, UserState> _userStates = [];
        private readonly string _adminTelegramId = "1074263794"; // Substitua pelo seu Telegram ID
        private readonly string _imageBaseUrl; // URL base para imagens
        private readonly string _pixAdmin;

        public BotService(IConfiguration config, IServiceScopeFactory scopeFactory)
        {
            Console.WriteLine("Inicializando BotService...");
            _botClient = new TelegramBotClient(config["Telegram:BotToken"] ?? throw new ArgumentNullException("Telegram:BotToken não configurado"));
            _scopeFactory = scopeFactory;
            _imageBaseUrl = config["ImageBaseUrl"] ?? throw new ArgumentNullException("ImageBaseUrl não configurado no appsettings.json");
            _pixAdmin = config["PixAdmin"] ?? throw new ArgumentNullException("Pix não configurado no appsettings.json");
            Console.WriteLine($"BotService inicializado com sucesso. ImageBaseUrl: {_imageBaseUrl}");
        }

        public void Start()
        {
            Console.WriteLine("Iniciando o polling do bot...");
            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync
            );
            Console.WriteLine("Bot iniciado com polling.");
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Handle mensagens de texto
            if (update.Type == UpdateType.Message && update.Message?.Text != null)
            {
                var message = update.Message;
                var chatId = message.Chat.Id;
                var text = message.Text.Trim().ToLower();
                var userId = message.From!.Id.ToString();

                Console.WriteLine($"Mensagem recebida: {text} de {userId}");

                if (!_userStates.ContainsKey(userId))
                    _userStates[userId] = new UserState { TelegramId = userId };

                var state = _userStates[userId];

                // Ignorar mensagens enquanto espera confirmação
                if (state.CurrentStep == "ConfirmingPurchase" && !text.StartsWith("/start"))
                {
                    await botClient.SendTextMessageAsync(chatId, "Por favor, confirme ou cancele a compra usando os botões.");
                    return;
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

                    if (state.CurrentStep == "Idle" && !text.StartsWith("/"))
                    {
                        var apartment = await context.Apartments
                            .FirstOrDefaultAsync(a => a.Identifier == text, cancellationToken);
                        if (apartment == null)
                        {
                            await botClient.SendTextMessageAsync(chatId, "Apartamento não encontrado. Envie o código do apartamento (ex.: apt101).");
                            return;
                        }

                        state.ApartmentId = apartment.Id;
                        var products = await context.Products
                            .Where(p => p.ApartmentId == apartment.Id && p.IsAvailable)
                            .ToListAsync(cancellationToken);

                        if (products == null || !products.Any())
                        {
                            await botClient.SendTextMessageAsync(chatId, $"Bem-vindo ao {EscapeMarkdownV2(apartment.Name)}\\! Nenhum produto disponível no momento.");
                        }
                        else
                        {
                            var productList = string.Join("\n", products.Select(p => $"{p.Id} \\| {EscapeMarkdownV2(p.Name)} \\| R${p.Price:F2}"));
                            var welcomeMessage = $"**Bem\\-vindo ao {EscapeMarkdownV2(apartment.Name)}\\!**\n" +
                                                 $"**Produtos disponíveis:**\n\n" +
                                                 $"**ID \\| Nome \\| Preço**\n" +
                                                 $"{productList}\n\n" +
                                                 $"**Digite /buy ID para comprar\\.**\n";
                            await botClient.SendTextMessageAsync(
                                chatId,
                                welcomeMessage,
                                parseMode: ParseMode.MarkdownV2
                            );
                        }
                        state.CurrentStep = "SelectingProduct";
                    }
                    else if (text == "/start")
                    {
                        await botClient.SendTextMessageAsync(chatId, "Envie o código do apartamento (ex.: apt101) para começar.");
                        state.CurrentStep = "Idle";
                    }
                    else if (text.StartsWith("/buy") && state.CurrentStep == "SelectingProduct")
                    {
                        var buyParts = text.Split(" ");
                        if (buyParts.Length != 2 || !int.TryParse(buyParts[1], out var productId))
                        {
                            await botClient.SendTextMessageAsync(chatId, "Uso: /buy ID_do_produto");
                            return;
                        }

                        var product = await context.Products
                            .FirstOrDefaultAsync(p => p.Id == productId && p.IsAvailable, cancellationToken);
                        if (product == null)
                        {
                            await botClient.SendTextMessageAsync(chatId, "Produto não disponível.");
                            return;
                        }

                        Console.WriteLine($"Produto {product.Id} - {product.Name} está disponível: {product.IsAvailable}");

                        var inlineKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Sim", $"confirm_yes_{product.Id}") },
                            new[] { InlineKeyboardButton.WithCallbackData("Não", $"confirm_no_{product.Id}") }
                        });

                        var confirmationMessage = $"Você selecionou:\n" +
                                                  $"Nome: {EscapeMarkdownV2(product.Name)}\n" +
                                                  $"Preço: R${product.Price:F2}\n" +
                                                  "Confirme a compra:";

                        if (!string.IsNullOrEmpty(product.ImageUrl))
                        {
                            var imageUrl = $"{_imageBaseUrl}{product.ImageUrl}";
                            try
                            {
                                await botClient.SendPhotoAsync(chatId, InputFile.FromUri(imageUrl), caption: confirmationMessage, replyMarkup: inlineKeyboard);
                                Console.WriteLine($"Imagem enviada com sucesso: {imageUrl}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Erro ao enviar imagem: {imageUrl}. Exceção: {ex.Message}");
                                await botClient.SendTextMessageAsync(chatId, "Erro ao carregar a imagem do produto, mas você ainda pode confirmar a compra:\n" + confirmationMessage, replyMarkup: inlineKeyboard);
                            }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, confirmationMessage, replyMarkup: inlineKeyboard);
                            Console.WriteLine("Mensagem de confirmação enviada sem imagem.");
                        }

                        state.CurrentStep = "ConfirmingPurchase";
                        state.SelectedProductId = productId;
                    }
                }
            }
            // Handle callbacks dos botões
            else if (update.Type == UpdateType.CallbackQuery)
            {
                var callbackQuery = update.CallbackQuery;
                var chatId = callbackQuery!.Message!.Chat.Id;
                var userId = callbackQuery.From.Id.ToString();
                var data = callbackQuery.Data;

                if (!_userStates.ContainsKey(userId) || _userStates[userId].CurrentStep != "ConfirmingPurchase")
                {
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                    return;
                }

                var state = _userStates[userId];
                int productId = state.SelectedProductId ?? -1;

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

                    var product = await context.Products
                        .FirstOrDefaultAsync(p => p.Id == productId && p.IsAvailable, cancellationToken);

                    if (product == null)
                    {
                        await botClient.SendTextMessageAsync(chatId, "Produto não disponível mais. Escolha outro com /buy ID.");
                        state.CurrentStep = "SelectingProduct";
                        state.SelectedProductId = null;
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                        return;
                    }

                    if (data == $"confirm_yes_{productId}")
                    {
                        var paymentId = await paymentService.CreatePayment(product.Price, "simulated_pix_key");
                        var transaction = new Transaction
                        {
                            ProductId = product.Id,
                            BuyerTelegramId = userId,
                            Amount = product.Price,
                            PaymentId = paymentId,
                            PaymentStatus = "Pending",
                            RequestDate = DateTime.UtcNow
                        };
                        context.Transactions.Add(transaction);
                        product.IsAvailable = false;
                        await context.SaveChangesAsync(cancellationToken);

                        var isPixDefault = paymentId.StartsWith("PIX_");
                        var messageText = isPixDefault
                            ? $"Compra confirmada!\n" +
                              $"Produto: {EscapeMarkdownV2(product.Name)}\n" +
                              $"Preço: R${product.Price:F2}\n" +
                              $"Código do Cadeado: {product.LockCode}\n\n"
                            : $"Compra confirmada!\n" +
                              $"Produto: {EscapeMarkdownV2(product.Name)}\n" +
                              $"Preço: R${product.Price:F2}\n" +
                              $"Pagamento criado (ID: {paymentId}).\n" +
                              "Aguarde a confirmação para receber o código do cadeado.\n\n";

                        await botClient.SendTextMessageAsync(chatId, messageText);
                        await botClient.SendTextMessageAsync(_adminTelegramId, $"Nova transação: {EscapeMarkdownV2(product.Name)} comprado por R${product.Price:F2} (ID: {transaction.Id}, PaymentId: {paymentId}).");

                        if (isPixDefault)
                        {
                            var complementoPix =
                                $"Por favor, faça o pix no valor da compra para o nosso financeiro:\n" +
                                $"{_pixAdmin}\n\n" +
                                "Ao fazer o pix, não esqueça de adicionar a descrição abaixo:\n";
                            await botClient.SendTextMessageAsync(chatId, complementoPix);
                            await botClient.SendTextMessageAsync(chatId, paymentId);
                        }

                        if (!isPixDefault)
                        {
                            bool confirmed = await paymentService.ConfirmPayment(paymentId);
                            if (confirmed)
                            {
                                transaction.PaymentStatus = "Confirmed";
                                await context.SaveChangesAsync(cancellationToken);
                                await botClient.SendTextMessageAsync(chatId, $"Pagamento confirmado! Código do cadeado: {product.LockCode}");
                            }
                        }

                        state.CurrentStep = "Idle";
                        state.SelectedProductId = null;
                    }
                    else if (data == $"confirm_no_{productId}")
                    {
                        await botClient.SendTextMessageAsync(chatId, "Compra cancelada. Digite /buy ID para escolher outro produto.");
                        state.CurrentStep = "SelectingProduct";
                        state.SelectedProductId = null;
                    }

                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                }
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Erro no bot: {exception.Message}");
            return Task.CompletedTask;
        }

        private string EscapeMarkdownV2(string text)
        {
            // Lista de caracteres que precisam de escape em MarkdownV2
            char[] reservedChars = { '_', '*', '[', ']', '(', ')', '~', '`', '#', '+', '-', '.', '!', '{', '}', '|', '>' };
            foreach (var c in reservedChars)
            {
                text = text.Replace(c.ToString(), $"\\{c}");
            }
            return text;
        }
    }

    public class UserState
    {
        public string TelegramId { get; set; } = null!;
        public string CurrentStep { get; set; } = "Idle";
        public int ApartmentId { get; set; }
        public int? SelectedProductId { get; set; }
    }
}