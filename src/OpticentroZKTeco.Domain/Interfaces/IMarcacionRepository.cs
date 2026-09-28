using System.Collections.Generic;
using OpticentroZKTeco.Domain.Entities;

namespace OpticentroZKTeco.Domain.Interfaces
{
    public interface IMarcacionRepository
    {
        void GuardarLote(IReadOnlyList<Marcacion> marcaciones);
    }
}
