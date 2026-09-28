using System;
using OpticentroZKTeco.Domain.Enums;

namespace OpticentroZKTeco.Domain.Entities
{
    public class Marcacion
    {
        public string UsuarioId { get; }
        public ModoVerificacion Verificacion { get; }
        public ModoEntradaSalida TipoMarca { get; }
        public DateTime Fecha { get; }

        public Marcacion(string usuarioId, ModoVerificacion verificacion, ModoEntradaSalida tipoMarca, DateTime fecha)
        {
            UsuarioId = usuarioId;
            Verificacion = verificacion;
            TipoMarca = tipoMarca;
            Fecha = fecha;
        }
    }
}
