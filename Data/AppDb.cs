using CanonSeSu.Api.Data.Models;
using LinqToDB;
using LinqToDB.Data;

namespace CanonSeSu.Api.Data;

public class AppDb(string connectionString)
    : DataConnection(ProviderName.PostgreSQL, connectionString)
{
    public ITable<ServiceDeviceCounter> ServiceDeviceCounters => this.GetTable<ServiceDeviceCounter>();
}
