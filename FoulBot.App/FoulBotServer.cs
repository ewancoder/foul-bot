﻿using Microsoft.Extensions.DependencyInjection;

namespace FoulBot.App;

public sealed class FoulBotServer
{
    private FoulBotServer() { }

    public static async Task StartAsync(CancellationToken cancellationToken)
    {
        var isDebug = false;
#if DEBUG
        isDebug = true;
#endif
        var isInMemory = false; // Enable this to debug without Redis etc.

        var builder = FoulBotServerBuilder.Create(isDebug, isInMemory);

        builder.Services
            .AddScoped<ChatLoader>()
            .AddScoped<ApplicationInitializer>()
            .RegisterBots(builder.Configuration, isDebug);

        {
            using var rootProvider = builder.BuildServiceProvider();
            await using var scope = rootProvider.CreateAsyncScope(); // We need this, root container doesn't work with IAsyncDisposable.
            var provider = scope.ServiceProvider;
            using var localCts = new CancellationTokenSource();
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, localCts.Token);

            var logger = provider.GetRequiredService<ILogger<FoulBotServer>>();

            var appInitializer = rootProvider.GetRequiredService<ApplicationInitializer>();
            try
            {
                await appInitializer.InitializeAsync(combinedCts.Token);

                logger.LogInformation("Application started.");
                await Task.Delay(Timeout.Infinite, combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Graceful shutdown initiated.");
                await appInitializer.GracefullyShutdownAsync();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error happened during initialization.");
            }
            finally
            {
                logger.LogInformation("Exiting...");

                // Provider is not disposed yet. We can afford graceful shutdown.
                await localCts.CancelAsync(); // Cancels combinedCts.Token.

                logger.LogInformation("Application stopped.");
            }
        }

        // Provider and CTSs are disposed here.
    }
}
