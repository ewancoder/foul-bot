﻿using FoulBot.App;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.CancelAsync();
};

try
{
    await LegacyProgram.LegacyMain();
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Exiting...");
}
