using System.Threading.Tasks;

namespace PurchaseFlow.Data;

public interface IPurchaseFlowDbSchemaMigrator
{
    Task MigrateAsync();
}

