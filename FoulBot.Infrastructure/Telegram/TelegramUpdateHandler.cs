﻿using FoulBot.Domain.Storage;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Infrastructure.Telegram;

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly ILogger<TelegramBotMessenger> _botMessengerLogger;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;
    private readonly IFoulMessageFactory _foulMessageFactory;
    private readonly FoulBotConfiguration _botConfiguration;
    private readonly DateTime _coldStarted = DateTime.UtcNow + TimeSpan.FromSeconds(2); // Make a delay on first startup so all the bots are properly initialized.
    private readonly IAllowedChatsProvider _allowedChatsProvider;
    private readonly TelegramBotClient _client;

    public TelegramUpdateHandler(
        ILogger<TelegramBotMessenger> botMessengerLogger, // TODO: Move to another factory.
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory,
        IFoulMessageFactory foulMessageFactory,
        FoulBotConfiguration botConfiguration,
        IAllowedChatsProvider allowedChatsProvider,
        TelegramBotClient client)
    {
        _botMessengerLogger = botMessengerLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _foulMessageFactory = foulMessageFactory;
        _botConfiguration = botConfiguration;
        _allowedChatsProvider = allowedChatsProvider;
        _client = client;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Initialized TelegramUpdateHandler with configuration {@Configuration}.", _botConfiguration);
    }

    private IScopedLogger Logger => _logger
        .AddScoped("BotId", _botConfiguration.BotId);

    private readonly HashSet<int> _pollingErrorCodes = [];
    private readonly object _pollingErrorLock = new();
    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // This thing happens for couple second every two days with Bad Gateway error during midnight.
        // Handle this separately.
        using var _ = Logger.BeginScope();

        if (exception is ApiRequestException arException)
        {
            if (_pollingErrorCodes.Contains(arException.ErrorCode))
                return Task.CompletedTask; // Ignore duplicate exceptions;

            lock (_pollingErrorLock)
            {
                if (_pollingErrorCodes.Contains(arException.ErrorCode))
                    return Task.CompletedTask; // Ignore duplicate exceptions;

                _pollingErrorCodes.Add(arException.ErrorCode);

                // Intentionally not awaited.
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    _logger.LogWarning(exception, "Polling error occurred during potential Telegram downtime.");
                    _pollingErrorCodes.Remove(arException.ErrorCode);
                }, cancellationToken);
            }
        }
        else
        {
            _logger.LogError(exception, "Polling error occurred.");
        }

        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        using var _ = Logger.BeginScope();
        _logger.LogDebug("Received update {@Update}.", update);

        if (update.Message?.Text == "$collect_garbage")
        {
            _logger.LogDebug("Collect garbage commant has been issued. Collecting garbage.");
            GC.Collect();
            return;
        }

        // Consider caching instances for every botClient instead of creating a lot of them.
        var botMessenger = new TelegramBotMessenger(_botMessengerLogger, botClient);

        if (DateTime.UtcNow < _coldStarted)
        {
            _logger.LogInformation("Handling update on cold start, delaying for 2 seconds.");
            await Task.Delay(2000, cancellationToken);
        }

        try
        {
            await HandleUpdateAsync(new(_botConfiguration.BotId, _botConfiguration.BotName), update, chat => _botFactory.JoinBotToChatAsync(botMessenger, chat, _botConfiguration), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle update {@Update}.", update);
        }
    }

    private async ValueTask HandleUpdateAsync(FoulBotId foulBotId, Update update, JoinBotToChatAsync botFactory, CancellationToken cancellationToken)
    {
        using var _ = Logger
            .AddScoped("BotId", foulBotId.BotId)
            .AddScoped("ChatId", update?.MyChatMember?.Chat?.Id ?? update?.Message?.Chat?.Id)
            .AddScoped("ChatName", update?.MyChatMember?.Chat?.Username ?? update?.Message?.Chat?.Username
                ?? update?.MyChatMember?.Chat?.Title ?? update?.Message?.Chat?.Title)
            .BeginScope();

        if (update == null)
        {
            _logger.LogError("Received null update from Telegram.");
            return;
        }

        if (update.Type == UpdateType.MyChatMember)
        {
            _logger.LogDebug("Received MyChatMember update, initiating bot change status.");

            if (update.MyChatMember?.NewChatMember?.User?.Username == null
                || update.MyChatMember.Chat?.Id == null)
            {
                _logger.LogWarning("MyChatMember update doesn't have required properties. Skipping handling.");
                return;
            }

            var member = update.MyChatMember.NewChatMember;
            var chatId = update.MyChatMember.Chat.Id.ToString();
            var invitedByUsername = update.MyChatMember.From?.Username; // Who invited / kicked the bot.

            if (member.Status == ChatMemberStatus.Left || member.Status == ChatMemberStatus.Kicked)
            {
                await _chatPool.KickBotFromChatAsync(
                    new(chatId), // Chat is never private when invited.
                    foulBotId,
                    cancellationToken);
            }
            else
            {
                await _chatPool.InviteBotToChatAsync(
                    new(chatId), // Chat is never private when invited.
                    foulBotId,
                    invitedByUsername,
                    botFactory,
                    cancellationToken);
            }

            _logger.LogInformation("Successfully handled NewChatMember update.");
            return;
        }

        if (update.Type == UpdateType.Message
            && (update.Message?.Type == MessageType.Text || update.Message?.Type == MessageType.Document))
        {
            _logger.LogDebug("Received Message update, handling the message.");

            if (update.Message?.Chat?.Id == null)
            {
                _logger.LogWarning("Message update doesn't have required properties. Skipping handling.");
                return;
            }

            var chatId = update.Message.Chat.Id.ToString();
            var invitedByUsername = update.Message.From?.Username; // Who invited / kicked the bot.

            if (update.Message.Text == "$activate")
            {
                await _allowedChatsProvider.AllowChatAsync(GetChatId());

                // Invite the bot.
                await _chatPool.InviteBotToChatAsync(
                    GetChatId(), foulBotId, invitedByUsername, botFactory, cancellationToken);
                return; // Do not add it to context.
            }

            if (update.Message.Text == "$deactivate")
            {
                await _allowedChatsProvider.DisallowChatAsync(GetChatId());

                // Kick the bot.
                await _chatPool.KickBotFromChatAsync(
                    GetChatId(), foulBotId, cancellationToken);
                return; // Do not add it to context.
            }

            var message = await _foulMessageFactory.CreateFromAsync(update.Message, _client);
            if (message == null)
            {
                _logger.LogDebug("FoulMessage factory returned null, skipping sending message to the chat.");
                return;
            }

            await _chatPool.HandleMessageAsync(
                GetChatId(),
                foulBotId,
                message,
                botFactory,
                cancellationToken);

            FoulChatId GetChatId()
            {
                return update.Message?.Chat.Type == ChatType.Private
                    ? new(chatId) { FoulBotId = foulBotId }
                    : new(chatId);
            }

            _logger.LogInformation("Successfully handled Message update.");
            return;
        }

        // TODO: Configure to only receive necessary types of updates.
        _logger.LogDebug("Received unnecessary update, skipping handling.");
    }

    /*private BotChatStatus ToBotChatStatus(ChatMemberStatus status)
    {
        if (status == ChatMemberStatus.Left || status == ChatMemberStatus.Kicked)
            return BotChatStatus.Left;

        return BotChatStatus.Joined;
    }*/
}
