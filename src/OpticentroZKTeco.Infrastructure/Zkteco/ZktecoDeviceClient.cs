using System;
using System.Collections.Generic;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Enums;
using OpticentroZKTeco.Domain.Interfaces;
using zkemkeeper;

namespace OpticentroZKTeco.Infrastructure.Zkteco
{
    public class ZktecoDeviceClient : IDispositivoAsistencia
    {
        private readonly string _ip;
        private readonly int _puerto;
        private readonly int _machineNumber;
        private readonly int? _password;
        private readonly CZKEM _dispositivo;
        private bool _conectado;

        public ZktecoDeviceClient(string ip, int puerto, int machineNumber = 1, int? password = null)
        {
            _ip = ip;
            _puerto = puerto;
            _machineNumber = machineNumber;
            _password = password;
            _dispositivo = new CZKEMClass();
        }

        public bool Conectar()
        {
            if (_password.HasValue)
            {
                _dispositivo.SetCommPassword(_password.Value);
            }

            _conectado = _dispositivo.Connect_Net(_ip, _puerto);
            return _conectado;
        }

        public IReadOnlyList<Marcacion> DescargarMarcaciones()
        {
            if (!_conectado)
            {
                return new List<Marcacion>();
            }

            var marcaciones = new List<Marcacion>();

            _dispositivo.EnableDevice(_machineNumber, false);
            try
            {
                bool hayDatos = _dispositivo.ReadGeneralLogData(_machineNumber);
                if (!hayDatos)
                {
                    return marcaciones;
                }

                string enrollNumber;
                int verifyMode, inOutMode, year, month, day, hour, minute, second;
                int workCode = 0;

                while (_dispositivo.SSR_GetGeneralLogData(
                    _machineNumber,
                    out enrollNumber,
                    out verifyMode,
                    out inOutMode,
                    out year,
                    out month,
                    out day,
                    out hour,
                    out minute,
                    out second,
                    ref workCode))
                {
                    var fecha = new DateTime(year, month, day, hour, minute, second);
                    var verificacion = (ModoVerificacion)verifyMode;
                    var tipoMarca = (ModoEntradaSalida)inOutMode;

                    marcaciones.Add(new Marcacion(enrollNumber, verificacion, tipoMarca, fecha));
                }
            }
            finally
            {
                _dispositivo.EnableDevice(_machineNumber, true);
            }

            return marcaciones;
        }

        public void Desconectar()
        {
            if (_conectado)
            {
                _dispositivo.Disconnect();
                _conectado = false;
            }
        }

        public string ObtenerUltimoError()
        {
            int errorCode = 0;
            _dispositivo.GetLastError(ref errorCode);
            return errorCode.ToString();
        }
    }
}
