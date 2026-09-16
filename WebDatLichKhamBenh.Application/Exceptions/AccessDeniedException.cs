namespace WebDatLichKhamBenh.Application.Exceptions;

public class AccessDeniedException : Exception
{
    public AccessDeniedException() : base("Bạn không có quyền thực hiện thao tác này.") { }
}
