using System.Collections.Generic;
using OpticentroZKTeco.Domain.Entities;

namespace OpticentroZKTeco.Domain.Interfaces
{
    public interface IDispositivoAsistencia
    {
        bool Conectar();
        IReadOnlyList<Marcacion> DescargarMarcaciones();
        void Desconectar();
    }
}
