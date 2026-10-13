# Build image cho API. Dùng trong deploy/docker-compose.yml (xem docs/deploy/ORACLE_CLOUD.md).
# Image Debian (không dùng alpine) vì cần ICU + tzdata cho tiếng Việt và múi giờ Asia/Ho_Chi_Minh.

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

# Chạy EF migration trước khi API khởi động (service `migrate` trong docker-compose).
# Bundle phải chạy trong /src vì AppDbContextFactory đọc WebDatLichKhamBenh.Api/appsettings.json;
# connection string thật lấy từ biến môi trường ConnectionStrings__DefaultConnection.
FROM build AS migrations
RUN dotnet tool install --global dotnet-ef --version 8.0.10
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api -o /src/efbundle --force
ENTRYPOINT ["/src/efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Thư mục khóa Data Protection (gắn volume). Tạo sẵn với quyền của user `app` để volume mới kế thừa quyền này.
RUN mkdir /keys && chown app:app /keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebDatLichKhamBenh.Api.dll"]
