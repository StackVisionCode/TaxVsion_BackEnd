namespace TaxVision.Notification.Tests.Deploy;

/// <summary>
/// El reintento del push de Client Forms se programa con <c>ScheduleAsync</c> a una cola local. Sin cola
/// durable, Wolverine lo guarda en memoria y un reinicio lo pierde (P-21.3.1 pide un mensaje local durable).
/// </summary>
public sealed class NotificationMessagingWiringTests
{
    [Fact]
    public void Client_form_push_retry_uses_a_durable_local_queue()
    {
        var program = ReadRepoFile("src/Services/Notification/TaxVision.Notification.Api/Program.cs");

        Assert.Matches(@"LocalQueueFor<[\w.]*RetryClientFormPushRequested>\(\)\s*\.UseDurableInbox\(\)", program);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"No se encontró '{relativePath}' subiendo desde {AppContext.BaseDirectory}.");
    }
}
