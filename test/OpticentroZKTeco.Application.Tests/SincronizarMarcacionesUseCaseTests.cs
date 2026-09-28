using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using OpticentroZKTeco.Application.UseCases;
using OpticentroZKTeco.Domain.Entities;
using OpticentroZKTeco.Domain.Enums;
using OpticentroZKTeco.Domain.Interfaces;

namespace OpticentroZKTeco.Application.Tests
{
    [TestFixture]
    public class SincronizarMarcacionesUseCaseTests
    {
        private static readonly DateTime Ayer = DateTime.Today.AddDays(-1);
        private static readonly DateTime Anteayer = DateTime.Today.AddDays(-2);

        private Mock<IDispositivoAsistencia> _dispositivoMock;
        private Mock<IMarcacionRepository> _repositorioMock;
        private Mock<ILogger> _loggerMock;
        private Mock<ISincronizacionMarcador> _marcadorMock;
        private Mock<IAvisoVisual> _avisoMock;

        [SetUp]
        public void SetUp()
        {
            _dispositivoMock = new Mock<IDispositivoAsistencia>();
            _repositorioMock = new Mock<IMarcacionRepository>();
            _loggerMock = new Mock<ILogger>();
            _marcadorMock = new Mock<ISincronizacionMarcador>();
            _avisoMock = new Mock<IAvisoVisual>();

            _marcadorMock.Setup(m => m.YaSincronizadoHoy()).Returns(false);
        }

        private SincronizarMarcacionesUseCase CrearUseCase(int diasRecuperacion = SincronizarMarcacionesUseCase.DiasRecuperacionPorDefecto)
        {
            return new SincronizarMarcacionesUseCase(
                _dispositivoMock.Object, _repositorioMock.Object, _loggerMock.Object, _marcadorMock.Object, _avisoMock.Object, diasRecuperacion);
        }

        private void DispositivoConMarcaciones(params Marcacion[] marcaciones)
        {
            _dispositivoMock.Setup(d => d.Conectar()).Returns(true);
            _dispositivoMock.Setup(d => d.DescargarMarcaciones()).Returns(marcaciones.ToList());
        }

        // Todos los dias de la ventana de recuperacion ya sincronizados, excepto los indicados.
        private void SincronizadoExcepto(params DateTime[] pendientes)
        {
            List<DateTime> sincronizadas = Enumerable.Range(0, SincronizarMarcacionesUseCase.DiasRecuperacionPorDefecto)
                .Select(i => Ayer.AddDays(-i))
                .Except(pendientes)
                .ToList();
            _marcadorMock.Setup(m => m.ObtenerFechasSincronizadas()).Returns(sincronizadas);
        }

        private static Marcacion Marca(string usuarioId, DateTime fecha)
        {
            return new Marcacion(usuarioId, ModoVerificacion.Huella, ModoEntradaSalida.Entrada, fecha);
        }

        private static bool SonFechas(IReadOnlyList<DateTime> actuales, params DateTime[] esperadas)
        {
            return actuales.SequenceEqual(esperadas);
        }

        [Test]
        public void Ejecutar_FlujoFeliz_GuardaSoloMarcacionesDeAyerYDesconecta()
        {
            SincronizadoExcepto(Ayer);
            DispositivoConMarcaciones(
                Marca("1", Ayer.AddHours(8)),
                Marca("2", Anteayer.AddHours(18)),
                Marca("3", Ayer.AddHours(17)));

            CrearUseCase().Ejecutar();

            _repositorioMock.Verify(r => r.GuardarLote(It.Is<IReadOnlyList<Marcacion>>(l => l.Count == 2)), Times.Once);
            _dispositivoMock.Verify(d => d.Desconectar(), Times.Once);
            _loggerMock.Verify(l => l.Info(It.IsAny<string>()), Times.Once);
            _loggerMock.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(
                It.Is<IReadOnlyList<DateTime>>(f => SonFechas(f, Ayer)),
                It.Is<IReadOnlyList<Marcacion>>(l => l.Count == 2),
                false), Times.Once);
            _avisoMock.Verify(a => a.Mostrar(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void Ejecutar_DiaAnteriorSinSincronizar_RecuperaAmbosDiasEnUnSoloLote()
        {
            // Caso real: el sabado no se envio porque la PC estuvo apagada el domingo.
            SincronizadoExcepto(Anteayer, Ayer);
            DispositivoConMarcaciones(
                Marca("1", Anteayer.AddHours(8)),
                Marca("1", Anteayer.AddHours(13)),
                Marca("2", Ayer.AddHours(9)),
                Marca("3", DateTime.Today.AddDays(-5).AddHours(8)));

            CrearUseCase().Ejecutar();

            _repositorioMock.Verify(r => r.GuardarLote(It.Is<IReadOnlyList<Marcacion>>(
                l => l.Count == 3 && l.All(m => m.Fecha.Date == Anteayer || m.Fecha.Date == Ayer))), Times.Once);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(
                It.Is<IReadOnlyList<DateTime>>(f => SonFechas(f, Anteayer, Ayer)),
                It.Is<IReadOnlyList<Marcacion>>(l => l.Count == 3),
                false), Times.Once);
        }

        [Test]
        public void Ejecutar_SinMarcadoresPrevios_NoVaMasAllaDeLaVentanaDeRecuperacion()
        {
            _marcadorMock.Setup(m => m.ObtenerFechasSincronizadas()).Returns(new List<DateTime>());
            DispositivoConMarcaciones(
                Marca("1", DateTime.Today.AddDays(-3).AddHours(8)),
                Marca("2", DateTime.Today.AddDays(-4).AddHours(8)));

            CrearUseCase(diasRecuperacion: 3).Ejecutar();

            _repositorioMock.Verify(r => r.GuardarLote(It.Is<IReadOnlyList<Marcacion>>(
                l => l.Count == 1 && l[0].UsuarioId == "1")), Times.Once);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(
                It.Is<IReadOnlyList<DateTime>>(f => SonFechas(f, DateTime.Today.AddDays(-3), Anteayer, Ayer)),
                It.IsAny<IReadOnlyList<Marcacion>>(),
                false), Times.Once);
        }

