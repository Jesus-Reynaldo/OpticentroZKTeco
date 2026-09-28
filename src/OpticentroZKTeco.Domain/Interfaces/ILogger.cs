namespace OpticentroZKTeco.Domain.Interfaces
{
    public interface ILogger
    {
        void Info(string mensaje);
        void Error(string mensaje);
    }
}
