using System.Text;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AccountStoreDynamicTests
{
    [Fact]
    public void AccountNumbers_ReturnsEveryStoredSlotInOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"AccountStore-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(root));
        try
        {
            store.Save(7, Encoding.UTF8.GetBytes("seven"));
            store.Save(2, Encoding.UTF8.GetBytes("two"));

            Assert.Equal([2, 7], store.AccountNumbers);
            Assert.Equal(8, store.NextAccountNumber());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