        [Test]
        public void Ejecutar_SinDiasPendientes_NoContactaDispositivoYMarcaElDia()
        {
            SincronizadoExcepto();

            CrearUseCase().Ejecutar();

            _dispositivoMock.Verify(d => d.Conectar(), Times.Never);
            _repositorioMock.Verify(r => r.GuardarLote(It.IsAny<IReadOnlyList<Marcacion>>()), Times.Never);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(
                It.Is<IReadOnlyList<DateTime>>(f => f.Count == 0),
                It.Is<IReadOnlyList<Marcacion>>(l => l.Count == 0),
                false), Times.Once);
        }

        [Test]
        public void Ejecutar_FalloDeConexion_NoDescargaNiGuardaYLoguearError()
        {
            SincronizadoExcepto(Ayer);
            _dispositivoMock.Setup(d => d.Conectar()).Returns(false);

            CrearUseCase().Ejecutar();

            _dispositivoMock.Verify(d => d.DescargarMarcaciones(), Times.Never);
            _repositorioMock.Verify(r => r.GuardarLote(It.IsAny<IReadOnlyList<Marcacion>>()), Times.Never);
            _dispositivoMock.Verify(d => d.Desconectar(), Times.Never);
            _loggerMock.Verify(l => l.Error(It.IsAny<string>()), Times.Once);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IReadOnlyList<Marcacion>>(), It.IsAny<bool>()), Times.Never);
            _avisoMock.Verify(a => a.Mostrar(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Ejecutar_FalloDelErp_NoMarcaParaReintentarLaProximaVez()
        {
            SincronizadoExcepto(Ayer);
            DispositivoConMarcaciones(Marca("1", Ayer.AddHours(8)));
            _repositorioMock.Setup(r => r.GuardarLote(It.IsAny<IReadOnlyList<Marcacion>>())).Throws(new Exception("ERP caido"));

            CrearUseCase().Ejecutar();

            _dispositivoMock.Verify(d => d.Desconectar(), Times.Once);
            _loggerMock.Verify(l => l.Error(It.IsAny<string>()), Times.Once);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IReadOnlyList<Marcacion>>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void Ejecutar_YaSincronizadoHoy_NoContactaDispositivoNiRepositorio()
        {
            _marcadorMock.Setup(m => m.YaSincronizadoHoy()).Returns(true);

            CrearUseCase().Ejecutar();

            _dispositivoMock.Verify(d => d.Conectar(), Times.Never);
            _dispositivoMock.Verify(d => d.DescargarMarcaciones(), Times.Never);
            _dispositivoMock.Verify(d => d.Desconectar(), Times.Never);
            _repositorioMock.Verify(r => r.GuardarLote(It.IsAny<IReadOnlyList<Marcacion>>()), Times.Never);
            _loggerMock.Verify(l => l.Info(It.IsAny<string>()), Times.Once);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(It.IsAny<IReadOnlyList<DateTime>>(), It.IsAny<IReadOnlyList<Marcacion>>(), It.IsAny<bool>()), Times.Never);
            _avisoMock.Verify(a => a.Mostrar(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Ejecutar_SinMarcacionesDeAyer_NoGuardaLoteYAunAsiMarca()
        {
            SincronizadoExcepto(Ayer);
            DispositivoConMarcaciones(Marca("2", Anteayer.AddHours(18)));

            CrearUseCase().Ejecutar();

            _repositorioMock.Verify(r => r.GuardarLote(It.IsAny<IReadOnlyList<Marcacion>>()), Times.Never);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(
                It.Is<IReadOnlyList<DateTime>>(f => SonFechas(f, Ayer)),
                It.Is<IReadOnlyList<Marcacion>>(l => l.Count == 0),
                false), Times.Once);
            _dispositivoMock.Verify(d => d.Desconectar(), Times.Once);
            _avisoMock.Verify(a => a.Mostrar(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void EjecutarManual_AunqueYaSeSincronizoHoy_EnviaSoloElRangoYMarcaComoManual()
        {
            _marcadorMock.Setup(m => m.YaSincronizadoHoy()).Returns(true);
            DateTime sabado = DateTime.Today.AddDays(-3);
            DispositivoConMarcaciones(
                Marca("1", sabado.AddHours(8)),
                Marca("2", sabado.AddHours(12)),
                Marca("3", Ayer.AddHours(8)));

            CrearUseCase().EjecutarManual(sabado, sabado);

            _repositorioMock.Verify(r => r.GuardarLote(It.Is<IReadOnlyList<Marcacion>>(
                l => l.Count == 2 && l.All(m => m.Fecha.Date == sabado))), Times.Once);
            _marcadorMock.Verify(m => m.MarcarComoSincronizado(
                It.Is<IReadOnlyList<DateTime>>(f => SonFechas(f, sabado)),
                It.Is<IReadOnlyList<Marcacion>>(l => l.Count == 2),
                true), Times.Once);
            _marcadorMock.Verify(m => m.YaSincronizadoHoy(), Times.Never);
            _dispositivoMock.Verify(d => d.Desconectar(), Times.Once);
        }
    }
}
