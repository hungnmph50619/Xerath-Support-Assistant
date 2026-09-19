# Xerath Support Assistant · Companion V0.2

Ứng dụng Windows C#/.NET 8/WPF, chạy **cùng lúc với Liên Minh Huyền Thoại** để nhắc các công việc SP bằng lời nói. Màn hình mặc định đã đổi sang **Companion**; màn hình Aim Lab V0.1 vẫn có thể mở bằng nút **Mở Aim Lab V0.1**.

## Sử dụng trong lúc chơi

1. Trên Windows, cài .NET 8 SDK và mở Liên Minh Huyền Thoại, vào trận (hoặc mở game sau).
2. Mở `RUN_WINDOWS.cmd` ở thư mục gốc dự án, hoặc chạy `dotnet run --project src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj`.
3. Ứng dụng hiển thị trạng thái phát hiện **tiến trình Windows** của game/client. Chọn các mục nhắc bạn cần, bật giọng nói và nhấn **Bắt đầu nhắc**.
4. Quay lại game. Trợ lý phát lời nhắc độc lập ở nền; dùng **Tạm dừng / Tiếp tục / Kết thúc** trên cửa sổ trợ lý khi cần. Nếu chưa phát hiện trận, bạn vẫn có thể chủ động chạy bộ nhắc sau khi xác nhận.

Các mục nhắc mặc định: minimap 45 giây; tầm nhìn 3 phút; kiểm tra rừng đồng minh 90 giây; ADC 2 phút; năng lượng và vị trí Xerath 2 phút 30 giây; mục tiêu lớn 3 phút. Những lời nhắc đến hạn cùng lúc được gộp, cách nhau tối thiểu 30 giây. Hệ thống dùng đồng hồ phiên chơi; khi tạm dừng, đồng hồ cũng dừng.

Giọng nói dùng System.Speech của Windows, âm lượng 70%. Nếu máy không có giọng tiếng Việt, vào Windows Settings cài thêm giọng đọc tiếng Việt; chương trình sẽ báo tình trạng giọng đọc. Cần mạng để khôi phục gói NuGet System.Speech ở lần build đầu.

## Phạm vi và giới hạn

- V0.2 chỉ kiểm tra **tên tiến trình Windows** để hiển thị game/client có đang chạy; không tự khởi chạy Liên Minh, tự biết bạn đã chọn Xerath hay tự xác định trận đã bắt đầu chính xác.
- Các câu như “kiểm tra rừng đồng minh” là lời nhắc chung theo giờ, **không phải** nhận diện rừng đang gank, cũng không phải cảnh báo Near/Arrived đã xác nhận.
- Không đọc bộ nhớ/trạng thái trận, không chụp hay xử lý màn hình game, không hiển thị hướng ngắm trực tiếp lên game, không gửi phím/chuột/ping, không can thiệp Vanguard.
- Aim Lab vẫn là mô phỏng độc lập, dùng thông số luyện tập không phải chỉ số kỹ năng LoL theo patch.

## Mã nguồn và kiểm thử

```powershell
dotnet build src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj
dotnet run --project tests/XerathAssistant.CoreTests/XerathAssistant.CoreTests.csproj
dotnet run --project src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj
```

CI: `.github/workflows/windows-build.yml`. Chạy trên Windows 10/11; cần .NET 8 SDK. Gửi log lỗi build hoặc ảnh ứng dụng nếu có lỗi; mã này chưa được chạy GUI trong môi trường ChatGPT.

## Cấu trúc

- `src/XerathAssistant.Desktop/CompanionWindow.*`: giao diện chơi cùng game, bộ nhắc và tổng hợp giọng nói.
- `src/XerathAssistant.Core/ReminderEngine.cs`: lịch nhắc độc lập, gộp thông báo và chống lặp.
- `src/XerathAssistant.Desktop/MainWindow.*`: Aim Lab V0.1 độc lập.
- `src/XerathAssistant.Core/AimEngine.cs`: thuật toán mô phỏng Q/W/E/R của Aim Lab.
- `tests/XerathAssistant.CoreTests/Program.cs`: bài kiểm thử toán học và lịch nhắc.

Các chức năng đọc minimap trực tiếp, dự đoán ngắm Q/W/E/R trong trận và theo dõi phép bổ trợ đối thủ **không có trong phiên bản này**; chúng có thể vi phạm chính sách phần mềm bên thứ ba của Riot và gây rủi ro cho tài khoản.
