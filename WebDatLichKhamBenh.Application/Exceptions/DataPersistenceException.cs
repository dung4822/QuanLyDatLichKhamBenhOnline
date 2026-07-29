namespace WebDatLichKhamBenh.Application.Exceptions;

public class DataPersistenceException : Exception
{
    public DataPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
