using System;
using System.Collections.Generic;
using OpticentroZKTeco.Domain.Entities;

namespace OpticentroZKTeco.Domain.Interfaces
{
    public interface ISincronizacionMarcador
    {
        bool YaSincronizadoHoy();
        IReadOnlyCollection<DateTime> ObtenerFechasSincronizadas();
        void MarcarComoSincronizado(IReadOnlyList<DateTime> fechasDatos, IReadOnlyList<Marcacion> marcaciones, bool esManual);
    }
}
