﻿using FoulBot.Api;
using FoulBot.Domain;
using FoulBot.Infrastructure;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace FoulBot.App;

public sealed class ChatLoader : IChatCache
{
    private readonly ILogger<ChatLoader> _logger;
    private readonly IFoulBotFactory _botFactory;
    private readonly ITelegramBotMessengerFactory _messengerFactory;
    private readonly HashSet<string> _chatIds;
    private readonly object _lock = new object();

    public ChatLoader(
        ILogger<ChatLoader> logger,
        IFoulBotFactory botFactory,
        ITelegramBotMessengerFactory messengerFactory)
    {
        _logger = logger;
        _botFactory = botFactory;
        _messengerFactory = messengerFactory;

        _chatIds = File.Exists("chats")
            ? File.ReadAllText("chats").Split(',').ToHashSet() // HACK: Blocking call.
            : new HashSet<string>();
    }

    public async Task LoadBotToChatAsync(
        TelegramBotClient client,
        ChatPool chatPool,
        FoulBotConfiguration configuration)
    {
        foreach (var chatId in _chatIds)
        {
            // If chat is private - only add the bot that belongs there.
            if (chatId.Contains('$') && chatId.Split('$')[1] != configuration.BotId)
                continue;

            await chatPool.InitializeChatAndBotAsync(
                configuration.BotId,
                chatId,
                chat => _botFactory.Create(_messengerFactory.Create(client), configuration, chat));
        }
    }

    // A hacky way to avoid nasty deadlocks.
    public void AddChat(string chatId)
    {
        _ = Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    _chatIds.Add(chatId);
                    System.IO.File.WriteAllText("chats", string.Join(',', _chatIds));
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error when trying to cache chats to file.");
            }
        });
    }
}
