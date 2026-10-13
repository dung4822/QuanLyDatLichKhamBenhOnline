# Build image cho API để deploy lên Render (xem docs/deploy/DEPLOY_FREE.md).
# Image Debian (không dùng alpine) vì cần ICU + tzdata cho tiếng Việt và múi giờ Asia/Ho_Chi_Minh.

# Bước 1: build bằng image có đủ .NET SDK.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Restore trước để Docker cache lại package khi chỉ sửa code.
COPY WebDatLichKhamBenh.Api/WebDatLichKhamBenh.Api.csproj WebDatLichKhamBenh.Api/
COPY WebDatLichKhamBenh.Application/WebDatLichKhamBenh.Application.csproj WebDatLichKhamBenh.Application/
COPY WebDatLichKhamBenh.Domain/WebDatLichKhamBenh.Domain.csproj WebDatLichKhamBenh.Domain/
COPY WebDatLichKhamBenh.Infrastructure/WebDatLichKhamBenh.Infrastructure.csproj WebDatLichKhamBenh.Infrastructure/
RUN dotnet restore WebDatLichKhamBenh.Api/WebDatLichKhamBenh.Api.csproj
COPY . .
RUN dotnet publish WebDatLichKhamBenh.Api/WebDatLichKhamBenh.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Bước 2: chạy bằng image nhẹ, chỉ có ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
# Render nhận HTTPS rồi chuyển vào container bằng HTTP, kèm header X-Forwarded-Proto/For.
# Bật cờ này để ASP.NET hiểu request gốc là HTTPS (cookie Secure + antiforgery mới hoạt động).
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebDatLichKhamBenh.Api.dll"]
