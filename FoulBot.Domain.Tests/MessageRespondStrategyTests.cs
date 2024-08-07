﻿using System.Reflection;
using AutoFixture.Kernel;

namespace FoulBot.Domain.Tests;

public class MessageRespondStrategyTests
{
    private readonly IFixture _fixture;
    private readonly MessageRespondStrategy _sut;
    private readonly IRespondStrategyConfiguration _configuration;

    public MessageRespondStrategyTests()
    {
        _fixture = AutoMoqDataAttribute.CreateFixture();
        _configuration = _fixture.Freeze<IRespondStrategyConfiguration>();

        var mrsBuilder = new MessageRespondStrategyBuilder(false);
        _fixture.Customizations.Add(mrsBuilder);
        _sut = _fixture.Create<MessageRespondStrategy>();
        _fixture.Customizations.Remove(mrsBuilder);
    }

    [Fact]
    public void GetReason_ShouldReturnNull_WhenSenderIsCurrentBot()
    {
        Assert.Null(_sut.GetReasonForResponding(CreateMessage(_configuration.BotName)));
    }

    [Fact]
    public void GetReason_ShouldReturnReply_WhenMessageIsAReplyToTheBot()
    {
        Assert.Equal("Reply", _sut.GetReasonForResponding(CreateMessage("sender", _configuration.BotId)));
    }

    [Fact]
    public void GetReason_ShouldStillReturnNull_WhenMessageIsAReplyToItselfFromABot()
    {
        Assert.Null(_sut.GetReasonForResponding(CreateMessage(_configuration.BotName, _configuration.BotId)));
    }

    [Fact]
    public void GetReason_ShouldReturnPrivateChat_WhenIsPrivateChatIsTrue()
    {
        _fixture.Customizations.Add(new MessageRespondStrategyBuilder(true));
        var sut = _fixture.Create<MessageRespondStrategy>();

        Assert.Equal("Private chat", sut.GetReasonForResponding(CreateMessage()));
    }

    [Fact]
    public void GetReason_ShouldReturnTriggerWord_WhenTriggeredByKeyword()
    {
        var result = _sut.GetReasonForResponding(CreateMessage(text: string.Join(',', _configuration.KeyWords)));

        Assert.Equal($"Trigger word: '{_configuration.KeyWords.First()}'", result);
    }

    [Fact]
    public void GetReason_ShouldReturnNull_WhenNoKeywordsArePresent()
    {
        Assert.Null(_sut.GetReasonForResponding(CreateMessage()));
    }

    [Theory, AutoMoqData]
    public void ShouldRespond_ShouldReturnFalse_WhenNoneOfMessagesHaveReasonToRespondTo(
        List<FoulMessage> messages)
    {
        Assert.False(_sut.ShouldRespond(messages));
        Assert.False(_sut.ShouldRespond(messages[0]));
    }

    [Theory, AutoMoqData]
    public void ShouldRespond_ShouldReturnTrue_WhenAtLeastOneMessageHasReasonToRespondTo(
        List<FoulMessage> messages)
    {
        messages[1].ReplyTo = _configuration.BotId;

        Assert.True(_sut.ShouldRespond(messages));
        Assert.True(_sut.ShouldRespond(messages[1]));
    }

    private FoulMessage CreateMessage(string? senderName = null, string? replyTo = null, string? text = null)
    {
        return new FoulMessage("id", FoulMessageType.Bot, senderName ?? _fixture.Create<string>(), text ?? _fixture.Create<string>(), DateTime.UtcNow, true)
        {
            ReplyTo = replyTo
        };
    }
}

public sealed class MessageRespondStrategyBuilder : ISpecimenBuilder
{
    private readonly bool _isPrivateChat;

    public MessageRespondStrategyBuilder(bool isPrivateChat)
    {
        _isPrivateChat = isPrivateChat;
    }

    public object Create(object request, ISpecimenContext context)
    {
        if (request is not ParameterInfo pi)
            return new NoSpecimen();

        if (pi.Member.DeclaringType != typeof(MessageRespondStrategy)
            || pi.ParameterType != typeof(bool)
            || pi.Name != "isPrivateChat")
            return new NoSpecimen();

        return _isPrivateChat;
    }
}
