﻿namespace FoulBot.Domain;

public interface IChatStore
{
    public void AddChat(FoulChatId chatId);
}
