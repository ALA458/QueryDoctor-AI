using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

/// <summary>Auditoría B5: una conexión nueva no nace activa; solo el sondeo la activa (RF-CFG-02).</summary>
public class ConnectionDefaultsTests
{
    [Fact]
    public void UnaConexionNuevaNaceEnPending()
    {
        Assert.Equal(ConnectionStatus.Pending, new Connection().Status);
    }
}
