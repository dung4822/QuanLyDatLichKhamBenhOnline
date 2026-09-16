namespace WebDatLichKhamBenh.Application.Exceptions;

public class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException(string message = "Vui lòng đăng nhập lại.") : base(message) { }
}
