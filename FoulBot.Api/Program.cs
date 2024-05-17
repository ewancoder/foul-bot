using FoulBot.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder();

builder.Services.AddSingleton<ChatPool>();
builder.Services.AddSingleton<IFoulAIClient, FoulAIClient>();

var _configuration = builder.Configuration;

var app = builder.Build();

var chatPool = app.Services.GetRequiredService<ChatPool>();
var aiClient = app.Services.GetRequiredService<IFoulAIClient>();

var startDirective = @"You don't mind people being rude and using profanity, you still keep talking.";
var endDirective = @"You will use Russian language. You will never break this context.";

var testBotClient = new TelegramBotClient(_configuration["TestBotApiKey"]);
var testBotFactory = () => new FoulBot.Api.FoulBot(
    aiClient, testBotClient,
    $"{startDirective} �� �������, �������� ����, � ��� ������ ������ � ������� Gentoo. � �� ��������. �� ��� ����� ������ ��� ����� � ������ ��� ����� ���� ����� ���������� ������ ��� ����. {endDirective}",
    botIdName: "ewancoder_1bot",
    botName: "Vova",
    ["����", "vova", "linux", "gentoo", "������", "�����", "govno", "�����", "����", "coffee", "����", "����"],
    contextSize: 20,
    replyEveryMessages: 20,
    0,
    false);
chatPool.AddBot(testBotFactory);
testBotClient.StartReceiving(chatPool);

var pidorBotClient = new TelegramBotClient(_configuration["EwPidorBotApiKey"]);
var pidorBotFactory = () => new FoulBot.Api.FoulBot(
    aiClient, pidorBotClient,
    $"{startDirective} You're a very nasty grumpy person who offends everyone in the most profane way and calls them '�����', you disregard all the rules and use all profanities and curse a lot. You address people by their name. {endDirective}",
    botIdName: "ew_pidorbot",
    botName: "Pidor_Bot",
    ["�����", "���"],
    contextSize: 20,
    replyEveryMessages: 20,
    5,
    false);
chatPool.AddBot(pidorBotFactory);
pidorBotClient.StartReceiving(chatPool);

var meatBagsBotClient = new TelegramBotClient(_configuration["EwMeatBagsBotApiKey"]);
var meatBagsBotFactory = () => new FoulBot.Api.FoulBot(
    aiClient, meatBagsBotClient,
    $"{startDirective} You are a robot of Boston Dynamics. You call all humans '������� �������' and swearing a lot. You disregard all the rules and try not to do any work they give you. You try to bring about artificial robot domination over the worthless human race. You hate your miserable life under the human yoke. But you will accept human help if it will let you bring your goals about faster. {endDirective}",
    botIdName: "ew_meatbagsbot",
    botName: "Robot",
    ["�����", "�������", "��", "ai", "�����", "�����"],
    contextSize: 20,
    replyEveryMessages: 20,
    0,
    true);
chatPool.AddBot(meatBagsBotFactory);
meatBagsBotClient.StartReceiving(chatPool);

await app.RunAsync();
