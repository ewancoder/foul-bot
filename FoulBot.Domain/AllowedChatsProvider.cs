﻿using System.Collections.Concurrent;

namespace FoulBot.Domain;

public interface IAllowedChatsProvider
{
    ValueTask<bool> IsAllowedChatAsync(FoulChatId chatId);
    ValueTask AllowChatAsync(FoulChatId chatId);
    ValueTask DisallowChatAsync(FoulChatId chatId);
}

public sealed class AllowedChatsProvider : IAllowedChatsProvider, IDisposable
{
    private readonly ILogger<AllowedChatsProvider> _logger;
    private readonly string _fileName;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConcurrentDictionary<FoulChatId, bool>? _allowedChats;

    public AllowedChatsProvider(
        ILogger<AllowedChatsProvider> logger,
        string fileName = "allowed_chats")
    {
        _fileName = fileName;
        _logger = logger;
    }

    public async ValueTask<bool> IsAllowedChatAsync(FoulChatId chatId)
    {
        var allowedChats = await GetAllowedChatsAsync();

        return allowedChats.ContainsKey(chatId);
    }

    public async ValueTask AllowChatAsync(FoulChatId chatId)
    {
        _logger.LogWarning("Allowing chat {ChatId}", chatId.Value);

        var allowedChats = await GetAllowedChatsAsync();
        allowedChats.TryAdd(chatId, false);

        await SaveChangesAsync();
    }

    public async ValueTask DisallowChatAsync(FoulChatId chatId)
    {
        _logger.LogWarning("Disallowing chat {ChatId}", chatId.Value);

        var allowedChats = await GetAllowedChatsAsync();
        allowedChats.TryRemove(chatId, out _);

        await SaveChangesAsync();
    }

    private async ValueTask SaveChangesAsync()
    {
        var allowedChats = await GetAllowedChatsAsync();

        var serialized = JsonSerializer.Serialize(
            allowedChats.Keys.Select(chatId => chatId.Value));

        await _lock.WaitAsync();
        try
        {
            _logger.LogDebug("Saving list of allowed chats: {SerializedAllowedChats}", serialized);
            await File.WriteAllTextAsync(_fileName, serialized);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private async ValueTask<ConcurrentDictionary<FoulChatId, bool>> GetAllowedChatsAsync()
    {
        if (_allowedChats != null)
            return _allowedChats;

        await _lock.WaitAsync();
        try
        {
#pragma warning disable CA1508 // False positive: It is updated below.
            if (_allowedChats != null)
                return _allowedChats;
#pragma warning restore CA1508

            if (!File.Exists(_fileName))
                await File.WriteAllTextAsync(_fileName, "[]");

            var fileContent = await File.ReadAllTextAsync(_fileName);
            var chats = JsonSerializer.Deserialize<string[]>(fileContent)?.Distinct()
                ?? throw new InvalidOperationException("Failed to deserialize allowed chats.");

            _allowedChats = new ConcurrentDictionary<FoulChatId, bool>(
                chats.Select(chat => new KeyValuePair<FoulChatId, bool>(new(chat), false)));

            return _allowedChats;
        }
        finally
        {
            _lock.Release();
        }
    }
}
